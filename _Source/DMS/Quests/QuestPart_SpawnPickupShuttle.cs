using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

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
            // arrived 要在確定真的降落之後才立起來：它同時決定 ExposeData 是深度
            // 持有還是只存參照,提前立起來會讓沒降落的穿梭機在存讀後變成 null。
            if (map == null || shuttle == null) return;
            arrived = true;

            TransportShip ship = TransportShipMaker.MakeTransportShip(
                transportShipDef ?? TransportShipDefOf.Ship_Shuttle, null, shuttle);

            ShipJob_Wait wait = (ShipJob_Wait)ShipJobMaker.MakeShipJob(ShipJobDefOf.WaitForever);
            wait.leaveImmediatelyWhenSatisfied = true;
            wait.showGizmos = false;
            ship.AddJob(wait);

            IntVec3 cell = FindLandingSpot(map);
            ship.ArriveAt(cell, map.Parent);
            ship.Start();

            string[] vars = LetterVars();
            Find.LetterStack.ReceiveLetter(
                SupplyChainText.Resolve(LetterPack, ArrivedLabelKeyword, vars),
                SupplyChainText.Resolve(LetterPack, ArrivedTextKeyword, vars),
                LetterDefOf.NeutralEvent, new TargetInfo(cell, map), null, quest);
        }

        /// <summary>
        /// 挑降落點。原版 GetBestShuttleLandingSpot 的問題：著陸信標區只檢查有沒有東西擋著，敵人就在旁邊也照降；
        /// 最後一層保底完全不看敵人與可達性，甚至可能回傳站不上去的格子；而且從不確認乘客走不走得到。
        /// 接人的任務裡乘客上不了機就是逾時失敗，所以這裡依序嘗試、每一層都要求乘客走得到：
        /// 1. 安全的著陸信標區（尊重玩家的指定）；
        /// 2. 殖民地建築旁的安全點（原版的敵人 35 格、火場 15 格、可達殖民地等檢查）；
        /// 3. 全圖隨機的安全點；
        /// 4. 放寬距離的安全點；
        /// 5. 不安全但玩家指定的信標區；
        /// 6. 原版結果。
        /// 尺寸用實際的運輸機，不是原版寫死的 ThingDefOf.Shuttle。
        ///
        /// Picks the landing spot. Vanilla GetBestShuttleLandingSpot's gaps: a landing-beacon zone is only checked for
        /// blocking things, so the ship lands right beside hostiles; the last fallback ignores hostiles and
        /// reachability entirely and can even return an unstandable cell; and nothing checks the passenger can walk
        /// there. For a pickup quest a passenger who can't board means a timeout failure, so each tier below also
        /// requires the passenger to reach it: a safe beacon zone (the player's choice comes first), a safe spot by
        /// colony buildings (vanilla's 35-cell hostile / 15-cell fire / reaches-the-colony checks), a safe random spot,
        /// a safe spot with relaxed distances, the beacon zone even if unsafe, then vanilla's answer.
        /// Sized by the actual ship rather than vanilla's hard-coded ThingDefOf.Shuttle.
        /// </summary>
        protected virtual IntVec3 FindLandingSpot(Map map)
        {
            const int Attempts = 4;
            Faction player = Faction.OfPlayer;
            IntVec2 size = shuttle.def.Size;
            IntVec2 paddedSize = size + new IntVec2(2, 2);

            bool hasBeacon = DropCellFinder.TryFindShipLandingArea(map, out IntVec3 beacon, out _);
            if (hasBeacon && DropCellFinder.SkyfallerCanLandAt(beacon, map, size, player) && PassengerCanReach(map, beacon))
            {
                return beacon;
            }

            for (int i = 0; i < Attempts; i++)
            {
                IntVec3 cell = DropCellFinder.TryFindSafeLandingSpotCloseToColony(map, size, player);
                if (cell.IsValid && PassengerCanReach(map, cell)) return cell;
            }
            for (int i = 0; i < Attempts; i++)
            {
                if (DropCellFinder.FindSafeLandingSpot(out IntVec3 cell, player, map, 35, 15, 25, paddedSize)
                    && PassengerCanReach(map, cell)) return cell;
            }
            for (int i = 0; i < Attempts; i++)
            {
                if (DropCellFinder.FindSafeLandingSpot(out IntVec3 cell, player, map, 15, 8, 10, paddedSize)
                    && PassengerCanReach(map, cell)) return cell;
            }

            if (hasBeacon)
            {
                return beacon;
            }
            Log.Warning($"[DMS] {GetType().Name}: no safe landing spot the passenger can reach on {map}; using vanilla's pick.");
            return DropCellFinder.GetBestShuttleLandingSpot(map, player);
        }

        /// <summary>
        /// 乘客走不走得到降落點。乘客不在這張地圖上（出門、商隊中）時無從判斷，一律放行。
        /// Whether the passenger can walk to the spot. A passenger off this map (away, in a caravan) can't be judged and passes.
        /// </summary>
        private bool PassengerCanReach(Map map, IntVec3 cell)
        {
            if (passenger == null || !passenger.Spawned || passenger.Map != map) return true;
            return map.reachability.CanReach(passenger.Position, cell, PathEndMode.Touch,
                TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly));
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
