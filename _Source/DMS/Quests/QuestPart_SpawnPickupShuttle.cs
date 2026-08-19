using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DMS
{
    /// <summary>
    /// 「艦隊派運輸機來接一個人」的共用 QuestPart。
    /// inSignal(通常是接受任務)時,把生成期就建好的穿梭機 Thing 以 TransportShip 降落,
    /// 等待指定 pawn 登機(登機即離場),並發出抵達信件。
    ///
    /// 子類只需要指定用哪個 RulePackDef 解析信件、以及信件裡的變數名。
    /// 注意:QuestPart 是以類別名存檔的,子類命名一旦上線就不要再改。
    /// </summary>
    public abstract class QuestPart_SpawnPickupShuttle : QuestPart
    {
        public string inSignal;
        public MapParent mapParent;
        public TransportShipDef transportShipDef;
        /// <summary>生成期以 ThingMaker 建立、已設定 requiredPawns 與 questTags 的穿梭機。</summary>
        public Thing shuttle;
        public Pawn passenger;
        public string issuerFactionName;
        public string askerName;

        protected bool arrived;

        /// <summary>信件文法所在的 RulePackDef。</summary>
        protected abstract RulePackDef LetterPack { get; }

        protected virtual string ArrivedLabelKeyword => "arrivedLetterLabel";
        protected virtual string ArrivedTextKeyword => "arrivedLetterText";

        /// <summary>
        /// 存檔用的 pawn 欄位標籤。軍事法庭上線時用的是 "defendant",
        /// 為了不讓舊存檔讀不到,子類可以覆寫回舊標籤。
        /// </summary>
        protected virtual string PassengerScribeLabel => "passenger";

        protected virtual string[] LetterVars()
        {
            return new[]
            {
                "passengerName", passenger?.LabelShort ?? "?",
                "issuerFactionName", issuerFactionName ?? "?",
                "askerName", askerName ?? "?",
            };
        }

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

            Map map = mapParent?.Map;
            // arrived 要在確定真的降落之後才立起來 —— 它同時決定 ExposeData 是深度
            // 持有還是只存參照,提前立起來會讓沒降落的穿梭機在存讀後變成 null。
            if (map == null || shuttle == null) return;
            arrived = true;

            TransportShip ship = TransportShipMaker.MakeTransportShip(
                transportShipDef ?? TransportShipDefOf.Ship_Shuttle, null, shuttle);

            ShipJob_Wait wait = (ShipJob_Wait)ShipJobMaker.MakeShipJob(ShipJobDefOf.WaitForever);
            wait.leaveImmediatelyWhenSatisfied = true;
            wait.showGizmos = false;
            ship.AddJob(wait);

            IntVec3 cell = DropCellFinder.GetBestShuttleLandingSpot(map, Faction.OfPlayer);
            ship.ArriveAt(cell, map.Parent);
            ship.Start();

            string[] vars = LetterVars();
            Find.LetterStack.ReceiveLetter(
                SupplyChainText.Resolve(LetterPack, ArrivedLabelKeyword, vars),
                SupplyChainText.Resolve(LetterPack, ArrivedTextKeyword, vars),
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
            Scribe_References.Look(ref passenger, PassengerScribeLabel);
            Scribe_Values.Look(ref issuerFactionName, "issuerFactionName");
            Scribe_Values.Look(ref askerName, "askerName");
        }
    }
}
