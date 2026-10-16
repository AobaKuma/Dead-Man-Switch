using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace DMS
{
    /// <summary>
    /// 脫序者接納任務。設計與流程驗證見 Plans/DMS_脫序者接納任務_實作計畫.md。
    ///
    /// 接受 → 脫序者（艦隊船員）從地圖邊緣步行進場並加入 → 加入滿 handoverDays 天，檔案還原完成，艦隊同時要求引渡。
    /// 選擇交給玩家（脫序要由玩家自己實踐）：
    /// - 拒絕（或期限內不答覆）：保證專案（量子位元破譯）＋ randomCount 項 randomCategory（I 級）專案，與艦隊敵對，
    ///   啟動綁定脫序者的追殺。
    /// - 交出：檔案沒收；艦隊好感度，皇權 DLC 下另有一份艦隊榮譽；殖民者因交出同伴心情低落。
    /// 做出選擇前脫序者死亡、離開派系或被綁架即失敗，沒有任何後果。
    ///
    /// Deviant refuge quest; design and flow checks in Plans/DMS_脫序者接納任務_實作計畫.md.
    /// Accept → a fleet crewman walks in and joins → handoverDays later the archive is restored and the fleet demands
    /// extradition. The choice is the player's: refuse (or stay silent) for the guaranteed projects plus randomCount
    /// projects of randomCategory, fleet hostility and a huntdown bound to the deviant; or hand them over for goodwill,
    /// Royalty honor and a mood hit, the archive seized. Losing the deviant before the choice fails with no consequences.
    /// </summary>
    public class QuestNode_Root_DeviantRefuge : QuestNode
    {
        public FactionDef fleetFactionDef;
        public PawnKindDef deviantKind;
        public float handoverDays = 5f;
        public float arrivalTimeoutDays = 1f;
        public List<ResearchProjectDef> guaranteedProjects = new List<ResearchProjectDef>();
        public KnowledgeCategoryDef randomCategory;
        public int randomCount = 2;
        public HuntdownDef huntdown;

        // 引渡要求（D-A：選擇交給玩家）。Extradition demand: the choice is the player's.
        /// <summary>答覆期限；逾時視為拒絕。Answer window; silence counts as refusal.</summary>
        public float extraditionDecisionDays = 2f;
        public int extraditionGoodwill = 25;
        /// <summary>皇權 DLC 啟用時給一名殖民者的艦隊榮譽。Fleet honor for one colonist when Royalty is active.</summary>
        public int extraditionHonor = 10;
        /// <summary>交出時殖民者得到原版「放逐殖民者」的心情懲罰。Colonists take vanilla's "colonist banished" mood hit.</summary>
        public bool extraditionMoodPenalty = true;

        private Faction Fleet => fleetFactionDef != null ? Find.FactionManager.FirstFactionOfDef(fleetFactionDef) : null;

        protected override bool TestRunInt(Slate slate)
        {
            Faction fleet = Fleet;
            if (fleet == null || fleet.defeated || !fleet.HasGoodwill || deviantKind == null || huntdown == null) return false;
            // 叛逃者開局本身就是脫序者。The deviant start is already a band of deviants.
            if (Faction.OfPlayer.def == DMS_DefOf.DMS_Deviants) return false;
            if (HuntdownUtility.IsActive(huntdown)) return false;
            return TryFindMap(slate, out _, out _);
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;
            Faction fleet = Fleet;
            if (!TryFindMap(slate, out Map map, out IntVec3 walkInSpot))
            {
                Log.Error("[DMS] Deviant refuge generated with no player home map to walk into.");
                return;
            }

            // 比照 QuestNode_GeneratePawn：生成為艦隊成員、交給任務保留，接受時才加入。
            // As QuestNode_GeneratePawn does: generated as a fleet member, kept by the quest, joining on acceptance.
            Pawn deviant = PawnGenerator.GeneratePawn(new PawnGenerationRequest(deviantKind, fleet,
                PawnGenerationContext.NonPlayer, map.Tile, mustBeCapableOfViolence: true, forbidAnyTitle: true,
                developmentalStages: DevelopmentalStage.Adult));
            QuestGen.AddToGeneratedPawns(deviant);
            if (!deviant.IsWorldPawn())
            {
                Find.WorldPawns.PassToWorld(deviant);
            }
            string deviantTag = QuestGenUtility.HardcodedTargetQuestTagWithQuestID("deviant");
            QuestUtility.AddQuestTag(deviant, deviantTag);

            slate.Set("map", map);
            slate.Set("deviant", deviant);
            slate.Set("fleetName", fleet.Name);

            string inSignal = slate.Get<string>("inSignal");
            // SetFaction 對任務 tag 送出 ChangedFactionToPlayer；QuestPart_PawnsArrive 在接受的同一 tick 先加入再進場。
            // SetFaction sends ChangedFactionToPlayer to the quest tag; QuestPart_PawnsArrive joins before arriving, on the accept tick.
            string sigJoined = QuestGenUtility.HardcodedSignalWithQuestID("deviant.ChangedFactionToPlayer");
            string sigDemand = QuestGenUtility.HardcodedSignalWithQuestID("extraditionDemand");
            string sigDecided = QuestGenUtility.HardcodedSignalWithQuestID("extraditionDecided");
            string sigExtradite = QuestGenUtility.HardcodedSignalWithQuestID("extradite");
            string sigRefuse = QuestGenUtility.HardcodedSignalWithQuestID("refuseExtradition");
            string sigExtradited = QuestGenUtility.HardcodedSignalWithQuestID("extradited");
            string sigHonorChosen = QuestGenUtility.HardcodedSignalWithQuestID("honorReceiverChosen");
            string sigDone = QuestGenUtility.HardcodedSignalWithQuestID("handoverDone");
            string sigFailed = QuestGenUtility.HardcodedSignalWithQuestID("deviantLost");
            int handoverTicks = (int)(handoverDays * GenDate.TicksPerDay);
            // 艦隊有官階可升時才給榮譽。Honor only when the fleet has ranks to climb.
            int honor = ModsConfig.RoyaltyActive && fleet.def.RoyalTitlesAwardableInSeniorityOrderForReading.Count > 0
                ? extraditionHonor
                : 0;

            // 1. 接受 → 加入並步行進場；任務視窗把脫序者列為獎勵。Accept → join and walk in; the deviant shows as the reward.
            QuestPart_PawnsArrive arrive = new QuestPart_PawnsArrive
            {
                inSignal = inSignal,
                arrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                joinPlayer = true,
                mapParent = map.Parent,
                spawnNear = walkInSpot,
                customLetterDef = LetterDefOf.PositiveEvent,
                customLetterLabel = "DMS_DeviantRefuge_ArrivedLabel".Translate(deviant.Named("DEVIANT")),
                customLetterText = "DMS_DeviantRefuge_ArrivedText".Translate(deviant.Named("DEVIANT"),
                    ((int)handoverDays).ToString().Named("DAYS"), fleet.Name.Named("FLEET")),
            };
            arrive.pawns.Add(deviant);
            quest.AddPart(arrive);
            QuestPart_Choice.Choice choice = new QuestPart_Choice.Choice();
            choice.questParts.Add(arrive);
            choice.rewards.Add(new Reward_Pawn { pawn = deviant, detailsHidden = false });
            quest.RewardChoice(new[] { choice }, inSignal);

            // 2. 監控（含逾時未加入），直到玩家對引渡要求做出選擇。Watch, including not joining in time, until the extradition choice.
            quest.AddPart(new QuestPart_DeviantWatch
            {
                inSignalEnable = inSignal,
                inSignalDisable = sigDecided,
                deviant = deviant,
                arrivalTimeoutTicks = (int)(arrivalTimeoutDays * GenDate.TicksPerDay),
                outSignalFailed = sigFailed,
            });

            // 3. 加入滿 handoverDays 天 → 檔案還原完成，艦隊同時要求引渡。handoverDays after joining → the extradition demand.
            // 原版 QuestPart_Delay 以剩餘時間代入 {0}、以日期代入提示的 {0}。Vanilla fills {0} with the time left, and the tip's {0} with the date.
            QuestPart_Delay handoverDelay = new QuestPart_Delay
            {
                inSignalEnable = sigJoined,
                inSignalDisable = sigFailed,
                delayTicks = handoverTicks,
                expiryInfoPart = "DMS_DeviantRefuge_HandoverInfo".Translate(),
                expiryInfoPartTip = "DMS_DeviantRefuge_HandoverInfoTip".Translate(),
            };
            handoverDelay.outSignalsCompleted.Add(sigDemand);
            quest.AddPart(handoverDelay);

            // 4. 引渡要求：選擇交給玩家；期限內沒答覆視為拒絕。The extradition demand: the player chooses; silence is refusal.
            quest.AddPart(new QuestPart_DeviantExtraditionDemand
            {
                inSignalEnable = sigDemand,
                deviant = deviant,
                fleet = fleet,
                decisionTicks = (int)(extraditionDecisionDays * GenDate.TicksPerDay),
                goodwillReward = extraditionGoodwill,
                honorReward = honor,
                moodPenalty = extraditionMoodPenalty,
                outSignalDecided = sigDecided,
                outSignalExtradite = sigExtradite,
                outSignalRefuse = sigRefuse,
                outSignalFailed = sigFailed,
            });

            // 5a. 交出：獎勵，檔案沒收。Hand over: rewards, archive seized.
            quest.AddPart(new QuestPart_DeviantExtradition
            {
                inSignal = sigExtradite,
                deviant = deviant,
                fleet = fleet,
                goodwillReward = extraditionGoodwill,
                honorReward = honor,
                moodPenalty = extraditionMoodPenalty,
                outSignalDone = sigExtradited,
                outSignalHonorChosen = sigHonorChosen,
                outSignalFailed = sigFailed,
            });
            if (honor > 0)
            {
                // 原版：選人信件送出 CHOSEN，這個 part 把榮譽給他。Vanilla: the choose-pawn letter sends CHOSEN, this gives them the honor.
                quest.AddPart(new QuestPart_GiveRoyalFavor { inSignal = sigHonorChosen, faction = fleet, amount = honor });
            }

            // 5b. 拒絕：授予、敵對、追殺。Refuse: grant, hostility, hunt.
            quest.AddPart(new QuestPart_DeviantHandover
            {
                inSignalEnable = sigRefuse,
                deviant = deviant,
                fleet = fleet,
                guaranteedProjects = guaranteedProjects.ToList(),
                randomCategory = randomCategory,
                randomCount = randomCount,
                huntdown = huntdown,
                outSignalDone = sigDone,
                outSignalFailed = sigFailed,
            });

            // 6. 結局。兩種選擇都算完成；有榮譽要分配時等選完人。失敗信件由各 part 自己發。
            // Endings: both choices complete the quest; with honor to hand out it waits for the pick. Parts send their own failure letters.
            quest.AddPart(new QuestPart_QuestEnd { inSignal = sigDone, outcome = QuestEndOutcome.Success });
            quest.AddPart(new QuestPart_QuestEnd { inSignal = sigExtradited, outcome = QuestEndOutcome.Success });
            quest.AddPart(new QuestPart_QuestEnd { inSignal = sigHonorChosen, outcome = QuestEndOutcome.Success });
            quest.AddPart(new QuestPart_QuestEnd { inSignal = sigFailed, outcome = QuestEndOutcome.Fail });
        }

        /// <summary>
        /// slate 指定了 map 就只看那張；否則先試 QuestGen_Get.GetMap 擲出的那張，再試其他玩家據點。
        /// 進場點比照原版 QuestNode_GetWalkInSpot。
        /// A map on the slate is the only candidate; otherwise the map QuestGen_Get.GetMap rolls comes first, then the other
        /// player homes. The walk-in spot follows vanilla QuestNode_GetWalkInSpot.
        /// </summary>
        private static bool TryFindMap(Slate slate, out Map map, out IntVec3 walkInSpot)
        {
            walkInSpot = IntVec3.Invalid;
            if (slate.TryGet("map", out map) && map != null)
            {
                return CanWalkInto(map) && TryFindWalkInSpot(map, out walkInSpot);
            }
            Map rolled = QuestGen_Get.GetMap(false, null);
            IEnumerable<Map> candidates = Find.Maps.Where(m => CanWalkInto(m) && m != rolled);
            if (rolled != null && CanWalkInto(rolled))
            {
                candidates = candidates.Prepend(rolled);
            }
            foreach (Map candidate in candidates)
            {
                if (TryFindWalkInSpot(candidate, out walkInSpot))
                {
                    map = candidate;
                    return true;
                }
            }
            map = null;
            return false;
        }

        /// <summary>玩家據點且不在太空層（軌道上沒有地圖邊緣可走進來）。A player home not on a space layer; orbit has no edge to walk in from.</summary>
        private static bool CanWalkInto(Map map)
        {
            return map.IsPlayerHome && !map.Tile.LayerDef.isSpace;
        }

        private static bool TryFindWalkInSpot(Map map, out IntVec3 spot)
        {
            return CellFinder.TryFindRandomEdgeCellWith(c => !c.Fogged(map) && map.reachability.CanReachColony(c), map,
                       CellFinder.EdgeRoadChance_Neutral, out spot)
                   || CellFinder.TryFindRandomEdgeCellWith(c => !c.Fogged(map), map, CellFinder.EdgeRoadChance_Neutral, out spot);
        }
    }
}
