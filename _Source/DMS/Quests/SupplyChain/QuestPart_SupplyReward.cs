using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DMS
{
    /// <summary>inSignal:以指定市場價值生成標準獎勵物資並空投。</summary>
    public class QuestPart_SupplyReward : QuestPart
    {
        public string inSignal;
        public float marketValue;
        public MapParent mapParent;
        public string issuerUnit;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal) return;

            Map map = mapParent?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            ThingSetMakerParams parms = default;
            parms.totalMarketValueRange = new FloatRange(marketValue * 0.95f, marketValue * 1.05f);
            List<Thing> things = ThingSetMakerDefOf.Reward_ItemsStandard.root.Generate(parms);

            IntVec3 spot = DropCellFinder.TradeDropSpot(map);
            DropPodUtility.DropThingsNear(spot, map, things);

            Find.LetterStack.ReceiveLetter(
                SupplyChainText.Resolve("paymentLetterLabel", "issuerUnit", issuerUnit),
                SupplyChainText.Resolve("paymentLetterText", "issuerUnit", issuerUnit),
                LetterDefOf.PositiveEvent, new TargetInfo(spot, map), null, quest);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref marketValue, "marketValue");
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Values.Look(ref issuerUnit, "issuerUnit");
        }
    }
}
