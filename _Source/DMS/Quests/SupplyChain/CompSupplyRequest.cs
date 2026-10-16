using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace DMS
{
    public class CompProperties_SupplyRequest : CompProperties_Transporter
    {
        // 交付/離場時播放的離開用 skyfaller (純視覺)
        public ThingDef leavingSkyfaller;
        // 起飛後留在原地的無陣營底座建築 (可供玩家拆除回收);null 則不留
        public ThingDef platformDef;

        public CompProperties_SupplyRequest()
        {
            compClass = typeof(CompSupplyRequest);
        }
    }

    /// <summary>
    /// 繼承 CompTransporter 以重用原版裝載對話框、leftToLoad 與殖民者搬運 job。
    /// 追加:類別物品計數、自動指定裝載、確認交付 gizmo。
    /// </summary>
    public class CompSupplyRequest : CompTransporter
    {
        public ThingCategoryDef requestedCategory;
        public int requestedCount;
        private bool sent;

        public new CompProperties_SupplyRequest Props => (CompProperties_SupplyRequest)props;

        public bool Matches(Thing t) => requestedCategory != null && t.def.IsWithinCategory(requestedCategory);

        // 已裝入且符合類別的數量
        public int MatchingCount
        {
            get
            {
                int n = 0;
                foreach (Thing t in innerContainer)
                {
                    if (Matches(t)) n += t.stackCount;
                }
                return n;
            }
        }

        public bool Satisfied => MatchingCount >= requestedCount;

        // 已在裝載清單上、尚未搬入的符合類別數量
        private int MatchingPlannedCount
        {
            get
            {
                if (leftToLoad == null) return 0;
                int n = 0;
                for (int i = 0; i < leftToLoad.Count; i++)
                {
                    var tr = leftToLoad[i];
                    if (tr.HasAnyThing && Matches(tr.AnyThing)) n += tr.CountToTransfer;
                }
                return n;
            }
        }

        public override string CompInspectStringExtra()
        {
            string baseStr = base.CompInspectStringExtra();
            string mine = "DMS_SupplyPod_Progress".Translate(
                MatchingCount, requestedCount, requestedCategory?.LabelCap.ToString() ?? "?");
            return baseStr.NullOrEmpty() ? mine : baseStr + "\n" + mine;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;

            if (parent.Faction != Faction.OfPlayer || sent) yield break;

            // 「開啟相關任務」gizmo:原版只有 Pawn / CompShuttle 等會主動呼叫,一般 Building 不會,
            // 這裡自行附上 (以 QuestLookTargets / QuestSelectTargets 是否包含本艙判定)
            foreach (Gizmo g in QuestUtility.GetQuestRelatedGizmos(parent))
                yield return g;

            // 原版只在艙內已有物品時提供「取消裝載」;若裝載清單上有東西但尚未搬入任何物品,
            // 這裡補上取消 gizmo,確保玩家在裝載途中隨時可以中止。
            if (LoadingInProgressOrReadyToLaunch && !innerContainer.Any && AnythingLeftToLoad)
            {
                yield return new Command_Action
                {
                    defaultLabel = "CommandCancelLoad".Translate(),
                    defaultDesc = "CommandCancelLoadDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true),
                    action = delegate
                    {
                        SoundDefOf.Designate_Cancel.PlayOneShotOnCamera();
                        CancelLoad();
                    },
                };
            }

            // 自動把地圖上符合類別的物品加入裝載清單
            yield return new Command_Action
            {
                defaultLabel = "DMS_SupplyPod_AutoDesignate".Translate(),
                defaultDesc = "DMS_SupplyPod_AutoDesignateDesc".Translate(requestedCategory?.label ?? "?"),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter", true),
                action = AutoDesignate,
            };

            // 確認交付
            Command_Action deliver = new Command_Action
            {
                defaultLabel = "DMS_SupplyPod_Deliver".Translate(),
                defaultDesc = "DMS_SupplyPod_DeliverDesc".Translate(requestedCount, requestedCategory?.label ?? "?"),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchShip", true),
                action = Deliver,
            };
            if (!Satisfied)
                deliver.Disable("DMS_SupplyPod_NotEnough".Translate(MatchingCount, requestedCount));
            yield return deliver;
        }

        private void AutoDesignate()
        {
            Map map = parent.Map;
            int need = requestedCount - MatchingCount - MatchingPlannedCount;
            if (need <= 0)
            {
                Messages.Message("DMS_SupplyPod_AlreadyDesignated".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                return;
            }
            // 已在裝載清單上的物品不重複指定 (同一 Thing 加入兩次會使 transferable 數量重複計算)
            HashSet<Thing> alreadyPlanned = new HashSet<Thing>();
            if (leftToLoad != null)
            {
                for (int i = 0; i < leftToLoad.Count; i++)
                    alreadyPlanned.AddRange(leftToLoad[i].things);
            }
            bool any = false;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (need <= 0) break;
                if (!Matches(t) || !t.Spawned || t.IsForbidden(Faction.OfPlayer) || alreadyPlanned.Contains(t)) continue;
                if (!map.reachability.CanReach(parent.Position, t, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly))) continue;
                int take = Mathf.Min(need, t.stackCount);
                TransferableOneWay tr = new TransferableOneWay();
                tr.things.Add(t);
                AddToTheToLoadList(tr, take);
                need -= take;
                any = true;
            }
            if (any)
            {
                // 裝載已在進行中時不可重設 groupID,否則正在搬運的殖民者 job 會失效
                if (!LoadingInProgressOrReadyToLaunch)
                    TransporterUtility.InitiateLoading(Gen.YieldSingle((CompTransporter)this));
                Messages.Message("DMS_SupplyPod_Designated".Translate(), parent, MessageTypeDefOf.TaskCompletion, false);
            }
            else
            {
                Messages.Message("DMS_SupplyPod_NoneFound".Translate(requestedCategory?.label ?? "?"), parent, MessageTypeDefOf.RejectInput, false);
            }
        }

        public void Deliver()
        {
            if (sent || !Satisfied) return;
            // 消耗要求數量的符合物品,並累計實際交付市值(品質/耐久/汙損都反映在 MarketValue)
            int toConsume = requestedCount;
            float deliveredValue = 0f;
            List<Thing> matching = innerContainer.Where(Matches).ToList();
            for (int i = 0; i < matching.Count && toConsume > 0; i++)
            {
                int take = Mathf.Min(toConsume, matching[i].stackCount);
                Thing taken = innerContainer.Take(matching[i], take);
                toConsume -= take;
                deliveredValue += taken.MarketValue * taken.stackCount;
                taken.Destroy();
            }
            // 任務端以 VALUE 計算報酬;子任務結束時的 Cleanup 會經 SendAway 把本艙送走
            QuestUtility.SendQuestTargetSignals(parent.questTags, "Delivered",
                parent.Named("SUBJECT"), deliveredValue.Named("VALUE"));
            SendAway();
        }

        /// <summary>交付完成或期限截止時呼叫:卸下剩餘物品、移除建築、於原地留下底座、播放離場 skyfaller。</summary>
        public void SendAway()
        {
            if (sent) return;
            sent = true;

            // 邊界:pod 仍在降落途中(位於 incoming skyfaller 容器內)或已離開地圖,
            // 沒有可用的 Map 可播離場動畫,直接銷毀即可。
            // DestroyMode.QuestLogic:原版不會對 questTags 送出 "pod.Destroyed",
            // 免得被任務端誤判成補給艙遭摧毀而觸發失敗。
            if (!parent.Spawned || parent.Map == null)
            {
                if (!parent.Destroyed)
                    parent.Destroy(DestroyMode.QuestLogic);
                return;
            }

            CancelLoad();
            Map map = parent.Map;
            IntVec3 pos = parent.Position;
            innerContainer.TryDropAll(pos, map, ThingPlaceMode.Near);
            ThingDef leaving = Props.leavingSkyfaller;
            ThingDef platformDef = Props.platformDef;
            parent.Destroy(DestroyMode.QuestLogic);
            // 貨艙脫離底座升空:底座以無陣營建築留在原地,玩家可自行拆除回收
            if (platformDef != null)
            {
                Thing platform = ThingMaker.MakeThing(platformDef);
                platform.SetFactionDirect(null);
                GenSpawn.Spawn(platform, pos, map);
            }
            if (leaving != null)
                SkyfallerMaker.SpawnSkyfaller(leaving, pos, map);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref requestedCategory, "requestedCategory");
            Scribe_Values.Look(ref requestedCount, "requestedCount");
            Scribe_Values.Look(ref sent, "sent");
        }
    }
}
