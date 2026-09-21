using Fortified;
using RimWorld;
using Verse;
using Verse.Sound;

namespace DMS
{
    /// <summary>
    /// 戰鬥群呼叫器的警報反應：收到警報後透過 FFF 的 RaidWave 系統叫一波援軍。
    /// 兵力、派系、到場方式（空投到遠處 → 進攻）、延遲、信件都由 <see cref="RaidWaveDef"/> 與它的 quest 決定，
    /// 每呼叫一次波次自動升級（GameComponent_RaidWave 記錄次數）。
    /// Alert response for the battle-group caller: on alarm, summon a wave through FFF's RaidWave
    /// system. Squad, faction, arrival (distant drop pods → assault), delay and letters all come from
    /// the <see cref="RaidWaveDef"/> and its quest; each call escalates to the next wave
    /// (GameComponent_RaidWave keeps the count).
    /// </summary>
    public class CompProperties_AlertEffector_CallBattleGroup : CompProperties_AlertEffector
    {
        public RaidWaveDef raidWaveDef;

        /// <summary>
        /// 是否受 RaidWave 的全域冷卻（兩天）限制。預設不受限：警報就是要叫人，
        /// 但呼叫仍會刷新全域冷卻時間戳，玩家自己的信標會跟著進冷卻。
        /// Whether to honour RaidWave's global cooldown (two days). Off by default: an alarm should
        /// bring people; note the call still stamps the global cooldown, so the player's own beacons
        /// go on cooldown too.
        /// </summary>
        public bool respectCooldown = false;

        /// <summary>觸發當下播的音效／特效（呼叫器本身）。Played at the caller when it fires the call.</summary>
        public SoundDef callSound;
        public EffecterDef callEffecter;

        public CompProperties_AlertEffector_CallBattleGroup()
        {
            compClass = typeof(CompAlertEffector_CallBattleGroup);
        }

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (raidWaveDef == null) yield return $"{parentDef.defName}: CompProperties_AlertEffector_CallBattleGroup needs a raidWaveDef";
        }
    }

    public class CompAlertEffector_CallBattleGroup : CompAlertEffector
    {
        public new CompProperties_AlertEffector_CallBattleGroup Props => (CompProperties_AlertEffector_CallBattleGroup)props;

        protected override void DoEffect()
        {
            if (!parent.Spawned || Props.raidWaveDef == null) return;

            RaidWaveWorker worker = Props.raidWaveDef.Worker;
            if (Props.respectCooldown)
            {
                AcceptanceReport report = worker.CanResolve();
                if (!report.Accepted)
                {
                    Log.Message($"[DMS] {parent.LabelCap} at {parent.Position}: raid wave call blocked ({report.Reason}).");
                    return;
                }
            }

            worker.Resolve(parent.Map);

            (Props.callSound ?? SoundDefOf.FlickSwitch).PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            Props.callEffecter?.Spawn(parent.Position, parent.Map).Cleanup();

            Messages.Message("DMS_BattleGroupCall_Triggered".Translate(parent.LabelCap),
                new LookTargets(parent), MessageTypeDefOf.ThreatBig);
        }
    }
}
