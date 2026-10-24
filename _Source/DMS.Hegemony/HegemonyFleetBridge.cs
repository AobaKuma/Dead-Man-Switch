using System;
using System.Reflection;
using FactionTerritories2;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DMS.Hegemony
{
    /// <summary>
    /// DMS 的 <see cref="IFleetSettlementBridge"/> 在 Hegemony 下的實作:補給鏈送往真實據點、援軍依據點實力縮放、
    /// 以及回報 Hegemony 強制的敵對。除了 ExternalHostilityLock 之外只用公開的 HegemonyApi。
    /// DMS's <see cref="IFleetSettlementBridge"/> under Hegemony: supply chains feed real towns, aid scales with town
    /// strength, and Hegemony-forced hostility is reported. Everything but ExternalHostilityLock uses the public
    /// HegemonyApi.
    /// </summary>
    internal sealed class HegemonyFleetBridge : IFleetSettlementBridge
    {
        // 需求係數夾在這個範圍,避免單一極端報價讓某類別獨占。Demand is clamped so one extreme quote can't monopolise.
        private const float MinDemand = 0.25f;
        private const float MaxDemand = 4f;

        // 援軍:近距離滿編,遠距離打折;沒有據點時用最低倍率。Aid: full strength close by, reduced far away.
        private const float NearTiles = 8f;
        private const float FarTiles = 60f;
        private const float FarFactor = 0.6f;
        private const float NoTownFactor = 0.6f;
        private const float WorkshopBonusPerLevel = 0.05f;
        private const int WorkshopBonusLevels = 10;
        private const float MinAidFactor = 0.5f;
        private const float MaxAidFactor = 1.5f;

        // DiplomacyLedger.ForcesHostility 是 internal,沒有公開 API;找不到就不回報鎖定。
        // DiplomacyLedger.ForcesHostility is internal with no public API; when missing, no lock is reported.
        private readonly PropertyInfo ledgerCurrent;
        private readonly MethodInfo forcesHostility;

        internal HegemonyFleetBridge()
        {
            Type ledger = typeof(SettlementState).Assembly.GetType("FactionTerritories2.DiplomacyLedger");
            ledgerCurrent = ledger == null ? null : AccessTools.Property(ledger, "Current");
            forcesHostility = ledger == null ? null
                : AccessTools.Method(ledger, "ForcesHostility", new[] { typeof(Faction), typeof(Faction) });
            if (ledgerCurrent == null || forcesHostility == null || forcesHostility.ReturnType != typeof(bool))
            {
                ledgerCurrent = null;
                forcesHostility = null;
                Log.Warning("[DMS] Hegemony DiplomacyLedger.ForcesHostility(Faction, Faction) was not found; "
                    + "court-martials will not detect Hegemony wars. This Hegemony version is untested.");
            }
        }

        public string ExternalHostilityLock(Faction a, Faction b)
        {
            if (forcesHostility == null)
            {
                return null;
            }
            object ledger = ledgerCurrent.GetValue(null);
            if (ledger == null || !(bool)forcesHostility.Invoke(ledger, new object[] { a, b }))
            {
                return null;
            }
            return "DMS_Hegemony_HostilityLocked".Translate(a.Name, b.Name);
        }

        public Settlement PickSupplyDestination(Faction fleet, PlanetTile near)
        {
            return NearestTown(fleet, near, out _, out _);
        }

        public float DemandFactor(Settlement destination, ThingDef def)
        {
            if (!HegemonyApi.TryGetSettlementState(destination, out SettlementState state)
                || def.BaseMarketValue <= 0f
                || !HegemonyApi.TryGetPrice(state, def, out double price)
                || price <= 0.0)
            {
                return 1f;
            }
            return Mathf.Clamp((float)(price / def.BaseMarketValue), MinDemand, MaxDemand);
        }

        public bool TryDeliver(Settlement destination, Thing stack)
        {
            // TryCredit 失敗時不消耗物品,由 DMS 照舊銷毀。On failure nothing is consumed and DMS destroys it as before.
            return HegemonyApi.TryGetSettlementState(destination, out SettlementState state)
                && HegemonyApi.TryCredit(state, stack, out _);
        }

        public void NotifyDelivered(Faction fleet, Settlement destination, float deliveredValue, float creditedValue)
        {
            // 補給鏈不經過原版 TradeDeal,完成一次交付就回報一次。The supply chain bypasses TradeDeal: report once per delivery.
            if (fleet != null && deliveredValue > 0f)
            {
                HegemonyApi.RecordTrade(Faction.OfPlayer, fleet, deliveredValue);
            }
            string key = creditedValue > 0f ? "DMS_Hegemony_SupplyStored" : "DMS_Hegemony_SupplyIssued";
            Messages.Message(key.Translate(destination.LabelCap, creditedValue.ToStringMoney()),
                destination, MessageTypeDefOf.PositiveEvent, historical: false);
        }

        public float AidStrengthFactor(Faction faction, Map map)
        {
            Settlement town = NearestTown(faction, map.Tile, out SettlementState state, out float distance);
            if (town == null)
            {
                Messages.Message("DMS_Hegemony_AidNoGarrison".Translate(faction.Name, NoTownFactor.ToStringPercent()),
                    MessageTypeDefOf.NeutralEvent, historical: false);
                return NoTownFactor;
            }

            float t = Mathf.InverseLerp(NearTiles, FarTiles, distance);
            float factor = Mathf.Lerp(1f, FarFactor, t);
            int workshop = Math.Max(HegemonyApi.GetWorkshopLevel(state, SpecialBuilding.Weapons),
                HegemonyApi.GetWorkshopLevel(state, SpecialBuilding.Armor));
            factor += WorkshopBonusPerLevel * Math.Min(workshop, WorkshopBonusLevels);
            factor = Mathf.Clamp(factor, MinAidFactor, MaxAidFactor);

            Messages.Message("DMS_Hegemony_AidStrength".Translate(town.LabelCap, Mathf.RoundToInt(distance),
                    factor.ToStringPercent()),
                town, MessageTypeDefOf.NeutralEvent, historical: false);
            return factor;
        }

        /// <summary>
        /// 派系在 Hegemony 有經濟狀態、離 near 最近的據點。同一圖層優先;near 所在圖層沒有據點時取任何圖層的第一座,
        /// 距離視為最遠。
        /// The faction's nearest town with a Hegemony economy. The same layer comes first; with none on near's layer the
        /// first town on any layer is used and treated as far away.
        /// </summary>
        private static Settlement NearestTown(Faction faction, PlanetTile near, out SettlementState state, out float distance)
        {
            state = null;
            distance = float.MaxValue;
            if (faction == null || !HegemonyApi.IsAvailable)
            {
                return null;
            }
            Settlement best = null;
            Settlement otherLayer = null;
            SettlementState otherState = null;
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction != faction || settlement.Destroyed
                    || !HegemonyApi.TryGetSettlementState(settlement, out SettlementState candidate))
                {
                    continue;
                }
                if (!near.Valid || settlement.Tile.Layer != near.Layer)
                {
                    if (otherLayer == null)
                    {
                        otherLayer = settlement;
                        otherState = candidate;
                    }
                    continue;
                }
                float d = Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, near);
                if (d < distance)
                {
                    distance = d;
                    best = settlement;
                    state = candidate;
                }
            }
            if (best == null && otherLayer != null)
            {
                state = otherState;
                distance = FarTiles;
                return otherLayer;
            }
            return best;
        }
    }
}
