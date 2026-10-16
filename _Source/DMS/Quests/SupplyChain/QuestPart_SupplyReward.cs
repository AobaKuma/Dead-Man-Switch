using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// inSignal:生成標準獎勵物資並空投。
    /// 訊號帶有 VALUE(實際交付貨物市值)時,報酬 = min(VALUE, rewardValueCap) × rewardFactor;
    /// 否則退回 marketValue(合約估值)。
    /// </summary>
    public class QuestPart_SupplyReward : QuestPart
    {
        public string inSignal;
        public float marketValue;        // 合約估值(退回用)
        public float rewardFactor;       // 實際交付市值 → 報酬的倍率(0 = 不用實際市值)
        public float rewardValueCap;     // 實際交付市值計價上限(0 = 不設限)
        public MapParent mapParent;
        public string issuerUnit;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal) return;

            Map map = mapParent?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            float value = marketValue;
            if (rewardFactor > 0f && signal.args.TryGetArg("VALUE", out float delivered) && delivered > 0f)
            {
                if (rewardValueCap > 0f) delivered = Mathf.Min(delivered, rewardValueCap);
                value = delivered * rewardFactor;
            }
            if (value <= 0f) return;

            ThingSetMakerParams parms = default;
            parms.totalMarketValueRange = new FloatRange(value * 0.95f, value * 1.05f);
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
            Scribe_Values.Look(ref rewardFactor, "rewardFactor", 0f);
            Scribe_Values.Look(ref rewardValueCap, "rewardValueCap", 0f);
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Values.Look(ref issuerUnit, "issuerUnit");
        }
    }
}
