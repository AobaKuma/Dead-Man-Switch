using System;
using System.Collections.Generic;
using FactionTerritories2;
using RimWorld;
using Verse;

namespace DMS.Hegemony
{
    /// <summary>
    /// 依聚落所屬派系的 MechRosterExtension 改寫 Hegemony 的機兵選擇。
    /// Hegemony 的 AI 聚落平常沒有自己的名單,會退回寫死的 Militor/Lancer/Centipede;
    /// 這裡把那條退路換成派系名單,並濾掉名單外的底盤(例如佔領來的原版機兵規劃)。
    /// 玩家聚落與玩家附庸不處理:那是玩家自己選的名單。
    /// Rewrites Hegemony's chassis choice from the owning faction's MechRosterExtension.
    /// AI towns normally have no roster and fall back to hard-coded Militor/Lancer/Centipede;
    /// that fallback becomes the faction roster, and off-roster chassis are filtered out.
    /// Player towns and player vassals are left alone: their roster is the player's choice.
    /// </summary>
    internal static class FactionMechRosters
    {
        internal static MechRosterExtension For(SettlementState state)
        {
            if (state == null || state.vassal)
            {
                return null;
            }
            Faction faction = state.settlement?.Faction;
            if (faction == null || faction.IsPlayer)
            {
                return null;
            }
            return faction.def.GetModExtension<MechRosterExtension>();
        }

        // 與 Hegemony 的 MechChassis.Available 相同條件。Same test as Hegemony's MechChassis.Available.
        internal static bool Usable(PawnKindDef kind)
        {
            return ModsConfig.BiotechActive && kind?.race?.race != null && kind.race.race.IsMechanoid
                && kind.race.HasComp(typeof(CompOverseerSubject));
        }

        // 與 Hegemony 的 MechChassis.Technology 相同:至少工業,種族科技夾在新石器~超凡之間。
        // Same as Hegemony's MechChassis.Technology: at least Industrial, race tech clamped Neolithic..Archotech.
        internal static TechLevel Technology(PawnKindDef kind)
        {
            int race = Math.Max((int)TechLevel.Neolithic, Math.Min((int)TechLevel.Archotech, (int)kind.race.techLevel));
            return (TechLevel)Math.Max((int)TechLevel.Industrial, race);
        }

        /// <summary>
        /// 這座聚落科技可用的派系名單;科技不足時退回整份可用名單,
        /// 讓 Hegemony 的工廠科技檢查自然擋下,而不是讓原版機兵補位。
        /// The roster usable at this town's technology. Below every chassis' technology it falls back to the
        /// whole usable roster, so Hegemony's own factory check blocks production instead of vanilla mechs filling in.
        /// </summary>
        internal static List<MechRosterEntry> Build(MechRosterExtension extension, SettlementState state)
        {
            TechLevel technology = HegemonyApi.GetTechnology(state);
            List<MechRosterEntry> usable = new List<MechRosterEntry>();
            List<MechRosterEntry> eligible = new List<MechRosterEntry>();
            foreach (MechRosterOption option in extension.roster)
            {
                if (option?.kind == null || option.weight < 1 || !Usable(option.kind))
                {
                    continue;
                }
                MechRosterEntry entry = Find(usable, option.kind);
                if (entry != null)
                {
                    entry.weight = Math.Min(1000, entry.weight + option.weight);
                    continue;
                }
                entry = new MechRosterEntry { kind = option.kind, weight = Math.Min(1000, option.weight) };
                usable.Add(entry);
                if (technology == TechLevel.Undefined || Technology(option.kind) <= technology)
                {
                    eligible.Add(entry);
                }
            }
            return eligible.Count > 0 ? eligible : usable;
        }

        internal static PawnKindDef Pick(MechRosterExtension extension, SettlementState state)
        {
            MechRosterEntry best = null;
            foreach (MechRosterEntry entry in Build(extension, state))
            {
                if (best == null || entry.weight > best.weight)
                {
                    best = entry;
                }
            }
            return best?.kind;
        }

        internal static bool HasOwnRoster(MechRosterExtension extension, SettlementState state)
        {
            if (state.mechRoster == null)
            {
                return false;
            }
            foreach (MechRosterEntry entry in state.mechRoster)
            {
                if (entry?.kind != null && entry.weight > 0 && extension.Allows(entry.kind) && Usable(entry.kind))
                {
                    return true;
                }
            }
            return false;
        }

        private static MechRosterEntry Find(List<MechRosterEntry> entries, PawnKindDef kind)
        {
            foreach (MechRosterEntry entry in entries)
            {
                if (entry.kind == kind)
                {
                    return entry;
                }
            }
            return null;
        }
    }
}
