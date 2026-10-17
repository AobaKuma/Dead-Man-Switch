using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DMS
{
    /// <summary>
    /// 交出脫序者：艦隊帶走脫序者並收走那份加密檔案（不授予任何研究、不敵對、不追殺）。
    /// - 殖民者因交出同伴而心情低落（原版「放逐殖民者」的想法，可關閉）。
    /// - 艦隊好感度提升。
    /// - 皇權 DLC：一份榮譽，由玩家在原版的選人信件中指定領受者（ChoiceLetter_ChoosePawn → QuestPart_GiveRoyalFavor）。
    ///   有榮譽要分配時任務等到選完才結束（outSignalHonorChosen）；沒有時直接送 outSignalDone。
    ///
    /// Handing the deviant over: the fleet takes them and the encrypted archive (no research, no hostility, no hunt).
    /// Colonists take vanilla's "colonist banished" mood hit (optional); fleet goodwill rises; with Royalty, honor goes to
    /// a colonist the player picks in vanilla's choose-pawn letter. With honor to hand out the quest ends once it's picked
    /// (outSignalHonorChosen); otherwise outSignalDone ends it at once.
    /// </summary>
    public class QuestPart_DeviantExtradition : QuestPart
    {
        public string inSignal;
        public Pawn deviant;
        public Faction fleet;
        public int goodwillReward;
        public int honorReward;
        public bool moodPenalty = true;
        public string outSignalDone;
        public string outSignalHonorChosen;
        public string outSignalFailed;

        private bool done;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || done) return;
            done = true;

            if (DeviantHuntUtility.IsLost(deviant, out DeviantLostReason reason))
            {
                DeviantHuntUtility.SendFailedLetter(deviant, DeviantHuntUtility.FailedSuffix(reason), quest);
                Find.SignalManager.SendSignal(new Signal(outSignalFailed));
                return;
            }

            string fleetName = fleet?.Name ?? "?";
            // 先記下名字：交出之後 pawn 不在地圖上。Capture the letter text before the pawn leaves.
            TaggedString doneLabel = "DMS_DeviantExtradition_DoneLabel".Translate(deviant.Named("DEVIANT"));
            TaggedString doneText = "DMS_DeviantExtradition_DoneText".Translate(deviant.Named("DEVIANT"), fleetName.Named("FLEET"));

            if (moodPenalty)
            {
                // 必須在脫序者還屬於殖民地時給：原版依「誰認識這名殖民者」分發想法。Must run while they still belong to the colony.
                PawnDiedOrDownedThoughtsUtility.TryGiveThoughts(deviant, null, PawnDiedOrDownedThoughtsKind.Banished);
            }
            HandOver();

            if (goodwillReward != 0 && fleet != null)
            {
                fleet.TryAffectGoodwillWith(Faction.OfPlayer, goodwillReward, canSendMessage: true, canSendHostilityLetter: true,
                    DMS_DefOf.DMS_ExtraditedDeviant);
            }
            Find.LetterStack.ReceiveLetter(doneLabel, doneText, LetterDefOf.NeutralEvent, null, fleet, quest);

            List<Pawn> receivers = honorReward > 0 && fleet != null && ModsConfig.RoyaltyActive ? HonorReceivers() : null;
            if (receivers.NullOrEmpty())
            {
                Find.SignalManager.SendSignal(new Signal(outSignalDone));
                return;
            }
            ChoiceLetter_ChoosePawn choose = (ChoiceLetter_ChoosePawn)LetterMaker.MakeLetter(
                "DMS_DeviantExtradition_HonorLabel".Translate(fleet.def.royalFavorLabel.Named("HONOR")),
                "DMS_DeviantExtradition_HonorText".Translate(fleetName.Named("FLEET"), honorReward.Named("AMOUNT"),
                    fleet.def.royalFavorLabel.Named("HONOR")),
                LetterDefOf.ChoosePawn, fleet, quest);
            choose.pawns.AddRange(receivers);
            choose.chosenPawnSignal = outSignalHonorChosen;
            Find.LetterStack.ReceiveLetter(choose);
        }

        /// <summary>
        /// 把脫序者交給艦隊：從商隊或地圖移出、解除床位、改屬艦隊、交給世界 pawn。
        /// Hands the deviant to the fleet: out of the caravan or off the map, beds released, fleet faction, to world pawns.
        /// </summary>
        private void HandOver()
        {
            Caravan caravan = deviant.GetCaravan();
            if (caravan != null)
            {
                CaravanInventoryUtility.MoveAllInventoryToSomeoneElse(deviant, caravan.PawnsListForReading);
                caravan.RemovePawn(deviant);
            }
            if (deviant.Spawned)
            {
                deviant.DeSpawn();
            }
            else
            {
                deviant.holdingOwner?.Remove(deviant);
            }
            deviant.ownership?.UnclaimAll();
            deviant.SetFaction(fleet);
            if (!deviant.IsWorldPawn())
            {
                Find.WorldPawns.PassToWorld(deviant);
            }
        }

        /// <summary>可領受榮譽的殖民者（不含任務借住者）。Colonists who can receive the honor, quest lodgers excluded.</summary>
        private List<Pawn> HonorReceivers()
        {
            return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists
                .Where(p => p != deviant && p.royalty != null && !QuestUtility.IsQuestLodger(p))
                .ToList();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_References.Look(ref deviant, "deviant");
            Scribe_References.Look(ref fleet, "fleet");
            Scribe_Values.Look(ref goodwillReward, "goodwillReward");
            Scribe_Values.Look(ref honorReward, "honorReward");
            Scribe_Values.Look(ref moodPenalty, "moodPenalty", true);
            Scribe_Values.Look(ref outSignalDone, "outSignalDone");
            Scribe_Values.Look(ref outSignalHonorChosen, "outSignalHonorChosen");
            Scribe_Values.Look(ref outSignalFailed, "outSignalFailed");
            Scribe_Values.Look(ref done, "done");
        }
    }
}
