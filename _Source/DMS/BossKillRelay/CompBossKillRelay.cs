using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fortified;
using RimWorld;
using Verse;
using Verse.Sound;

namespace DMS
{
    public class CompProperties_BossKillRelay : CompProperties
    {
        /// <summary>監聽的訊號，預設為 <see cref="BossKilledSignal.Tag"/>。The signal to listen for; defaults to <see cref="BossKilledSignal.Tag"/>.</summary>
        public string listenSignal = BossKilledSignal.Tag;

        /// <summary>只認這些 Boss；留空則同地圖任何 Boss 死亡都算。Only these bosses count; empty means any boss on the map.</summary>
        public List<PawnKindDef> bossKinds;

        public SoundDef releaseSound;

        public CompProperties_BossKillRelay()
        {
            compClass = typeof(CompBossKillRelay);
        }
    }

    /// <summary>
    /// Boss 擊殺中繼器：同一張地圖上的 Boss 死亡時（<see cref="BossKilledSignal"/>），放行所有連結到它的門禁 wanter
    /// （密封門、<see cref="Building_AccessKeyTransformer"/> 等）。
    /// 它是門禁系統的 activatable 端，所以 FFF 的 Access link 工具（ExportTool_LinkAccessKeyWanter）可以直接選它；
    /// 跟 <see cref="CompVaultConsole"/> 一樣允許連結多個 wanter，一座中繼器可以封住整個 Boss 房的出入口。
    ///
    /// The boss-kill relay: when a boss dies on the same map (<see cref="BossKilledSignal"/>), it releases every
    /// access-key wanter linked to it (sealed doors, <see cref="Building_AccessKeyTransformer"/> and so on).
    /// It is the activatable end of the access-key system, so FFF's access link tool (ExportTool_LinkAccessKeyWanter)
    /// can pick it directly; like <see cref="CompVaultConsole"/> it takes any number of wanters, so one relay can seal
    /// every exit of a boss room.
    ///
    /// 連結只經由 <see cref="AccessKeyLinkUtility.TryLink"/>：它設定 <see cref="LinkedAccessWanter"/> 後會自己通知 wanter，
    /// 所以 setter 只記錄，不能再通知一次，否則密封門會多算一張卡而永遠打不開。
    /// Links only come through <see cref="AccessKeyLinkUtility.TryLink"/>, which notifies the wanter itself after setting
    /// <see cref="LinkedAccessWanter"/>; the setter must only record it, or the sealed door counts one card too many and
    /// never opens.
    /// </summary>
    public class CompBossKillRelay : ThingComp, IAccessKeyActivatable
    {
        private List<Thing> linkedWanters = new List<Thing>();

        private bool released;

        public CompProperties_BossKillRelay Props => (CompProperties_BossKillRelay)props;

        public bool Released => released;

        public IReadOnlyList<Thing> LinkedWanters => linkedWanters;

        // ── IAccessKeyActivatable ──────────────────────────────────────────────

        public Thing ParentThing => parent;

        public Thing LinkedAccessWanter
        {
            get => linkedWanters.Count > 0 ? linkedWanters[0] : null;
            set
            {
                if (value != null && !linkedWanters.Contains(value)) linkedWanters.Add(value);
            }
        }

        public bool AccessKeyActivated => released;

        public bool CanLinkWanter(Thing wanter)
        {
            return wanter != null && !wanter.Destroyed && wanter != parent && !released && !linkedWanters.Contains(wanter);
        }

        public void Notify_WanterLinked(Thing wanter)
        {
        }

        // ── 訊號 / Signal ──────────────────────────────────────────────────────

        public override void Notify_SignalReceived(Signal signal)
        {
            base.Notify_SignalReceived(signal);
            if (released || signal.tag != Props.listenSignal) return;
            // 訊號是全域的：別的地圖（例如殖民地遭遇的 Boss 團）死掉的 Boss 不算。
            // Signals are global: a boss dying on another map (say, a bossgroup raiding the colony) doesn't count.
            if (!parent.Spawned || !signal.IsFromMap(parent.Map)) return;

            signal.args.TryGetArg("SUBJECT", out Pawn boss);
            if (!Props.bossKinds.NullOrEmpty() && (boss == null || !Props.bossKinds.Contains(boss.kindDef))) return;

            signal.args.TryGetArg("INSTIGATOR", out Thing instigator);
            Release(instigator as Pawn, boss);
        }

        // ── 放行 / Release ─────────────────────────────────────────────────────

        public void Release(Pawn pawn = null, Pawn boss = null)
        {
            if (released) return;
            released = true;

            for (int i = 0; i < linkedWanters.Count; i++)
            {
                Thing wanter = linkedWanters[i];
                if (wanter == null || wanter.Destroyed) continue;

                List<IAccessKeyWanter> wanters = AccessKeyLinkUtility.GetWanters(wanter);
                for (int j = 0; j < wanters.Count; j++)
                {
                    wanters[j].Notify_AccessKeyUsed(this, pawn);
                }
            }

            if (!parent.Spawned) return;
            Props.releaseSound?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            if (linkedWanters.Count > 0)
            {
                Messages.Message(boss != null
                        ? "DMS_BossKillRelay_ReleasedBy".Translate(parent.Named("RELAY"), boss.Named("BOSS"))
                        : "DMS_BossKillRelay_Released".Translate(parent.Named("RELAY")),
                    new LookTargets(parent), MessageTypeDefOf.PositiveEvent);
            }
        }

        // ── UI ─────────────────────────────────────────────────────────────────

        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();
            if (released)
            {
                sb.Append("DMS_BossKillRelay_InspectReleased".Translate());
            }
            else if (linkedWanters.Count == 0)
            {
                sb.Append("DMS_BossKillRelay_InspectNoLink".Translate());
            }
            else
            {
                sb.Append("DMS_BossKillRelay_InspectLocked".Translate(linkedWanters.Count));
                if (!Props.bossKinds.NullOrEmpty())
                {
                    sb.AppendLine();
                    sb.Append("DMS_BossKillRelay_InspectBossKinds".Translate(Props.bossKinds.Select(k => k.LabelCap.Resolve()).ToCommaList()));
                }
            }
            return sb.ToString();
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (released) return;
            for (int i = 0; i < linkedWanters.Count; i++)
            {
                Thing wanter = linkedWanters[i];
                if (wanter != null && wanter.Spawned && wanter.Map == parent.Map)
                {
                    GenDraw.DrawLineBetween(parent.TrueCenter(), wanter.TrueCenter());
                }
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            if (DebugSettings.ShowDevGizmos && !released)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Release",
                    action = () => Release()
                };
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref linkedWanters, "linkedWanters", LookMode.Reference);
            Scribe_Values.Look(ref released, "released", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                linkedWanters ??= new List<Thing>();
                linkedWanters.RemoveAll(t => t == null);
            }
        }
    }
}
