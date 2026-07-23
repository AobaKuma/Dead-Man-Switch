using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DMS
{
    /// <summary>
    /// inSignal(接受任務):讓生成時預先建立的穿梭機 Thing 以 TransportShip 降落,
    /// 等待被告登機(登機即離場),並發出抵達信件。
    /// </summary>
    public class QuestPart_SpawnCourtShuttle : QuestPart
    {
        public string inSignal;
        public MapParent mapParent;
        public TransportShipDef transportShipDef;   // DMS 自帶運輸機 def
        public Thing shuttle;          // 生成時以 ThingMaker 建立、已設定 requiredPawns 與 questTags
        public Pawn defendant;
        public string issuerFactionName;
        public string askerName;

        private bool arrived;

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                foreach (GlobalTargetInfo t in base.QuestLookTargets) yield return t;
                if (shuttle != null && shuttle.Spawned) yield return shuttle;
            }
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || arrived) return;
            arrived = true;

            Map map = mapParent?.Map;
            if (map == null || shuttle == null) return;

            TransportShip ship = TransportShipMaker.MakeTransportShip(
                transportShipDef ?? TransportShipDefOf.Ship_Shuttle, null, shuttle);

            ShipJob_Wait wait = (ShipJob_Wait)ShipJobMaker.MakeShipJob(ShipJobDefOf.WaitForever);
            wait.leaveImmediatelyWhenSatisfied = true;
            wait.showGizmos = false;
            ship.AddJob(wait);

            IntVec3 cell = DropCellFinder.GetBestShuttleLandingSpot(map, Faction.OfPlayer);
            ship.ArriveAt(cell, map.Parent);
            ship.Start();

            Find.LetterStack.ReceiveLetter(
                SupplyChainText.Resolve(CourtMartialText.Pack, "arrivedLetterLabel",
                    "defendantName", defendant?.LabelShort ?? "?",
                    "issuerFactionName", issuerFactionName,
                    "askerName", askerName),
                SupplyChainText.Resolve(CourtMartialText.Pack, "arrivedLetterText",
                    "defendantName", defendant?.LabelShort ?? "?",
                    "issuerFactionName", issuerFactionName,
                    "askerName", askerName),
                LetterDefOf.NeutralEvent, new TargetInfo(cell, map), null, quest);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Defs.Look(ref transportShipDef, "transportShipDef");
            Scribe_Values.Look(ref arrived, "arrived");
            // 仿 QuestPart_SpawnThing:未降落時由本 part 深度持有,入世界後改為參照
            if (!arrived)
                Scribe_Deep.Look(ref shuttle, "shuttle");
            else
                Scribe_References.Look(ref shuttle, "shuttle");
            Scribe_References.Look(ref defendant, "defendant");
            Scribe_Values.Look(ref issuerFactionName, "issuerFactionName");
            Scribe_Values.Look(ref askerName, "askerName");
        }
    }

    /// <summary>軍事法庭文本 pack 快取。</summary>
    public static class CourtMartialText
    {
        private static RulePackDef cached;
        public static RulePackDef Pack =>
            cached ??= DefDatabase<RulePackDef>.GetNamed("DMS_CourtMartialLetters");
    }
}
