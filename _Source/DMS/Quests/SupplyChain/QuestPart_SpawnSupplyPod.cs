using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DMS
{
    /// <summary>
    /// inSignal:於玩家母星地圖空投補給艙建築並設定其 CompSupplyRequest。
    /// inSignalSendAway:期限截止 → 令補給艙卸貨並離場。
    /// </summary>
    public class QuestPart_SpawnSupplyPod : QuestPart
    {
        public string inSignal;
        public string inSignalSendAway;
        public MapParent mapParent;
        public ThingDef podDef;
        public ThingDef skyfallerDef;
        public ThingCategoryDef category;
        public int count;
        public string questTagToAdd;
        public string issuerUnit;

        private Thing pod;
        private bool spawned;

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                foreach (GlobalTargetInfo t in base.QuestLookTargets) yield return t;
                if (pod != null && pod.Spawned) yield return pod;
            }
        }

        // 讓 pod 成為任務的「選擇目標」:
        // 1. 選中 pod 時 InspectGizmoGrid 會自動附上原版「開啟相關任務」gizmo
        //    (判定條件為 QuestLookTargets 或 QuestSelectTargets 包含該 Thing)
        // 2. 任務面板可反向跳轉選中 pod
        public override IEnumerable<GlobalTargetInfo> QuestSelectTargets
        {
            get
            {
                foreach (GlobalTargetInfo t in base.QuestSelectTargets) yield return t;
                if (pod != null && pod.Spawned) yield return pod;
            }
        }

        public override string QuestSelectTargetsLabel => "DMS_SupplyPod_SelectLabel".Translate();

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag == inSignal && !spawned)
            {
                spawned = true;
                SpawnPod();
            }
            else if (signal.tag == inSignalSendAway)
            {
                TrySendAway();
            }
        }

        private void SpawnPod()
        {
            Map map = mapParent?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            pod = ThingMaker.MakeThing(podDef);
            pod.SetFactionDirect(Faction.OfPlayer);
            CompSupplyRequest comp = pod.TryGetComp<CompSupplyRequest>();
            if (comp != null)
            {
                comp.requestedCategory = category;
                comp.requestedCount = count;
            }
            QuestUtility.AddQuestTag(pod, questTagToAdd);

            IntVec3 cell = FindLandingCell(map);
            SkyfallerMaker.SpawnSkyfaller(skyfallerDef, pod, cell, map);

            string[] vars =
            {
                "issuerUnit", issuerUnit,
                "categoryLabel", category.label,
                "count", count.ToString(),
            };
            Find.LetterStack.ReceiveLetter(
                SupplyChainText.Resolve("podLetterLabel", vars),
                SupplyChainText.Resolve("podLetterText", vars),
                LetterDefOf.NeutralEvent, new TargetInfo(cell, map), null, quest);
        }

        /// <summary>有可用著陸信標區時優先降落其中心,否則沿用貿易空投點。</summary>
        private static IntVec3 FindLandingCell(Map map)
        {
            List<ShipLandingArea> zones = ShipLandingBeaconUtility.GetLandingZones(map);
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].Active && zones[i].Clear)
                    return zones[i].CenterCell;
            }
            return DropCellFinder.TradeDropSpot(map);
        }

        private void TrySendAway()
        {
            if (pod == null || pod.Destroyed) return;
            pod.TryGetComp<CompSupplyRequest>()?.SendAway();
            pod = null;
        }

        public override void Cleanup()
        {
            base.Cleanup();
            // 任務因其他原因結束時,不留下殘餘建築
            TrySendAway();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref inSignalSendAway, "inSignalSendAway");
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Defs.Look(ref podDef, "podDef");
            Scribe_Defs.Look(ref skyfallerDef, "skyfallerDef");
            Scribe_Defs.Look(ref category, "category");
            Scribe_Values.Look(ref count, "count");
            Scribe_Values.Look(ref questTagToAdd, "questTagToAdd");
            Scribe_Values.Look(ref issuerUnit, "issuerUnit");
            Scribe_References.Look(ref pod, "pod");
            Scribe_Values.Look(ref spawned, "spawned");
        }
    }
}
