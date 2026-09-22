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
