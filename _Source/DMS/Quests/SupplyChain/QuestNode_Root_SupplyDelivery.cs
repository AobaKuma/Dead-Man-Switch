using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace DMS
{
    /// <summary>
    /// 單一階段交付子任務 root。
    /// slate 輸入(由 QuestPart_SupplyChainGenerator 提供):
    ///   map, category, targetCount, stage, totalStages, rewardValue,
    ///   deadlineDays, stageSuccessSignal, stageFailSignal
    /// 流程:接受(autoAccept)→空投補給艙→期限倒數
    ///   交付完成→空投獎勵→通知母任務成功→子任務成功
    ///   期限截止→補給艙離場→通知母任務失敗→子任務失敗
    /// </summary>
    public class QuestNode_Root_SupplyDelivery : QuestNode
    {
        public ThingDef podDef;
        public ThingDef incomingSkyfaller;

        protected override bool TestRunInt(Slate slate)
        {
            return podDef != null
                && incomingSkyfaller != null
                && slate.Get<ThingCategoryDef>("category") != null
                && slate.Get<int>("targetCount") > 0;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;

            Map map = slate.Get<Map>("map") ?? Find.AnyPlayerHomeMap;
            ThingCategoryDef category = slate.Get<ThingCategoryDef>("category");
            int targetCount = slate.Get<int>("targetCount");
            float rewardValue = slate.Get<float>("rewardValue");
            float deadlineDays = slate.Get<float>("deadlineDays", 5f);
            string stageSuccessSignal = slate.Get<string>("stageSuccessSignal");
            string stageFailSignal = slate.Get<string>("stageFailSignal");
            string issuerUnit = slate.Get<string>("issuerUnit");
            string inSignal = slate.Get<string>("inSignal");

            // 挑戰等級與母任務一致(由生成器經 slate 傳入)
            quest.challengeRating = slate.Get<int>("challengeRating", 1);

            string podTag = QuestGenUtility.HardcodedTargetQuestTagWithQuestID("pod");
            string deliveredSignal = QuestGenUtility.HardcodedSignalWithQuestID("pod.Delivered");
            string deadlineSignal = QuestGenUtility.HardcodedSignalWithQuestID("deadline");

            // 空投補給艙;期限截止時令其離場
            quest.AddPart(new QuestPart_SpawnSupplyPod
            {
                inSignal = inSignal,
                inSignalSendAway = deadlineSignal,
                mapParent = map.Parent,
                podDef = podDef,
                skyfallerDef = incomingSkyfaller,
                category = category,
                count = targetCount,
                questTagToAdd = podTag,
                issuerUnit = issuerUnit,
            });

            // 期限
            QuestPart_Delay delay = new QuestPart_Delay
            {
                inSignalEnable = inSignal,
                inSignalDisable = deliveredSignal,
                delayTicks = (int)(deadlineDays * GenDate.TicksPerDay),
                expiryInfoPart = "DMS_SupplyChain_DeadlineTitle".Translate(),
                expiryInfoPartTip = "DMS_SupplyChain_DeadlineTip".Translate(),
                isBad = true,
                alertLabel = "DMS_SupplyChain_AlertLabel".Translate(),
                alertExplanation = "DMS_SupplyChain_AlertExplanation".Translate(targetCount, category.label),
                ticksLeftAlertCritical = GenDate.TicksPerDay,
            };
            delay.outSignalsCompleted.Add(deadlineSignal);
            quest.AddPart(delay);

            // 交付完成 → 空投獎勵
            quest.AddPart(new QuestPart_SupplyReward
            {
                inSignal = deliveredSignal,
                marketValue = rewardValue,
                mapParent = map.Parent,
                issuerUnit = issuerUnit,
            });

            // 回報母任務
            if (!stageSuccessSignal.NullOrEmpty())
                quest.AddPart(new QuestPart_Pass { inSignal = deliveredSignal, outSignal = stageSuccessSignal });
            if (!stageFailSignal.NullOrEmpty())
                quest.AddPart(new QuestPart_Pass { inSignal = deadlineSignal, outSignal = stageFailSignal });

            // 子任務結束
            quest.AddPart(new QuestPart_QuestEnd { inSignal = deliveredSignal, outcome = QuestEndOutcome.Success });
            quest.AddPart(new QuestPart_QuestEnd { inSignal = deadlineSignal, outcome = QuestEndOutcome.Fail });
        }
    }
}
