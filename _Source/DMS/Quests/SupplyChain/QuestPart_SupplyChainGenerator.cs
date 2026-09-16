using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 仿 QuestPart_SubquestGenerator_RelicHunt 的鏈式產生器:
    /// 每階段在執行期用固定的 slate 生成一個交付子任務,
    /// 監聽子任務回報的成功/失敗訊號來推進或結算。
    /// </summary>
    public class QuestPart_SupplyChainGenerator : QuestPartActivable
    {
        public ThingCategoryDef category;
        public QuestScriptDef subquestDef;
        public MapParent mapParent;
        public string issuerUnit;
        public string issuerFactionName;
        public int challengeRating = 1;

        public int totalStages;
        public int baseCount;
        public float countGrowth;
        public float unitValue;      // 類別中位單價(由 node 於生成時計算)
        public float rewardMarkup;   // 報酬 = 階段要求總市值 × markup
        public float rewardGrowth;   // 每階段額外報酬加成
        public float stageDeadlineDays;
        public float deadlineDaysPerStage;
        public IntRange stageIntervalTicksRange;  // 階段之間的隨機間隔

        public string signalChainCompleted;
        public string signalChainSettled;
        public string signalChainAllFailed;

        // runtime state
        public int currentStage;        // 0-based
        public int stagesCompleted;
        private string activeStageSuccessSignal;
        private string activeStageFailSignal;
        private Quest activeSubquest;
        private bool started;
        private int nextStageTick = -1;   // 下一階段的排程 tick(-1 = 無排程)

        public override string DescriptionPart =>
            $"Deliveries completed: {stagesCompleted} / {totalStages}";

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (State != QuestPartState.Enabled) return;

            // 啟用(母任務被接受)後生成第一階段
            if (!started && signal.tag == inSignalEnable)
            {
                started = true;
                GenerateStage();
                return;
            }
            if (activeStageSuccessSignal != null && signal.tag == activeStageSuccessSignal)
            {
                stagesCompleted++;
                currentStage++;
                activeSubquest = null;
                if (currentStage >= totalStages)
                {
                    Complete();
                    Find.SignalManager.SendSignal(new Signal(signalChainCompleted));
                }
                else
                {
                    ScheduleNextStage();
                }
                return;
            }
            if (activeStageFailSignal != null && signal.tag == activeStageFailSignal)
            {
                activeSubquest = null;
                nextStageTick = -1;
                Complete();
                Find.SignalManager.SendSignal(new Signal(
                    stagesCompleted > 0 ? signalChainSettled : signalChainAllFailed));
            }
        }

        /// <summary>以隨機間隔排程下一階段;間隔為 0 時立即生成。</summary>
        private void ScheduleNextStage()
        {
            int interval = stageIntervalTicksRange.RandomInRange;
            if (interval <= 0)
            {
                GenerateStage();
                return;
            }
            nextStageTick = Find.TickManager.TicksGame + interval;
            Messages.Message(
                SupplyChainText.Resolve("intervalMessage",
                    "issuerUnit", issuerUnit,
                    "days", ((float)interval / GenDate.TicksPerDay).ToString("0.#")),
                MessageTypeDefOf.NeutralEvent, false);
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (nextStageTick >= 0 && Find.TickManager.TicksGame >= nextStageTick)
            {
                nextStageTick = -1;
                GenerateStage();
            }
        }

        private void GenerateStage()
        {
            Map map = mapParent?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null)
            {
                // 沒有可用地圖:直接結算
                Complete();
                Find.SignalManager.SendSignal(new Signal(
                    stagesCompleted > 0 ? signalChainSettled : signalChainAllFailed));
                return;
            }

            int count = Mathf.Max(1, Mathf.RoundToInt(baseCount * Mathf.Pow(countGrowth, currentStage)));
            // 報酬與該階段要求的實際市值掛鉤,後期階段另有溢價
            float reward = count * unitValue * rewardMarkup * Mathf.Pow(rewardGrowth, currentStage);
            float deadline = stageDeadlineDays + deadlineDaysPerStage * currentStage;

            activeStageSuccessSignal = $"Quest{quest.id}.Stage{currentStage}Success";
            activeStageFailSignal = $"Quest{quest.id}.Stage{currentStage}Fail";

            Slate slate = new Slate();
            slate.Set("map", map);
            slate.Set("category", category);
            slate.Set("categoryLabel", category.label);
            slate.Set("targetCount", count);
            slate.Set("stage", currentStage + 1);
            slate.Set("totalStages", totalStages);
            slate.Set("rewardValue", reward);
            slate.Set("deadlineDays", deadline);
            slate.Set("stageSuccessSignal", activeStageSuccessSignal);
            slate.Set("stageFailSignal", activeStageFailSignal);
            slate.Set("issuerUnit", issuerUnit);
            slate.Set("issuerFactionName", issuerFactionName);
            slate.Set("challengeRating", challengeRating);

            activeSubquest = QuestUtility.GenerateQuestAndMakeAvailable(subquestDef, slate);
            activeSubquest.parent = quest;

            if (activeSubquest.State == QuestState.Ongoing)
            {
                // autoAccept 子任務:自行發信通知(文本由文法隨機組合)
                string[] vars =
                {
                    "issuerUnit", issuerUnit,
                    "categoryLabel", category.label,
                    "count", count.ToString(),
                    "stage", (currentStage + 1).ToString(),
                    "totalStages", totalStages.ToString(),
                };
                Find.LetterStack.ReceiveLetter(
                    SupplyChainText.Resolve("stageLetterLabel", vars),
                    SupplyChainText.Resolve("stageLetterText", vars),
                    LetterDefOf.PositiveEvent, null, null, activeSubquest);
            }
            else
            {
                QuestUtility.SendLetterQuestAvailable(activeSubquest);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref category, "category");
            Scribe_Defs.Look(ref subquestDef, "subquestDef");
            Scribe_Values.Look(ref issuerUnit, "issuerUnit");
            Scribe_Values.Look(ref issuerFactionName, "issuerFactionName");
            Scribe_Values.Look(ref challengeRating, "challengeRating", 1);
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Values.Look(ref totalStages, "totalStages");
            Scribe_Values.Look(ref baseCount, "baseCount");
            Scribe_Values.Look(ref countGrowth, "countGrowth");
            Scribe_Values.Look(ref unitValue, "unitValue");
            Scribe_Values.Look(ref rewardMarkup, "rewardMarkup");
            Scribe_Values.Look(ref rewardGrowth, "rewardGrowth");
            Scribe_Values.Look(ref stageDeadlineDays, "stageDeadlineDays");
            Scribe_Values.Look(ref deadlineDaysPerStage, "deadlineDaysPerStage");
            Scribe_Values.Look(ref stageIntervalTicksRange, "stageIntervalTicksRange");
            Scribe_Values.Look(ref nextStageTick, "nextStageTick", -1);
            Scribe_Values.Look(ref signalChainCompleted, "signalChainCompleted");
            Scribe_Values.Look(ref signalChainSettled, "signalChainSettled");
            Scribe_Values.Look(ref signalChainAllFailed, "signalChainAllFailed");
            Scribe_Values.Look(ref currentStage, "currentStage");
            Scribe_Values.Look(ref stagesCompleted, "stagesCompleted");
            Scribe_Values.Look(ref activeStageSuccessSignal, "activeStageSuccessSignal");
            Scribe_Values.Look(ref activeStageFailSignal, "activeStageFailSignal");
            Scribe_References.Look(ref activeSubquest, "activeSubquest");
            Scribe_Values.Look(ref started, "started");
        }
    }
}
