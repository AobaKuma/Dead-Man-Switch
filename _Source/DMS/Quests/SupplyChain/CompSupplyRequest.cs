using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    public class CompProperties_SupplyRequest : CompProperties_Transporter
    {
        // 交付/離場時播放的離開用 skyfaller (純視覺)
        public ThingDef leavingSkyfaller;

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
            bool any = false;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (need <= 0) break;
                if (!Matches(t) || !t.Spawned || t.IsForbidden(Faction.OfPlayer)) continue;
                int take = Mathf.Min(need, t.stackCount);
                TransferableOneWay tr = new TransferableOneWay();
                tr.things.Add(t);
                AddToTheToLoadList(tr, take);
                need -= take;
                any = true;
            }
            if (any)
            {
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
            // 消耗要求數量的符合物品
            int toConsume = requestedCount;
            List<Thing> matching = innerContainer.Where(Matches).ToList();
            for (int i = 0; i < matching.Count && toConsume > 0; i++)
            {
                int take = Mathf.Min(toConsume, matching[i].stackCount);
                Thing taken = innerContainer.Take(matching[i], take);
                toConsume -= take;
                taken.Destroy();
            }
            QuestUtility.SendQuestTargetSignals(parent.questTags, "Delivered", parent.Named("SUBJECT"));
            SendAway();
        }

        /// <summary>交付完成或期限截止時呼叫:卸下剩餘物品、移除建築、播放離場 skyfaller。</summary>
        public void SendAway()
        {
            if (sent) return;
            sent = true;

            // 邊界:pod 仍在降落途中(位於 incoming skyfaller 容器內)或已離開地圖,
            // 沒有可用的 Map 可播離場動畫,直接銷毀即可。
            if (!parent.Spawned || parent.Map == null)
            {
                if (!parent.Destroyed)
                    parent.Destroy(DestroyMode.Vanish);
                return;
            }

            CancelLoad();
            Map map = parent.Map;
            IntVec3 pos = parent.Position;
            innerContainer.TryDropAll(pos, map, ThingPlaceMode.Near);
            ThingDef leaving = Props.leavingSkyfaller;
            parent.Destroy(DestroyMode.Vanish);
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
