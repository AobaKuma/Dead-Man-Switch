using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 第 5 天：檔案還原完成的同時，艦隊得知脫序者受我們庇護並要求引渡。選擇權交給玩家（ChoiceLetter_DeviantExtradition）：
    /// - 交出：outSignalExtradite → QuestPart_DeviantExtradition（獎勵、檔案沒收）。
    /// - 拒絕：outSignalRefuse → QuestPart_DeviantHandover（授予研究、敵對、追殺）。拒絕才是玩家自己的脫序行為。
    /// 期限內沒有答覆視為拒絕。兩種選擇之前都先送 outSignalDecided，交付前監控就此停用。
    ///
    /// Day 5: as the archive is restored, the fleet learns we shelter the deviant and demands extradition. The choice is
    /// the player's (ChoiceLetter_DeviantExtradition): hand over → QuestPart_DeviantExtradition (rewards, archive seized);
    /// refuse → QuestPart_DeviantHandover (research, hostility, hunt), the player's own act of deviance. No answer by the
    /// deadline counts as refusal. outSignalDecided goes out first either way, which disables the pre-handover watch.
    /// </summary>
    public class QuestPart_DeviantExtraditionDemand : QuestPartActivable
    {
        public Pawn deviant;
        public Faction fleet;
        public int decisionTicks = 120000;
        public int goodwillReward;
        public int honorReward;
        public bool moodPenalty;
        public string outSignalDecided;
        public string outSignalExtradite;
        public string outSignalRefuse;
        public string outSignalFailed;

        private const int CheckIntervalTicks = 250;

        private int TicksLeft => EnableTick + decisionTicks - Find.TickManager.TicksGame;

        protected override void Enable(SignalArgs receivedArgs)
        {
            base.Enable(receivedArgs);
            if (DeviantHuntUtility.IsLost(deviant, out DeviantLostReason reason))
            {
                DeviantHuntUtility.SendFailedLetter(deviant, DeviantHuntUtility.FailedSuffix(reason), quest);
                Find.SignalManager.SendSignal(new Signal(outSignalFailed));
                Complete();
                return;
            }

            string fleetName = fleet?.Name ?? "?";
            ChoiceLetter_DeviantExtradition letter = (ChoiceLetter_DeviantExtradition)LetterMaker.MakeLetter(
                "DMS_DeviantExtradition_LetterLabel".Translate(deviant.Named("DEVIANT")),
                "DMS_DeviantExtradition_LetterText".Translate(deviant.Named("DEVIANT"), fleetName.Named("FLEET"),
                    RewardLines().Named("REWARDS"), decisionTicks.ToStringTicksToPeriod().Named("DAYS")),
                DMS_DefOf.DMS_DeviantExtradition,
                deviant.Spawned ? new LookTargets(deviant) : LookTargets.Invalid,
                fleet, quest, null);
            letter.deviant = deviant;
            Find.LetterStack.ReceiveLetter(letter);
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0 || TicksLeft > 0) return;
            // 沉默即拒絕。Silence is refusal.
            Messages.Message("DMS_DeviantExtradition_SilenceMessage".Translate((fleet?.Name ?? "?").Named("FLEET")),
                MessageTypeDefOf.NegativeEvent);
            Decide(extradite: false);
        }

        /// <summary>玩家在信件中做出選擇，或期限到了。The player chose in the letter, or the deadline passed.</summary>
        public void Decide(bool extradite)
        {
            if (State != QuestPartState.Enabled) return;
            RemoveLetters();
            Complete();
            Find.SignalManager.SendSignal(new Signal(outSignalDecided));
            Find.SignalManager.SendSignal(new Signal(extradite ? outSignalExtradite : outSignalRefuse));
        }

        public override string ExpiryInfoPart =>
            State == QuestPartState.Enabled && !quest.Historical
                ? "DMS_DeviantExtradition_DeadlineInfo".Translate(System.Math.Max(0, TicksLeft).ToStringTicksToPeriod()).ToString()
                : null;

        public override string ExpiryInfoPartTip =>
            State == QuestPartState.Enabled ? "DMS_DeviantExtradition_DeadlineInfoTip".Translate().ToString() : null;

        /// <summary>交出時的獎勵清單（信件用）。The rewards for handing over, for the letter.</summary>
        private string RewardLines()
        {
            List<string> lines = new List<string>();
            string fleetName = fleet?.Name ?? "?";
            if (goodwillReward != 0)
            {
                lines.Add("DMS_DeviantExtradition_RewardGoodwill".Translate(fleetName.Named("FLEET"), goodwillReward.Named("AMOUNT")));
            }
            if (honorReward > 0 && fleet != null)
            {
                lines.Add("DMS_DeviantExtradition_RewardHonor".Translate(honorReward.Named("AMOUNT"),
                    fleet.def.royalFavorLabel.Named("HONOR")));
            }
            if (moodPenalty)
            {
                lines.Add("DMS_DeviantExtradition_RewardMood".Translate());
            }
            return string.Join("\n", lines);
        }

        private void RemoveLetters()
        {
            foreach (Letter letter in Find.LetterStack.LettersListForReading.ToList())
            {
                if (letter is ChoiceLetter_DeviantExtradition choice && choice.quest == quest)
                {
                    Find.LetterStack.RemoveLetter(choice);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref deviant, "deviant");
            Scribe_References.Look(ref fleet, "fleet");
            Scribe_Values.Look(ref decisionTicks, "decisionTicks", 120000);
            Scribe_Values.Look(ref goodwillReward, "goodwillReward");
            Scribe_Values.Look(ref honorReward, "honorReward");
            Scribe_Values.Look(ref moodPenalty, "moodPenalty");
            Scribe_Values.Look(ref outSignalDecided, "outSignalDecided");
            Scribe_Values.Look(ref outSignalExtradite, "outSignalExtradite");
            Scribe_Values.Look(ref outSignalRefuse, "outSignalRefuse");
            Scribe_Values.Look(ref outSignalFailed, "outSignalFailed");
        }
    }

    /// <summary>
    /// 艦隊的引渡要求：交出、拒絕、稍後再決定。不能右鍵關閉；任務結束或已做出選擇後自動消失。
    /// The fleet's extradition demand: hand over, refuse, or decide later. Can't be right-click dismissed; disappears once
    /// the quest ends or a choice is made.
    /// </summary>
    public class ChoiceLetter_DeviantExtradition : ChoiceLetter
    {
        public Pawn deviant;

        public override bool CanDismissWithRightClick => false;

        private QuestPart_DeviantExtraditionDemand Demand =>
            quest?.PartsListForReading.OfType<QuestPart_DeviantExtraditionDemand>().FirstOrDefault();

        public override bool CanShowInLetterStack =>
            base.CanShowInLetterStack && quest != null && quest.State == QuestState.Ongoing
            && Demand?.State == QuestPartState.Enabled;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (ArchivedOnly)
                {
                    yield return Option_Close;
                    yield break;
                }
                yield return new DiaOption("DMS_DeviantExtradition_Extradite".Translate(deviant.Named("DEVIANT")))
                {
                    action = () => Demand?.Decide(extradite: true),
                    resolveTree = true,
                };
                yield return new DiaOption("DMS_DeviantExtradition_Refuse".Translate())
                {
                    action = () => Demand?.Decide(extradite: false),
                    resolveTree = true,
                };
                yield return Option_Postpone;
                if (lookTargets.IsValid())
                {
                    yield return Option_JumpToLocationAndPostpone;
                }
                if (quest != null && !quest.hidden)
                {
                    yield return Option_ViewInQuestsTab("ViewRelatedQuest", postpone: true);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref deviant, "deviant");
        }
    }
}
