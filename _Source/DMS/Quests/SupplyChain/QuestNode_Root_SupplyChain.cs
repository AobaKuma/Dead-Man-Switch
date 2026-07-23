using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace DMS
{
    /// <summary>categoryPool 的 XML 條目:類別 + 數量係數(高價值類別用較低係數)。</summary>
    public class SupplyCategoryOption
    {
        public ThingCategoryDef category;
        public float countFactor = 1f;
    }

    /// <summary>
    /// 母任務 root:選定一個 ThingCategory,掛上子任務鏈產生器。
    /// 玩家接受母任務後,產生器立刻生成第一階段交付子任務。
    /// </summary>
    public class QuestNode_Root_SupplyChain : QuestNode
    {
        public List<SupplyCategoryOption> categoryPool;
        public QuestScriptDef subquestDef;
        public RulePackDef issuerNamePack;   // 發包單位名稱生成規則

        // 難度點數 → 挑戰等級(任務面板骷髏數,1~3)
        public SimpleCurve pointsToChallengeRatingCurve;
        // 難度點數 → 期望階段數,加上隨機擾動後夾在 stagesClamp 內
        public SimpleCurve pointsToStagesCurve;
        public IntRange stagesJitter = new IntRange(-1, 1);
        public IntRange stagesClamp = new IntRange(2, 5);
        public float countGrowthPerStage = 1.6f;      // 每階段數量成長倍率
        // 難度點數 → 第一階段要求的總市值
        public SimpleCurve pointsToRequestValueCurve;
        public float rewardMarkup = 1.35f;            // 報酬 = 該階段要求總市值 × markup
        public float rewardGrowthPerStage = 1.1f;     // 每階段額外報酬加成(數量成長之外的溢價)
        // 完成獎勵:全部階段完成時,按合約貨物總市值 × factor 給予原版式獎勵選項
        public float completionBonusFactor = 0.4f;
        public FactionDef giverFactionDef;            // 好感度獎勵選項的發包陣營(可空)
        public float stageDeadlineDays = 5f;          // 每階段期限(天)
        public float deadlineDaysPerStage = 1f;       // 每階段追加期限(天)
        public FloatRange stageIntervalDaysRange = new FloatRange(1f, 3f); // 階段間隨機間隔(天)

        protected override bool TestRunInt(Slate slate)
        {
            if (categoryPool.NullOrEmpty()
                || subquestDef == null
                || QuestGen_Get.GetMap(false, null) == null
                || !categoryPool.Any(o => MedianUnitValue(o.category) > 0f))
                return false;
            // 調度員可自動生成(canGeneratePawn),只需陣營存在且非敵對
            Faction f = giverFactionDef != null
                ? Find.FactionManager.FirstFactionOfDef(giverFactionDef)
                : null;
            return f != null && !f.HostileTo(Faction.OfPlayer);
        }

        /// <summary>類別內可交易物品的中位市值(對奢侈品離群值穩健)。無有效物品時回傳 0。</summary>
        public static float MedianUnitValue(ThingCategoryDef cat)
        {
            if (cat == null) return 0f;
            List<float> vals = new List<float>();
            foreach (ThingDef d in cat.DescendantThingDefs)
            {
                if (d.category != ThingCategory.Item || !d.PlayerAcquirable) continue;
                float v = d.BaseMarketValue;
                if (v > 0f) vals.Add(v);
            }
            if (vals.Count == 0) return 0f;
            vals.Sort();
            return vals[vals.Count / 2];
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;
            Map map = QuestGen_Get.GetMap(false, null);
            slate.Set("map", map);

            // 只從能算出單價的類別中挑選
            SupplyCategoryOption pick = categoryPool
                .Where(o => MedianUnitValue(o.category) > 0f)
                .RandomElement();

            // 要求數量 = 難度點數映射的要求總市值 ÷ 類別中位單價
            float points = slate.Get<float>("points", 300f);

            // 挑戰等級:點數映射,夾在 1~3
            int challengeRating = UnityEngine.Mathf.Clamp(
                UnityEngine.Mathf.RoundToInt(pointsToChallengeRatingCurve?.Evaluate(points) ?? 1f), 1, 3);
            quest.challengeRating = challengeRating;

            // 階段數:點數曲線 + 隨機擾動
            int totalStages = UnityEngine.Mathf.Clamp(
                UnityEngine.Mathf.RoundToInt(pointsToStagesCurve?.Evaluate(points) ?? 3f)
                    + stagesJitter.RandomInRange,
                stagesClamp.min, stagesClamp.max);
            float requestValue = pointsToRequestValueCurve != null
                ? pointsToRequestValueCurve.Evaluate(points)
                : points * 2f;
            float unitValue = MedianUnitValue(pick.category);
            int baseCount = UnityEngine.Mathf.Max(1,
                UnityEngine.Mathf.RoundToInt(requestValue / unitValue * pick.countFactor));

            slate.Set("category", pick.category);
            slate.Set("categoryLabel", pick.category.label);
            slate.Set("totalStages", totalStages);
            slate.Set("firstCount", baseCount);
            slate.Set("deadlineDays", stageDeadlineDays);

            // 全部階段的大約需求總量(取概數供文本使用)
            int approxTotalCount = 0;
            for (int s = 0; s < totalStages; s++)
            {
                approxTotalCount += UnityEngine.Mathf.Max(1,
                    UnityEngine.Mathf.RoundToInt(baseCount * UnityEngine.Mathf.Pow(countGrowthPerStage, s)));
            }
            if (approxTotalCount >= 100)
                approxTotalCount = approxTotalCount / 10 * 10;
            else if (approxTotalCount >= 20)
                approxTotalCount = approxTotalCount / 5 * 5;
            slate.Set("approxTotalCount", approxTotalCount);

            // 後勤調度員:原版 GetPawn 優先取該陣營現有 WorldPawn,
            // 沒有合適人選時自動生成一名並送入世界池(canGeneratePawn)。
            // slate "asker" 讓描述規則可用 [asker_nameFull]/[asker_pronoun]/[asker_possessive]
            Faction giverFaction = giverFactionDef != null
                ? Find.FactionManager.FirstFactionOfDef(giverFactionDef)
                : null;
            Pawn asker = null;
            if (giverFaction != null)
            {
                asker = QuestGen_Pawns.GetPawn(quest, new QuestGen_Pawns.GetPawnParms
                {
                    mustBeOfFaction = giverFaction,
                    canGeneratePawn = true,
                    ifWorldPawnThenMustBeFree = true,
                    mustBeNonHostileToPlayer = true,
                });
            }
            slate.Set("asker", asker);
            slate.Set("issuerFactionName",
                giverFaction?.Name ?? "DMS_SupplyChain_FallbackIssuer".Translate().ToString());

            // 發包單位名稱在生成時決定一次,貫穿描述與所有階段信件
            string issuerUnit = issuerNamePack != null
                ? NameGenerator.GenerateName(issuerNamePack)
                : "the Colonial Fleet quartermaster corps";
            slate.Set("issuerUnit", issuerUnit);

            string chainCompleted = QuestGenUtility.HardcodedSignalWithQuestID("chain.Completed");
            string chainSettled = QuestGenUtility.HardcodedSignalWithQuestID("chain.Settled");
            string chainAllFailed = QuestGenUtility.HardcodedSignalWithQuestID("chain.AllFailed");

            QuestPart_SupplyChainGenerator generator = new QuestPart_SupplyChainGenerator
            {
                inSignalEnable = slate.Get<string>("inSignal"),
                issuerUnit = issuerUnit,
                issuerFactionName = giverFaction?.Name ?? "DMS_SupplyChain_FallbackIssuer".Translate().ToString(),
                challengeRating = challengeRating,
                category = pick.category,
                subquestDef = subquestDef,
                totalStages = totalStages,
                baseCount = baseCount,
                countGrowth = countGrowthPerStage,
                unitValue = unitValue,
                rewardMarkup = rewardMarkup,
                rewardGrowth = rewardGrowthPerStage,
                stageDeadlineDays = stageDeadlineDays,
                deadlineDaysPerStage = deadlineDaysPerStage,
                stageIntervalTicksRange = new IntRange(
                    (int)(stageIntervalDaysRange.min * GenDate.TicksPerDay),
                    (int)(stageIntervalDaysRange.max * GenDate.TicksPerDay)),
                mapParent = map.Parent,
                signalChainCompleted = chainCompleted,
                signalChainSettled = chainSettled,
                signalChainAllFailed = chainAllFailed,
            };
            quest.AddPart(generator);

            // 三種結局:全部完成=成功 / 中途截止但有完成階段=結算成功 / 一階段都沒完成=失敗
            quest.AddPart(new QuestPart_QuestEnd
            {
                inSignal = chainCompleted,
                outcome = QuestEndOutcome.Success,
            });
            quest.AddPart(new QuestPart_QuestEnd
            {
                inSignal = chainSettled,
                outcome = QuestEndOutcome.Success,
            });
            quest.AddPart(new QuestPart_QuestEnd
            {
                inSignal = chainAllFailed,
                outcome = QuestEndOutcome.Fail,
            });

            // ===== 完成獎勵選項 (原版 QuestPart_Choice) =====
            // 貨物總市值 = Σ 各階段要求數量 × 中位單價
            float totalCargoValue = 0f;
            for (int s = 0; s < totalStages; s++)
            {
                int stageCount = UnityEngine.Mathf.Max(1,
                    UnityEngine.Mathf.RoundToInt(baseCount * UnityEngine.Mathf.Pow(countGrowthPerStage, s)));
                totalCargoValue += stageCount * unitValue;
            }
            float bonusValue = totalCargoValue * completionBonusFactor;

            // 原版 GiveRewards 在 giverFaction != null 時會直接評估 asker.royalty,
            // asker 為 null 會 NRE。無調度員可當 asker 時退回純物資獎勵。
            Faction rewardFaction = asker != null ? giverFaction : null;

            RewardsGeneratorParams rewardParms = new RewardsGeneratorParams
            {
                rewardValue = bonusValue,
                thingRewardItemsOnly = true,
                allowGoodwill = rewardFaction != null,
                giverFaction = rewardFaction,
            };
            // 只在 chainCompleted (全部階段完成) 時發放;中途結算不觸發
            QuestGen_Rewards.GiveRewards(quest, rewardParms, chainCompleted,
                "DMS_SupplyChain_BonusLetterLabel".Translate(),
                "DMS_SupplyChain_BonusLetterText".Translate(),
                asker: asker);
        }
    }
}
