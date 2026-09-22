using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 軍事法庭:玩家與艦隊敵對時生成。
    /// 流程:接受→休戰+穿梭機接走被告→調查(隨機天數)→宣判(降一級,
    /// 服刑天數與官階反比,最少 minDetentionDays)→服刑期滿空投歸還→
    /// 按被告社交/交談能力擲盟友判定,成功→盟友,失敗→維持中立。
    /// 背叛/被告死亡/休戰破裂→恢復敵對,任務失敗。
    ///
    /// 被告優先取官階最高者;沒有任何持銜者時改取市場價值最高的殖民者(見 FindDefendant)。
    /// 封存科技隱匿級的永久敵對只能靠本任務解除,詳見 OccultechSanctionUtility。
    /// </summary>
    public class QuestNode_Root_CourtMartial : QuestNode
    {
        public FactionDef fleetFactionDef;
        public TransportShipDef transportShipDef;   // 接送用運輸機(DMS_Ship_TransportShuttle)
        public IntRange investigationDaysRange = new IntRange(3, 6);
        // 官階 seniority → 服刑天數(反比曲線)
        public SimpleCurve seniorityToDetentionDaysCurve;
        public int minDetentionDays = 5;
        public float boardingDeadlineDays = 2f;
        public float baseAllyChance = 0.15f;
        public float allyChancePerSocialLevel = 0.03f;
        public int allyGoodwill = 75;
        // 無罪釋放機率(官階越高、社交越好越可能無罪;無罪→立即歸還+直接盟友)
        public float baseAcquitChance = 0.10f;
        public float acquitChancePerSocialLevel = 0.02f;
        public float acquitChancePerSeniority = 0.01f;
        public float maxAcquitChance = 0.75f;
        // 封存科技隱匿級審判:官階最高者必須實際服刑一年(60 天),不得無罪釋放、不得升為盟友。
        // 見 OccultechSanctionUtility / GameComponent_OccultechSanction。
        public int occultedDetentionDays = 60;

        private Faction Fleet => fleetFactionDef != null
            ? Find.FactionManager.FirstFactionOfDef(fleetFactionDef)
            : null;

        /// <summary>
        /// 挑選被告。優先取官階最高者;一個持銜者都沒有時退而求其次,改取市場價值最高的殖民者：
        /// 艦隊在名冊上找不到人，就直接向殖民地索討他們最看重的那一個。
        ///
        /// 這個 fallback 是封存科技隱匿級永久敵對的保險:軍事法庭是唯一出路,若因為殖民地
        /// 當下沒有任何持銜者而生不出任務,存檔就會永遠卡在敵對(見 OccultechSanctionUtility)。
        ///
        /// 兩條路徑都排除任務借住者(quest lodger):他們屬於別的任務,借出去會讓兩邊的
        /// QuestPart 互相打架。
        /// </summary>
        private static Pawn FindDefendant(Faction fleet, Map map)
        {
            Pawn best = null;
            int bestSeniority = -1;
            Pawn richest = null;
            float bestValue = -1f;

            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (QuestUtility.IsQuestLodger(p))
                {
                    continue;
                }

                RoyalTitleDef t = p.royalty?.GetCurrentTitle(fleet);
                if (t != null && t.seniority > bestSeniority)
                {
                    bestSeniority = t.seniority;
                    best = p;
                }

                float value = p.MarketValue;
                if (value > bestValue)
                {
                    bestValue = value;
                    richest = p;
                }
            }

            return best ?? richest;
        }

        protected override bool TestRunInt(Slate slate)
        {
            // 不要求 Royalty:本任務的 Def 已移出 1.6/Royalty/，無 DLC 時仍會載入。
            // 軍銜相關的部分(被告挑選、降階、刑期曲線)全部都做了 null 防護：
            // 沒有軍銜系統時被告改由市場價值決定、宣判時略過降階，流程照常跑完。
            // 這條路徑必須在無 Royalty 時也能走，否則封存科技隱匿級的永久敵對沒有出路。
            if (transportShipDef?.shipThing == null) return false;
            Faction fleet = Fleet;
            if (fleet == null || !fleet.HostileTo(Faction.OfPlayer)) return false;
            Map map = QuestGen_Get.GetMap(false, null);
            return map != null && FindDefendant(fleet, map) != null;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;
            Map map = QuestGen_Get.GetMap(false, null);
            Faction fleet = Fleet;
            Pawn defendant = FindDefendant(fleet, map);
            // 被告可能是 FindDefendant 的 fallback 挑出來的無銜殖民者,title 允許為 null。
            // seniority 0 在刑期曲線上對應最長刑期，沒有軍銜就沒有從輕的餘地。
            RoyalTitleDef title = defendant.royalty?.GetCurrentTitle(fleet);
            int seniority = title?.seniority ?? 0;

            int investigationDays = investigationDaysRange.RandomInRange;
            int detentionDays = Mathf.Max(minDetentionDays,
                Mathf.RoundToInt(seniorityToDetentionDaysCurve?.Evaluate(seniority)
                    ?? (15f - seniority / 100f)));

            // 任務敘述分流:既有的三段文本通篇圍繞「我們官階最高的軍官」與「至少降階一級」寫成,
            // 被告沒有可褫奪的軍銜時整段語意都對不上。用 grammar 常數挑另一組文本
            // (見 questDescriptionRules 的 defendantRanked 條件),條件與 QuestPart_CourtVerdict
            // 的 demoted 判定完全一致,敘述承諾的降階就一定會發生。
            QuestGen.AddQuestDescriptionConstants(new Dictionary<string, string>
            {
                { "defendantRanked", seniority > 0 ? "True" : "False" },
            });

            // 隱匿級永久敵對下的審判:刑期固定一年,無罪判定與盟友判定一律關閉。
            // 這是永久敵對唯一的解除途徑,不能靠官階高就輕輕放過。
            bool occultechTrial = GameComponent_OccultechSanction.CompSafe?.PermanentHostile ?? false;
            float trialAllyChance = baseAllyChance;
            float trialAcquitChance = baseAcquitChance;
            float trialAcquitPerSocial = acquitChancePerSocialLevel;
            float trialAcquitPerSeniority = acquitChancePerSeniority;
            float trialAllyPerSocial = allyChancePerSocialLevel;
            if (occultechTrial)
            {
                detentionDays = occultedDetentionDays;
                trialAllyChance = 0f;
                trialAllyPerSocial = 0f;
                trialAcquitChance = 0f;
                trialAcquitPerSocial = 0f;
                trialAcquitPerSeniority = 0f;
            }

            // 憲兵指揮部士官(敵對陣營,不能限制非敵對)
            Pawn asker = QuestGen_Pawns.GetPawn(quest, new QuestGen_Pawns.GetPawnParms
            {
                mustBeOfFaction = fleet,
                canGeneratePawn = true,
                ifWorldPawnThenMustBeFree = true,
            });

            // slate 文本變數
            slate.Set("map", map);
            slate.Set("asker", asker);
            slate.Set("defendant", defendant);
            slate.Set("issuerFactionName", fleet.Name);
            slate.Set("playerFactionName", Faction.OfPlayer.Name);
            slate.Set("investigationDays", investigationDays);
            slate.Set("detentionDays", detentionDays);
            quest.challengeRating = 2;

            // 訊號
            string inSignal = slate.Get<string>("inSignal");
            string shuttleTag = QuestGenUtility.HardcodedTargetQuestTagWithQuestID("shuttle");
            string sigSent = QuestGenUtility.HardcodedSignalWithQuestID("shuttle.SentSatisfied");
            string sigShuttleDestroyed = QuestGenUtility.HardcodedSignalWithQuestID("shuttle.Destroyed");
            string sigBoardDeadline = QuestGenUtility.HardcodedSignalWithQuestID("boardDeadline");
            string sigVerdict = QuestGenUtility.HardcodedSignalWithQuestID("verdict");
            string sigAcquitted = QuestGenUtility.HardcodedSignalWithQuestID("acquitted");
            string sigGuilty = QuestGenUtility.HardcodedSignalWithQuestID("guilty");
            string sigServed = QuestGenUtility.HardcodedSignalWithQuestID("served");
            string sigReturned = QuestGenUtility.HardcodedSignalWithQuestID("returned");
            string sigDied = QuestGenUtility.HardcodedSignalWithQuestID("defendantDied");
            string sigTruceBroken = QuestGenUtility.HardcodedSignalWithQuestID("truceBroken");
            string sigFail = QuestGenUtility.HardcodedSignalWithQuestID("failAll");

            // 生成時預先建立穿梭機 Thing(接受時才降落),使用 DMS 自帶運輸機
            Thing shuttle = ThingMaker.MakeThing(transportShipDef.shipThing);
            CompShuttle shuttleComp = shuttle.TryGetComp<CompShuttle>();
            shuttleComp.requiredPawns.Add(defendant);
            shuttleComp.acceptColonists = false;
            QuestUtility.AddQuestTag(shuttle, shuttleTag);

            // 1. 接受→休戰(外交結算也在此 part)
            quest.AddPart(new QuestPart_CourtTruce
            {
                inSignalEnable = inSignal,
                faction = fleet,
                defendant = defendant,
                inSignalSuccess = sigReturned,
                inSignalAcquitted = sigAcquitted,
                inSignalFail = sigFail,
                outSignalTruceBroken = sigTruceBroken,
                baseAllyChance = trialAllyChance,
                allyChancePerSocialLevel = trialAllyPerSocial,
                allyGoodwill = allyGoodwill,
                occultechTrial = occultechTrial,
            });

            // 2. 接受→穿梭機降落
            quest.AddPart(new QuestPart_SpawnCourtShuttle
            {
                inSignal = inSignal,
                mapParent = map.Parent,
                transportShipDef = transportShipDef,
                shuttle = shuttle,
                passenger = defendant,
                issuerFactionName = fleet.Name,
                askerName = asker.LabelShort,
            });

            // 3. 登機期限
            QuestPart_Delay boardDelay = new QuestPart_Delay
            {
                inSignalEnable = inSignal,
                inSignalDisable = sigSent,
                delayTicks = (int)(boardingDeadlineDays * GenDate.TicksPerDay),
                isBad = true,
                expiryInfoPart = "DMS_CourtMartial_BoardDeadline".Translate(),
                expiryInfoPartTip = "DMS_CourtMartial_BoardDeadlineTip".Translate(),
            };
            boardDelay.outSignalsCompleted.Add(sigBoardDeadline);
            quest.AddPart(boardDelay);

            // 4. 登機離場→外借給艦隊。歸還時機完全由訊號鏈控制:
            //    自動歸還設為永不觸發,由 QuestPart_CourtVerdict(無罪)或
            //    QuestPart_ReturnDefendant(刑滿)呼叫其 Complete() 歸還並發出 sigReturned。
            QuestPart_LendColonistsToFaction lend = new QuestPart_LendColonistsToFaction
            {
                inSignalEnable = sigSent,
                shuttle = shuttle,
                lendColonistsToFaction = fleet,
                // 同 OfficerTraining:設成真實時長(調查 + 服刑),否則任務頁會顯示
                // 「一萬多天後歸還」。多押一小時讓訊號鏈先跑,自動歸還只當保險。
                returnLentColonistsInTicks = (investigationDays + detentionDays) * GenDate.TicksPerDay + GenDate.TicksPerHour,
                returnMap = map.Parent,
                outSignalColonistsDied = sigDied,
            };
            lend.outSignalsCompleted.Add(sigReturned);
            quest.AddPart(lend);

            // 5. 調查期滿→宣判
            QuestPart_Delay verdictDelay = new QuestPart_Delay
            {
                inSignalEnable = sigSent,
                delayTicks = investigationDays * GenDate.TicksPerDay,
                expiryInfoPart = "DMS_CourtMartial_TrialInfo".Translate(),
            };
            verdictDelay.outSignalsCompleted.Add(sigVerdict);
            quest.AddPart(verdictDelay);

            // 宣判:擲無罪判定;無罪→立即歸還+sigAcquitted;有罪→降一級+sigGuilty
            quest.AddPart(new QuestPart_CourtVerdict
            {
                inSignal = sigVerdict,
                faction = fleet,
                defendant = defendant,
                detentionDays = detentionDays,
                outSignalAcquitted = sigAcquitted,
                outSignalGuilty = sigGuilty,
                baseAcquitChance = trialAcquitChance,
                acquitChancePerSocialLevel = trialAcquitPerSocial,
                acquitChancePerSeniority100 = trialAcquitPerSeniority,
                maxAcquitChance = occultechTrial ? 0f : maxAcquitChance,
            });

            // 5b. 有罪→服刑倒數→刑滿歸還
            QuestPart_Delay detentionDelay = new QuestPart_Delay
            {
                inSignalEnable = sigGuilty,
                delayTicks = detentionDays * GenDate.TicksPerDay,
                expiryInfoPart = "DMS_CourtMartial_DetentionInfo".Translate(),
            };
            detentionDelay.outSignalsCompleted.Add(sigServed);
            quest.AddPart(detentionDelay);

            quest.AddPart(new QuestPart_ReturnDefendant { inSignal = sigServed });

            // 6. 失敗來源匯流:登機逾時/穿梭機被毀/休戰破裂/被告死亡
            quest.AddPart(new QuestPart_Pass { inSignal = sigBoardDeadline, outSignal = sigFail });
            quest.AddPart(new QuestPart_Pass { inSignal = sigShuttleDestroyed, outSignal = sigFail });
            quest.AddPart(new QuestPart_Pass { inSignal = sigTruceBroken, outSignal = sigFail });
            quest.AddPart(new QuestPart_Pass { inSignal = sigDied, outSignal = sigFail });

            // 7. 逾時未登機時送走穿梭機;任務結束時清理
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

            // 8. 結局
            quest.AddPart(new QuestPart_QuestEnd { inSignal = sigReturned, outcome = QuestEndOutcome.Success });
            quest.AddPart(new QuestPart_QuestEnd { inSignal = sigFail, outcome = QuestEndOutcome.Fail });
        }
    }
}
