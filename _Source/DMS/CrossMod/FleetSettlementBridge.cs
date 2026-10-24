using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DMS
{
    /// <summary>
    /// 讓外部模組(目前是 Hegemony 橋接組件 DMS.Hegemony)把艦隊的「實際據點」接進 DMS 的任務與許可。
    /// DMS.dll 不引用任何外部模組;沒有註冊實作時每個查詢都回到 DMS 原本的行為。
    /// Lets an external mod (currently the DMS.Hegemony bridge) plug the fleet's real settlements into DMS quests and
    /// permits. DMS.dll references no external mod; with no implementation registered every query falls back to
    /// DMS's own behaviour.
    /// </summary>
    public interface IFleetSettlementBridge
    {
        /// <summary>
        /// 外部模組是否把這兩派系鎖在敵對(例如 Hegemony 的背叛、殲滅戰、正式戰爭)。非 null 時為玩家可讀的原因。
        /// Whether the external mod locks these factions into hostility (e.g. Hegemony betrayal, annihilation or formal
        /// war). Non-null is a player-facing reason.
        /// </summary>
        string ExternalHostilityLock(Faction a, Faction b);

        /// <summary>補給鏈的收貨據點:離 near 最近、能收貨的艦隊據點;沒有則 null。The supply chain's receiving town, or null.</summary>
        Settlement PickSupplyDestination(Faction fleet, PlanetTile near);

        /// <summary>據點對這種物品的需求係數:1 為平常,大於 1 為短缺。Demand for this item at the town: 1 normal, above 1 short.</summary>
        float DemandFactor(Settlement destination, ThingDef def);

        /// <summary>把整疊物品存入據點儲備;回傳 true 時物品已被消耗。Credits the whole stack; true means it was consumed.</summary>
        bool TryDeliver(Settlement destination, Thing stack);

        /// <summary>一次交付完成後呼叫一次:回報交易並通知玩家。Called once per completed delivery.</summary>
        void NotifyDelivered(Faction fleet, Settlement destination, float deliveredValue, float creditedValue);

        /// <summary>派系向玩家地圖派援軍時的兵力倍率:1 為不變。Strength multiplier for aid sent to a player map: 1 unchanged.</summary>
        float AidStrengthFactor(Faction faction, Map map);
    }

    public static class FleetSettlementBridge
    {
        /// <summary>由外部橋接組件在啟動時設定。Set by an external bridge at startup.</summary>
        public static IFleetSettlementBridge Current;

        public static bool Active => Current != null;

        public static string HostilityLock(Faction a, Faction b)
        {
            if (Current == null || a == null || b == null) return null;
            try
            {
                return Current.ExternalHostilityLock(a, b);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[DMS] Fleet settlement bridge ExternalHostilityLock failed: " + ex, 0x444D5311);
                return null;
            }
        }

        public static Settlement SupplyDestination(Faction fleet, PlanetTile near)
        {
            if (Current == null || fleet == null) return null;
            try
            {
                return Current.PickSupplyDestination(fleet, near);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[DMS] Fleet settlement bridge PickSupplyDestination failed: " + ex, 0x444D5312);
                return null;
            }
        }

        public static float Demand(Settlement destination, ThingDef def)
        {
            if (Current == null || destination == null || def == null) return 1f;
            try
            {
                float factor = Current.DemandFactor(destination, def);
                return float.IsNaN(factor) || factor <= 0f ? 1f : factor;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[DMS] Fleet settlement bridge DemandFactor failed: " + ex, 0x444D5313);
                return 1f;
            }
        }

        public static bool TryDeliver(Settlement destination, Thing stack)
        {
            if (Current == null || destination == null || stack == null) return false;
            try
            {
                return Current.TryDeliver(destination, stack);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[DMS] Fleet settlement bridge TryDeliver failed: " + ex, 0x444D5314);
                return false;
            }
        }

        public static void Delivered(Faction fleet, Settlement destination, float deliveredValue, float creditedValue)
        {
            if (Current == null || destination == null) return;
            try
            {
                Current.NotifyDelivered(fleet, destination, deliveredValue, creditedValue);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[DMS] Fleet settlement bridge NotifyDelivered failed: " + ex, 0x444D5315);
            }
        }

        public static float AidStrength(Faction faction, Map map)
        {
            if (Current == null || faction == null || map == null) return 1f;
            try
            {
                float factor = Current.AidStrengthFactor(faction, map);
                return float.IsNaN(factor) || factor <= 0f ? 1f : factor;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[DMS] Fleet settlement bridge AidStrengthFactor failed: " + ex, 0x444D5316);
                return 1f;
            }
        }
    }
}
