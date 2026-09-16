using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 軍官受訓晉升任務。
    ///
    /// 由 Patch_GenerateBestowingCeremonyQuest 取代授銜典禮而生成:當 pawn 即將取得的
    /// 階級掛有 TitleTrainingExtension(目前是准尉與少校)時,艦隊不辦典禮,改派運輸機
    /// 把人接上去受訓。
    ///
    /// 流程:接受 → 穿梭機降落 → 學員登機離場(外借給艦隊)→ 受訓 N 天 →
    /// 完訓晉升 + 課程技能大量經驗 + 移除一個負面特質 → 空投歸還。
    /// 受訓期間與艦隊翻臉 → 學員被當成敵國戰鬥人員扣押,進艦隊綁架名單,任務失敗。
    /// </summary>
    public class QuestNode_Root_OfficerTraining : QuestNode
    {
        public FactionDef fleetFactionDef;
        public TransportShipDef transportShipDef;   // 接送用運輸機(DMS_Ship_TransportShuttle)
        public PawnKindDef askerKind;               // 發文的教育訓練處軍官
        public float boardingDeadlineDays = 2f;
        /// <summary>殖民地至少要剩幾個自由殖民者才會開這個任務(免得把最後一個人送走)。</summary>
        public int minFreeColonists = 2;

        public List<TrainingCourse> courses = new List<TrainingCourse>();

        /// <summary>可移除的負面特質白名單。</summary>
        public List<TraitEntry> removableTraits = new List<TraitEntry>();

        /// <summary>白名單之外,是否也把 marketValueFactorOffset 為負的特質視為負面(涵蓋其他 mod 的特質)。</summary>
        public bool alsoRemoveNegativeValueTraits = true;

        /// <summary>讓 QuestPart 讀回本節點的資料設定,免得把整份名單塞進存檔。</summary>
        public static QuestNode_Root_OfficerTraining Config =>
            DMS_DefOf.DMS_OfficerTraining?.root as QuestNode_Root_OfficerTraining;

        private Faction Fleet => fleetFactionDef != null
            ? Find.FactionManager.FirstFactionOfDef(fleetFactionDef)
            : null;

        private QuestGen_Pawns.GetPawnParms AskerParms(Faction fleet)
        {
            return new QuestGen_Pawns.GetPawnParms
            {
                mustBeOfKind = askerKind,
                mustBeOfFaction = fleet,
                canGeneratePawn = true,
                ifWorldPawnThenMustBeFree = true,
            };
        }

        private bool TryGetTarget(Slate slate, out Pawn trainee, out Faction fleet, out RoyalTitleDef newTitle)
        {
            trainee = null;
            newTitle = null;
            slate.TryGet("bestowingFaction", out fleet);
            if (fleet == null) fleet = Fleet;
            if (fleet == null) return false;
            if (!slate.TryGet("titleHolder", out trainee) || trainee?.royalty == null) return false;
            if (trainee.Faction == null || !trainee.Faction.IsPlayer) return false;

            newTitle = trainee.royalty.GetTitleAwardedWhenUpdating(fleet, trainee.royalty.GetFavor(fleet));
            return newTitle != null && newTitle.GetModExtension<TitleTrainingExtension>() != null;
        }

        /// <summary>同一個 pawn 只能有一份受訓任務在跑 —— GenerateBestowingCeremonyQuest 會被反覆呼叫。</summary>
        private static bool HasOngoingTraining(Pawn pawn)
        {
            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < quests.Count; i++)
            {
                Quest q = quests[i];
                if (q.State != QuestState.NotYetAccepted && q.State != QuestState.Ongoing) continue;
                foreach (QuestPart part in q.PartsListForReading)
                {
                    if (part is QuestPart_TrainingGraduation g && g.trainee == pawn) return true;
                }
            }
            return false;
        }

        protected override bool TestRunInt(Slate slate)
        {
            if (!ModsConfig.RoyaltyActive) return false;
            if (transportShipDef?.shipThing == null) return false;
            if (!transportShipDef.shipThing.HasComp(typeof(CompShuttle))) return false;
            if (!courses.Any(c => c?.skill != null)) return false;
            if (!TryGetTarget(slate, out Pawn trainee, out Faction fleet, out _)) return false;
            if (fleet.HostileTo(Faction.OfPlayer)) return false;
            if (trainee.Dead || trainee.Downed || !trainee.Spawned || trainee.IsPrisoner) return false;

            Map map = trainee.MapHeld;
            if (map == null || !map.IsPlayerHome) return false;
            if (map.mapPawns.FreeColonistsSpawnedCount < minFreeColonists) return false;
            if (HasOngoingTraining(trainee)) return false;

            return QuestGen_Pawns.GetPawnTest(AskerParms(fleet), out _);
        }

        protected override void RunInt()
        {
            if (!ModLister.CheckRoyalty("Officer training")) return;

            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;
            if (!TryGetTarget(slate, out Pawn trainee, out Faction fleet, out RoyalTitleDef newTitle)) return;

            TitleTrainingExtension ext = newTitle.GetModExtension<TitleTrainingExtension>();
            Map map = trainee.MapHeld;
            if (map == null) return;

            int trainingDays = ext.trainingDaysRange.RandomInRange;
            TrainingCourse course = courses.Where(c => c?.skill != null)
                                           .RandomElementByWeight(c => Mathf.Max(0.001f, c.weight));
            string courseName = course.ResolvedLabel;

            Pawn asker = QuestGen_Pawns.GetPawn(quest, AskerParms(fleet));

            // ---- slate 文本變數 ------------------------------------------------
            slate.Set("map", map);
            slate.Set("asker", asker);
            slate.Set("trainee", trainee);
            slate.Set("issuerFactionName", fleet.Name);
            slate.Set("playerFactionName", Faction.OfPlayer.Name);
            slate.Set("newTitle", newTitle.GetLabelFor(trainee));
            slate.Set("courseName", courseName);
            slate.Set("courseSkill", course.skill.LabelCap.ToString());
            slate.Set("trainingDays", trainingDays);

            // ---- 訊號 ---------------------------------------------------------
            string inSignal = slate.Get<string>("inSignal");
            string shuttleTag = QuestGenUtility.HardcodedTargetQuestTagWithQuestID("shuttle");
            string sigSent = QuestGenUtility.HardcodedSignalWithQuestID("shuttle.SentSatisfied");
            string sigShuttleDestroyed = QuestGenUtility.HardcodedSignalWithQuestID("shuttle.Destroyed");
            string sigBoardDeadline = QuestGenUtility.HardcodedSignalWithQuestID("boardDeadline");
            string sigGraduate = QuestGenUtility.HardcodedSignalWithQuestID("graduate");
            string sigReturned = QuestGenUtility.HardcodedSignalWithQuestID("returned");
            string sigDied = QuestGenUtility.HardcodedSignalWithQuestID("traineeDied");
            string sigCaptured = QuestGenUtility.HardcodedSignalWithQuestID("captured");
            string sigAborted = QuestGenUtility.HardcodedSignalWithQuestID("aborted");
            string sigFail = QuestGenUtility.HardcodedSignalWithQuestID("failAll");

            // 生成時預先建立穿梭機 Thing(接受時才降落),使用 DMS 自帶運輸機
            Thing shuttle = ThingMaker.MakeThing(transportShipDef.shipThing);
            CompShuttle shuttleComp = shuttle.TryGetComp<CompShuttle>();
            if (shuttleComp == null) return;
            shuttleComp.requiredPawns.Add(trainee);
            shuttleComp.acceptColonists = false;
            QuestUtility.AddQuestTag(shuttle, shuttleTag);

            // ---- 1. 接受 → 穿梭機降落 -------------------------------------------
            quest.AddPart(new QuestPart_SpawnTrainingShuttle
            {
                inSignal = inSignal,
                mapParent = map.Parent,
                transportShipDef = transportShipDef,
                shuttle = shuttle,
                passenger = trainee,
                issuerFactionName = fleet.Name,
                askerName = asker.LabelShort,
                courseName = courseName,
            });

            // ---- 2. 登機期限 ----------------------------------------------------
            QuestPart_Delay boardDelay = new QuestPart_Delay
            {
                inSignalEnable = inSignal,
                inSignalDisable = sigSent,
                delayTicks = (int)(boardingDeadlineDays * GenDate.TicksPerDay),
                isBad = true,
                expiryInfoPart = "DMS_OfficerTraining_BoardDeadline".Translate(),
                expiryInfoPartTip = "DMS_OfficerTraining_BoardDeadlineTip".Translate(),
            };
            boardDelay.outSignalsCompleted.Add(sigBoardDeadline);
            quest.AddPart(boardDelay);

            // ---- 3. 登機離場 → 外借給艦隊 ----------------------------------------
            //  自動歸還設為永不觸發;歸還時機由 QuestPart_TrainingGraduation 呼叫
            //  其 Complete() 決定,完成訊號 sigReturned 代表任務成功。
            QuestPart_LendColonistsToFaction lend = new QuestPart_LendColonistsToFaction
            {
                inSignalEnable = sigSent,
                shuttle = shuttle,
                lendColonistsToFaction = fleet,
                // 計時器要設成真實課程時長:任務頁的「外借中」那行是用這個值算
                // 「N 天後歸還」的,設成天文數字會顯示成一萬八千天。多押一小時,
                // 讓 sigGraduate 一定先觸發,自動歸還只當作保險。
                returnLentColonistsInTicks = trainingDays * GenDate.TicksPerDay + GenDate.TicksPerHour,
                returnMap = map.Parent,
                outSignalColonistsDied = sigDied,
            };
            lend.outSignalsCompleted.Add(sigReturned);
            quest.AddPart(lend);

            // ---- 4. 受訓倒數 → 完訓 ----------------------------------------------
            QuestPart_Delay trainDelay = new QuestPart_Delay
            {
                inSignalEnable = sigSent,
                delayTicks = trainingDays * GenDate.TicksPerDay,
                expiryInfoPart = "DMS_OfficerTraining_CourseInfo".Translate(),
            };
            trainDelay.outSignalsCompleted.Add(sigGraduate);
            quest.AddPart(trainDelay);

            quest.AddPart(new QuestPart_TrainingGraduation
            {
                inSignal = sigGraduate,
                faction = fleet,
                trainee = trainee,
                newTitle = newTitle,
                courseSkill = course.skill,
                courseName = courseName,
                skillXp = ext.skillXp,
                passionUpgradeChance = ext.passionUpgradeChance,
                noTraitBonusXpFactor = ext.noTraitBonusXpFactor,
                outSignalFail = sigFail,
            });

            // ---- 5. 敵對監控(翻臉 → 扣押 / 破局)---------------------------------
            quest.AddPart(new QuestPart_TrainingWatch
            {
                inSignalEnable = inSignal,
                faction = fleet,
                trainee = trainee,
                asker = asker,
                outSignalCaptured = sigCaptured,
                outSignalAborted = sigAborted,
            });

            // ---- 6. 逾時未登機的信件 ---------------------------------------------
            string[] letterVars =
            {
                "traineeName", trainee.LabelShort,
                "issuerFactionName", fleet.Name,
                "askerName", asker.LabelShort,
                "courseName", courseName,
            };
            quest.AddPart(new QuestPart_Letter
            {
                inSignal = sigBoardDeadline,
                letter = LetterMaker.MakeLetter(
                    SupplyChainText.Resolve(OfficerTrainingText.Pack, "missedLetterLabel", letterVars),
                    SupplyChainText.Resolve(OfficerTrainingText.Pack, "missedLetterText", letterVars),
                    LetterDefOf.NegativeEvent, null, null, quest),
            });

            // ---- 7. 失敗來源匯流 --------------------------------------------------
            quest.AddPart(new QuestPart_Pass { inSignal = sigBoardDeadline, outSignal = sigFail });
            quest.AddPart(new QuestPart_Pass { inSignal = sigShuttleDestroyed, outSignal = sigFail });
            quest.AddPart(new QuestPart_Pass { inSignal = sigDied, outSignal = sigFail });
            quest.AddPart(new QuestPart_Pass { inSignal = sigCaptured, outSignal = sigFail });
            quest.AddPart(new QuestPart_Pass { inSignal = sigAborted, outSignal = sigFail });

            // ---- 8. 穿梭機善後 ----------------------------------------------------
            quest.AddPart(new QuestPart_SendShuttleAway
            {
                inSignal = sigFail,
                shuttle = shuttle,
                dropEverything = true,
            });
            quest.AddPart(new QuestPart_SendShuttleAwayOnCleanup
            {
                shuttle = shuttle,
                dropEverything = true,
            });

            // ---- 9. 結局 -----------------------------------------------------------
            quest.AddPart(new QuestPart_QuestEnd { inSignal = sigReturned, outcome = QuestEndOutcome.Success });
            quest.AddPart(new QuestPart_QuestEnd { inSignal = sigFail, outcome = QuestEndOutcome.Fail });
        }
    }
}
