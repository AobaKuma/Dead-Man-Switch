using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 受訓期間的敵對監控。
    /// 接受任務時啟用,之後每 250 tick 檢查艦隊是否轉為敵對 —— 用輪詢而不是
    /// faction.BecameHostileToPlayer 訊號,才能同時抓到玩家主動挑釁與各種間接翻臉來源。
    ///
    /// 翻臉時分兩種:
    /// - 學員還沒登機 → 發 outSignalAborted,單純破局,人還在家。
    /// - 學員已在艦上 → 從 lend part 的清單移除 → 進艦隊綁架名單 → 發 outSignalCaptured。
    ///
    /// 這裡刻意完全不呼叫 lend.Complete():那會發出 lend 的完成訊號(= 歸還成功),
    /// 任務就會被判成功。把人從清單移除後直接送失敗訊號,任務結束時 activable part 走
    /// Cleanup(),不會觸發 Complete()。
    /// </summary>
    public class QuestPart_TrainingWatch : QuestPartActivable
    {
        public Faction faction;
        public Pawn trainee;
        public Pawn asker;
        public string outSignalCaptured;
        public string outSignalAborted;

        private bool resolved;

        private const int CheckIntervalTicks = 250;

        private QuestPart_LendColonistsToFaction Lend =>
            quest?.PartsListForReading.OfType<QuestPart_LendColonistsToFaction>().FirstOrDefault();

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (resolved || faction == null) return;
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0) return;
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile) return;

            resolved = true;

            QuestPart_LendColonistsToFaction lend = Lend;
            bool aboard = lend != null
                          && lend.State == QuestPartState.Enabled
                          && trainee != null
                          && !trainee.Dead
                          && lend.LentColonistsListForReading.Contains(trainee);

            if (aboard)
            {
                Find.LetterStack.ReceiveLetter(
                    SupplyChainText.Resolve(OfficerTrainingText.Pack, "capturedLetterLabel", LetterVars()),
                    SupplyChainText.Resolve(OfficerTrainingText.Pack, "capturedLetterText", LetterVars()),
                    LetterDefOf.NegativeEvent, null, faction, quest);

                lend.LentColonistsListForReading.Remove(trainee);

                // KidnappedPawnsTracker.Kidnap 內部直接用 kidnapper.Named(...),不能傳 null。
                Pawn kidnapper = (asker != null && !asker.Destroyed) ? asker : faction.leader;
                if (kidnapper != null && !kidnapper.Destroyed)
                {
                    faction.kidnapped.Kidnap(trainee, kidnapper);
                }
                else
                {
                    // 幾乎不會走到:連發文軍官和派系領袖都不在了。直接併入艦隊,
                    // 至少不會讓學員卡在世界 pawn 池裡變成看不到也找不回的幽靈。
                    trainee.SetFaction(faction);
                }

                Find.SignalManager.SendSignal(new Signal(outSignalCaptured));
            }
            else
            {
                Find.LetterStack.ReceiveLetter(
                    SupplyChainText.Resolve(OfficerTrainingText.Pack, "abortLetterLabel", LetterVars()),
                    SupplyChainText.Resolve(OfficerTrainingText.Pack, "abortLetterText", LetterVars()),
                    LetterDefOf.NegativeEvent, null, faction, quest);

                Find.SignalManager.SendSignal(new Signal(outSignalAborted));
            }

            Complete();
        }

        private string[] LetterVars()
        {
            return new[]
            {
                "traineeName", trainee?.LabelShort ?? "?",
                "issuerFactionName", faction?.Name ?? "?",
                "askerName", asker?.LabelShort ?? "?",
                "playerFactionName", Faction.OfPlayer?.Name ?? "?",
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref faction, "faction");
            Scribe_References.Look(ref trainee, "trainee");
            Scribe_References.Look(ref asker, "asker");
            Scribe_Values.Look(ref outSignalCaptured, "outSignalCaptured");
            Scribe_Values.Look(ref outSignalAborted, "outSignalAborted");
            Scribe_Values.Look(ref resolved, "resolved");
        }
    }
}
