using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 拒絕引渡後的交付：授予封存檔案、與艦隊敵對、啟動綁定脫序者的追殺。由 QuestPart_DeviantExtraditionDemand 的拒絕訊號啟用。
    ///
    /// 交付當下若有軍事法庭在審理，研究照樣授予，敵對與追殺延到審判結束（休戰期間設成敵對會讓那場審判破裂）。
    /// 延後期間任務維持進行中，每 250 tick 檢查一次。
    ///
    /// The day-5 handover: grant the sealed files, turn the fleet hostile, start the huntdown bound to the deviant.
    /// If a court-martial is in session the research is still granted, but hostility and the hunt wait until the trial
    /// ends (turning hostile mid-truce would collapse that trial). The quest stays ongoing meanwhile, checked every 250 ticks.
    /// </summary>
    public class QuestPart_DeviantHandover : QuestPartActivable
    {
        public Pawn deviant;
        public Faction fleet;
        public List<ResearchProjectDef> guaranteedProjects = new List<ResearchProjectDef>();
        public KnowledgeCategoryDef randomCategory;
        public int randomCount;
        public HuntdownDef huntdown;
        public string outSignalDone;
        public string outSignalFailed;

        private bool deferred;

        private const int CheckIntervalTicks = 250;

        protected override void Enable(SignalArgs receivedArgs)
        {
            base.Enable(receivedArgs);

            // 交付前監控每 250 tick 才看一次；這一刻再驗一次。The watch only polls every 250 ticks; check once more now.
            if (DeviantHuntUtility.IsLost(deviant, out DeviantLostReason reason))
            {
                DeviantHuntUtility.SendFailedLetter(deviant, DeviantHuntUtility.FailedSuffix(reason), quest);
                Find.SignalManager.SendSignal(new Signal(outSignalFailed));
                Complete();
                return;
            }

            string granted = GrantAll();
            deferred = OccultechSanctionUtility.CourtMartialOngoing;
            string fleetName = fleet?.Name ?? "?";
            Find.LetterStack.ReceiveLetter(
                "DMS_DeviantRefuge_HandoverLabel".Translate(),
                (deferred ? "DMS_DeviantRefuge_HandoverTextDeferred" : "DMS_DeviantRefuge_HandoverText")
                    .Translate(deviant.Named("DEVIANT"), fleetName.Named("FLEET"), granted.Named("GRANTED")),
                deferred ? LetterDefOf.NegativeEvent : LetterDefOf.ThreatBig,
                deviant.Spawned ? new LookTargets(deviant) : null, fleet, quest);

            if (!deferred)
            {
                ApplyHostility();
                Finish();
            }
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (!deferred || Find.TickManager.TicksGame % CheckIntervalTicks != 0) return;
            if (OccultechSanctionUtility.CourtMartialOngoing) return;

            deferred = false;
            ApplyHostility();
            // 審判期間脫序者可能已經不在：敵對照樣生效（艦隊已經知道了），追殺則因綁定者失去而在下一次檢查時結束。
            // The deviant may be gone by now: hostility still applies (the fleet already knows); the hunt then ends at its
            // next check because its bound pawn is lost.
            Find.LetterStack.ReceiveLetter(
                "DMS_DeviantRefuge_HostilityLabel".Translate(),
                "DMS_DeviantRefuge_HostilityText".Translate(deviant.Named("DEVIANT"), (fleet?.Name ?? "?").Named("FLEET")),
                LetterDefOf.ThreatBig, null, fleet, quest);
            Finish();
        }

        public override string ExpiryInfoPart => deferred ? "DMS_DeviantRefuge_DeferredInfo".Translate().ToString() : null;

        private void Finish()
        {
            Find.SignalManager.SendSignal(new Signal(outSignalDone));
            Complete();
        }

        /// <summary>授予保證專案與隨機專案，回傳信件用的清單。Grants the guaranteed and random projects; returns the letter list.</summary>
        private string GrantAll()
        {
            List<ResearchProjectDef> projects = new List<ResearchProjectDef>();
            projects.AddRange(guaranteedProjects.Where(p => p != null));
            projects.AddRange(DeviantTechUtility.PickRandom(randomCategory, randomCount, projects));

            StringBuilder lines = new StringBuilder();
            foreach (ResearchProjectDef proj in projects)
            {
                DeviantGrantResult result = DeviantTechUtility.Grant(proj);
                TaggedString line;
                switch (result)
                {
                    case DeviantGrantResult.Pending:
                        line = "DMS_DeviantRefuge_PendingLine".Translate(proj.LabelCap.Named("PROJECT"),
                            DeviantTechUtility.MissingPrerequisites(proj).Named("PREREQS"));
                        break;
                    case DeviantGrantResult.AlreadyHeld:
                        line = "DMS_DeviantRefuge_AlreadyHeldLine".Translate(proj.LabelCap.Named("PROJECT"));
                        break;
                    default:
                        line = "DMS_DeviantRefuge_GrantedLine".Translate(proj.LabelCap.Named("PROJECT"));
                        break;
                }
                if (lines.Length > 0) lines.Append('\n');
                lines.Append(line.Resolve());
            }
            return lines.Length > 0 ? lines.ToString() : "DMS_DeviantRefuge_NothingGranted".Translate().ToString();
        }

        /// <summary>
        /// 敵對＋追殺。艦隊敵對在任何情況下都生效（准將豁免只適用研究制裁）；追殺落在脫序者所在的據點，否則任一據點。
        /// Hostility plus the hunt. Hostility always applies (the brigadier exemption only covers research sanctions); the
        /// hunt lands on the home the deviant is on, else any home.
        /// </summary>
        private void ApplyHostility()
        {
            OccultechSanctionUtility.SetFleetHostile(fleet, DMS_DefOf.DMS_HarboredDeviant);
            if (huntdown != null && !DeviantHuntUtility.IsLost(deviant, out _))
            {
                Map map = deviant.MapHeld != null && deviant.MapHeld.IsPlayerHome ? deviant.MapHeld : Find.AnyPlayerHomeMap;
                HuntdownUtility.Start(huntdown, map, source: "Quest_" + quest.id, boundPawn: deviant);
                FleetTrackingUtility.InheritBlackout(huntdown);
            }
            // 撤掉被告不是脫序者的待接受傳票，換成指名脫序者的。Swap pending summonses for one naming the deviant.
            CourtMartialOfferUtility.RequestRefresh(ensureOffer: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref deviant, "deviant");
            Scribe_References.Look(ref fleet, "fleet");
            Scribe_Collections.Look(ref guaranteedProjects, "guaranteedProjects", LookMode.Def);
            Scribe_Defs.Look(ref randomCategory, "randomCategory");
            Scribe_Values.Look(ref randomCount, "randomCount");
            Scribe_Defs.Look(ref huntdown, "huntdown");
            Scribe_Values.Look(ref outSignalDone, "outSignalDone");
            Scribe_Values.Look(ref outSignalFailed, "outSignalFailed");
            Scribe_Values.Look(ref deferred, "deferred");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                guaranteedProjects ??= new List<ResearchProjectDef>();
                guaranteedProjects.RemoveAll(p => p == null);
            }
        }
    }
}
