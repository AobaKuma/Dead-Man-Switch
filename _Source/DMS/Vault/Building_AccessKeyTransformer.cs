using System.Collections.Generic;
using System.Text;
using Fortified;
using RimWorld;
using Verse;
using Verse.Sound;

namespace DMS
{
    /// <summary>
    /// 被門禁鑰匙解鎖後「換成另一個建築」的佔位建築。
    /// A placeholder building that is replaced by another building once an access key unlocks it.
    ///
    /// 它是 FFF 門禁系統的 wanter 端（<see cref="IAccessKeyWanter"/>），由 access console
    /// （<see cref="CompAccessKeyActivatable"/>）透過 <see cref="AccessKeyLinkUtility"/> 連結；
    /// 連上幾座控制台就需要幾張鑰匙卡。全部用掉後，依 <see cref="ModExtension_AccessKeyTransform"/>
    /// 在原地生成 unlockedDef，目前用在封板狀態的貨運電梯，解鎖後變回可進入的 DMS_VaultElevator。
    ///
    /// It is the wanter end of FFF's access-key system, linked from an access console. Every linked
    /// console costs one key card; once all are spent it spawns unlockedDef in place. Used for the
    /// sealed freight elevator pad, which becomes the enterable DMS_VaultElevator.
    /// </summary>
    public class Building_AccessKeyTransformer : Building, IAccessKeyWanter
    {
        // 與 Building_RollingDoor_AccessLink 同一套計數：-1 = 尚未被任何控制台連結。
        // Same bookkeeping as Building_RollingDoor_AccessLink: -1 = nothing has linked to us yet.
        private int countToActivate = -1;

        private bool activated;

        private ModExtension_AccessKeyTransform extCached;
        private bool extResolved;

        private ModExtension_AccessKeyTransform Ext
        {
            get
            {
                if (!extResolved)
                {
                    extCached = def.GetModExtension<ModExtension_AccessKeyTransform>();
                    extResolved = true;
                }
                return extCached;
            }
        }

        public bool Activated => activated;

        public int RemainingKeys => countToActivate < 0 ? 0 : countToActivate;

        /// <summary>找不到版面連結時，自動連結控制台的搜尋半徑。Search radius for a console when no layout link exists.</summary>
        private const float AutoLinkRadius = 12f;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            // 版面的 Task_LinkAccessKeyWanter 在整份版面生成完才跑，所以等目前的 long event（地圖生成、讀檔）結束再檢查；
            // 一般遊戲中生成時沒有 long event，會立刻執行。
            // The layout's Task_LinkAccessKeyWanter runs only after the whole layout has spawned, so wait for the current
            // long event (map generation, loading) to finish; spawned during play there's none and this runs at once.
            LongEventHandler.ExecuteWhenFinished(TryAutoLink);
        }

        /// <summary>
        /// 保底連結。沒有任何控制台連過來時（手動放置、舊存檔、版面連結失敗），控制台照樣收卡卻什麼都不會發生，
        /// 鑰匙卡白白被吃掉。這裡改連最近一台還沒連結、也還沒啟用的門禁控制台；
        /// 若附近那台早已刷過卡卻沒連到任何東西（舊版留下的卡死存檔），視為卡已經刷過，直接解鎖。
        ///
        /// Fallback link. With no console linked (placed by hand, an old save, a failed layout link) a console still
        /// takes the card and nothing happens, so the key is simply eaten. Link the nearest access console that is
        /// neither linked nor activated; if the nearby one was already swiped with nothing linked (a save stuck by the
        /// old behaviour), treat the card as spent and unlock at once.
        /// </summary>
        private void TryAutoLink()
        {
            if (!Spawned || activated || countToActivate >= 0) return;

            IAccessKeyActivatable best = null;
            IAccessKeyActivatable spentUnlinked = null;
            float bestDist = float.MaxValue;
            float spentDist = float.MaxValue;
            HashSet<Thing> seen = new HashSet<Thing>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(Position, AutoLinkRadius, useCenter: true))
            {
                if (!c.InBounds(Map)) continue;
                List<Thing> things = c.GetThingList(Map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    // 封鎖門自己就是 activatable 兼 wanter，刷卡開的是它本身，不能被搶來連。
                    // Access doors are their own activatable and wanter; the card opens the door itself, so never borrow one.
                    if (t == this || t is Building_Door || !seen.Add(t) || AccessKeyLinkUtility.IsWanter(t)) continue;
                    IAccessKeyActivatable act = AccessKeyLinkUtility.GetActivatable(t);
                    if (act == null || act.LinkedAccessWanter != null) continue;

                    float dist = t.Position.DistanceToSquared(Position);
                    if (act.AccessKeyActivated)
                    {
                        // 會吐戰利品的控制台刷卡本來就有用途，不算「卡刷進空氣」。Loot consoles had a use of their own.
                        bool dropsLoot = act is CompAccessKeyActivatable comp && comp.Props.lootMaker != null;
                        if (!dropsLoot && dist < spentDist)
                        {
                            spentDist = dist;
                            spentUnlinked = act;
                        }
                    }
                    else if (act.CanLinkWanter(this) && dist < bestDist)
                    {
                        bestDist = dist;
                        best = act;
                    }
                }
            }

            if (best != null && AccessKeyLinkUtility.TryLink(best, this))
            {
                Log.Message($"[DMS] {def.defName} at {Position} had no linked console; linked to {best.ParentThing} instead.");
                return;
            }
            if (spentUnlinked != null)
            {
                spentUnlinked.LinkedAccessWanter = this;
                Log.Message($"[DMS] {def.defName} at {Position}: {spentUnlinked.ParentThing} was already swiped with nothing linked; unlocking.");
                Unlock();
            }
        }

        // ── IAccessKeyWanter ───────────────────────────────────────────────────

        public void Notify_LinkedTo(IAccessKeyActivatable activatable)
        {
            if (activated) return;
            if (countToActivate < 0)
            {
                countToActivate = 0;
            }
            countToActivate++;
        }

        public void Notify_AccessKeyUsed(IAccessKeyActivatable activatable, Pawn pawn = null)
        {
            if (activated) return;
            countToActivate--;
            if (countToActivate <= 0)
            {
                Unlock(pawn);
            }
        }

        // ── 解鎖 / Unlock ──────────────────────────────────────────────────────

        /// <summary>
        /// 原地換成 unlockedDef。先記下位置再 Destroy 自己，否則 Impassable 的 5x5 會擋住新建築生成。
        /// Swap to unlockedDef in place. Capture the placement first, then destroy ourselves, or our
        /// own impassable footprint blocks the spawn.
        /// </summary>
        public void Unlock(Pawn pawn = null)
        {
            if (activated || !Spawned) return;
            activated = true;

            ModExtension_AccessKeyTransform ext = Ext;
            if (ext?.unlockedDef == null)
            {
                Log.Error($"[DMS] {def.defName} was unlocked but has no ModExtension_AccessKeyTransform.unlockedDef; nothing to turn into.");
                return;
            }

            Map map = Map;
            IntVec3 pos = Position;
            Rot4 rot = Rotation;
            bool wasSelected = Find.Selector.IsSelected(this);

            ext.unlockSound?.PlayOneShot(new TargetInfo(pos, map));
            if (def.destroyable)
            {
                Destroy(DestroyMode.Vanish);
            }
            else
            {
                // XML 把 destroyable 關掉的話 Destroy() 會拒絕；退回 DeSpawn 仍能完成替換，但存檔可能留下懸空引用。
                // Destroy() refuses when the def is not destroyable; DeSpawn still completes the swap but may leave a dangling reference in saves.
                Log.ErrorOnce($"[DMS] {def.defName} has destroyable=false; Building_AccessKeyTransformer needs destroyable=true to swap cleanly. Falling back to DeSpawn.", def.shortHash);
                DeSpawn(DestroyMode.Vanish);
            }

            Thing replacement = ThingMaker.MakeThing(ext.unlockedDef);
            GenSpawn.Spawn(replacement, pos, map, rot, WipeMode.Vanish);

            if (ext.markHacked && replacement is ThingWithComps twc)
            {
                twc.GetComp<CompHackable>()?.HackNow();
            }

            if (wasSelected)
            {
                Find.Selector.Select(replacement, playSound: false, forceDesignatorDeselect: false);
            }

            Messages.Message("DMS_AccessKeyTransform_Unlocked".Translate(replacement.Named("THING")), replacement, MessageTypeDefOf.PositiveEvent);
        }

        // ── UI ─────────────────────────────────────────────────────────────────

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            if (!activated)
            {
                sb.AppendLineIfNotEmpty();
                sb.Append(countToActivate > 0
                    ? "DMS_AccessKeyTransform_Locked".Translate(countToActivate)
                    : "DMS_AccessKeyTransform_NoConsole".Translate());
            }
            return sb.ToString();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }
            if (DebugSettings.ShowDevGizmos && !activated)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Unlock",
                    action = () => Unlock()
                };
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref activated, "activated", defaultValue: false);
            Scribe_Values.Look(ref countToActivate, "countToActivate", defaultValue: -1);
        }
    }
}
