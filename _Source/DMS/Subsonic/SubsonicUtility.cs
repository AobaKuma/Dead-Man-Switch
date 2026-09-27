using System.Collections.Generic;
using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DMS
{
    /// <summary>
    /// 一次次聲波脈衝的參數，技能、次聲波塔、次聲波手雷共用。
    /// Parameters of one subsonic pulse, shared by the module ability, the emitter tower and the grenade.
    /// </summary>
    public class SubsonicPulseProps
    {
        public float radius = 6f;

        /// <summary>疊加到受害者身上的傷病；null 就只判定恐慌。Hediff stacked onto victims; null means panic only.</summary>
        public HediffDef hediff;

        /// <summary>每次脈衝增加的嚴重度（體型大於 1 的生物依體型遞減）。Severity per pulse (reduced by body size above 1).</summary>
        public float severity = 0.3f;

        /// <summary>每個受害者陷入恐慌的機率，再乘上 (1 - 恐懼抗性)。Panic chance per victim, times (1 - fear resistance).</summary>
        public float panicChance = 0.25f;

        /// <summary>null 用原版 PanicFlee。null means vanilla PanicFlee.</summary>
        public MentalStateDef mentalState;

        /// <summary>只影響與來源敵對者；手雷不分敵我。Only affect pawns hostile to the source; the grenade hits everyone.</summary>
        public bool hostileOnly = true;

        /// <summary>次聲波穿牆，不看視線。Infrasound goes through walls; no line-of-sight check.</summary>
        public bool ignoreWalls = true;

        /// <summary>
        /// 聽覺器官（HearingSource 部位）受到的傷害類型；null 就不造成實際傷害。
        /// 走原版 DamageWorker_AddInjury，所以頭盔與天生的鈍器護甲會先抵擋。
        /// Damage dealt to hearing organs (HearingSource parts); null means no real damage.
        /// Goes through vanilla's injury worker, so helmets and natural blunt armor absorb it first.
        /// </summary>
        public DamageDef earDamageDef;

        /// <summary>每個聽覺器官各自承受的傷害量。Damage applied to each hearing organ separately.</summary>
        public float earDamage = 0f;

        public float earArmorPenetration = 0f;

        public FleckDef fleck;
        public SoundDef sound;
    }

    /// <summary>
    /// 次聲波作用於所有「非機械體且具有聽覺」的單位，聽覺越好受害越深；聽覺為 0（全聾、沒有耳朵）即免疫。
    /// 聽覺可超過 100%（仿生耳等），效果會跟著超過基準值。
    /// Infrasound hits every non-mechanoid that can hear, harder the better it hears; zero hearing
    /// (deaf, no ears) means immune. Hearing can exceed 100% (bionic ears etc.) and so can the effect.
    /// </summary>
    public static class SubsonicUtility
    {
        private static readonly List<Pawn> tmpPawns = new List<Pawn>();

        /// <summary>
        /// 在 center 放一次脈衝。source 用來判定敵我（hostileOnly）與排除自己；可為 null。
        /// Fire one pulse at center. source decides hostility (hostileOnly) and is itself excluded; may be null.
        /// </summary>
        public static int DoPulse(Map map, IntVec3 center, SubsonicPulseProps props, Thing source, Faction sourceFaction = null)
        {
            if (map == null || props == null || !center.IsValid) return 0;

            if (props.fleck != null)
                FleckMaker.Static(center, map, props.fleck, props.radius);
            props.sound?.PlayOneShot(new TargetInfo(center, map));

            if (sourceFaction == null)
                sourceFaction = source?.Faction;

            // 先複製：陷入心理狀態、倒地可能會改動原清單。
            // Copy first: mental states and downing may mutate the source list.
            tmpPawns.Clear();
            tmpPawns.AddRange(map.mapPawns.AllPawnsSpawned);

            int affected = 0;
            float radiusSq = props.radius * props.radius;
            MentalStateDef state = props.mentalState ?? MentalStateDefOf.PanicFlee;
            try
            {
                for (int i = 0; i < tmpPawns.Count; i++)
                {
                    Pawn pawn = tmpPawns[i];
                    if (pawn == source || pawn.Dead || !pawn.Spawned) continue;
                    if ((pawn.Position - center).LengthHorizontalSquared > radiusSq) continue;
                    if (props.hostileOnly && !IsHostile(pawn, source, sourceFaction)) continue;
                    float hearing = HearingFactor(pawn);
                    if (hearing <= 0f) continue;
                    if (!props.ignoreWalls && !GenSight.LineOfSight(center, pawn.Position, map, skipFirstCell: true)) continue;

                    Affect(pawn, props, state, source, hearing);
                    affected++;
                }
            }
            finally
            {
                tmpPawns.Clear();
            }
            return affected;
        }

        /// <summary>範圍內是否有會被這次脈衝影響的目標。Whether anything in range would be hit by a pulse.</summary>
        public static bool AnyValidVictim(Map map, IntVec3 center, SubsonicPulseProps props, Thing source, Faction sourceFaction = null)
        {
            if (map == null || props == null) return false;
            if (sourceFaction == null)
                sourceFaction = source?.Faction;
            float radiusSq = props.radius * props.radius;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == source || pawn.Dead || pawn.Downed) continue;
                if ((pawn.Position - center).LengthHorizontalSquared > radiusSq) continue;
                if (props.hostileOnly && !IsHostile(pawn, source, sourceFaction)) continue;
                if (HearingFactor(pawn) <= 0f) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 效果倍率 = 聽覺能力等級；機械體一律 0。
        /// Effect multiplier = hearing capacity level; always 0 for mechanoids.
        /// </summary>
        public static float HearingFactor(Pawn pawn)
        {
            if (pawn?.RaceProps == null || pawn.RaceProps.IsMechanoid || pawn.health?.capacities == null) return 0f;
            return Mathf.Max(0f, pawn.health.capacities.GetLevel(PawnCapacityDefOf.Hearing));
        }

        private static readonly List<BodyPartRecord> tmpEars = new List<BodyPartRecord>();

        private static void DamageEars(Pawn pawn, SubsonicPulseProps props, Thing source)
        {
            if (props.earDamageDef == null || props.earDamage <= 0f) return;

            tmpEars.Clear();
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (part.def.tags.Contains(BodyPartTagDefOf.HearingSource))
                    tmpEars.Add(part);
            }
            try
            {
                for (int i = 0; i < tmpEars.Count; i++)
                {
                    if (pawn.Dead) return;
                    DamageInfo dinfo = new DamageInfo(props.earDamageDef, props.earDamage, props.earArmorPenetration,
                        instigator: source, hitPart: tmpEars[i]);
                    pawn.TakeDamage(dinfo);
                }
            }
            finally
            {
                tmpEars.Clear();
            }
        }

        private static bool IsHostile(Pawn pawn, Thing source, Faction sourceFaction)
        {
            if (source != null) return pawn.HostileTo(source);
            return sourceFaction != null && pawn.HostileTo(sourceFaction);
        }

        private static void Affect(Pawn pawn, SubsonicPulseProps props, MentalStateDef state, Thing source, float hearing)
        {
            // 倍率已在傷害前取得，所以同一次脈衝打壞耳朵不會回頭削弱這一發本身。
            // hearing was read before the damage, so ears ruined by this pulse don't weaken this pulse.
            DamageEars(pawn, props, source);
            if (pawn.Dead || !pawn.Spawned) return;

            if (props.hediff != null && props.severity > 0f)
            {
                // 大型生物的體腔與耳道對同一頻段的共振較弱。
                // Large creatures resonate less at the same frequency.
                float severity = props.severity * hearing / Mathf.Max(1f, pawn.BodySize);
                HealthUtility.AdjustSeverity(pawn, props.hediff, severity);
            }

            if (pawn.Dead || !pawn.Spawned) return;

            if (!pawn.Awake())
                RestUtility.WakeUp(pawn);

            if (props.panicChance <= 0f || pawn.Downed || pawn.InMentalState) return;
            if (pawn.mindState?.mentalStateHandler == null) return;

            float chance = props.panicChance * hearing * (1f - pawn.GetStatValue(FFF_DefOf.FFF_FearResistance));
            if (!Rand.Chance(Mathf.Clamp01(chance))) return;
            if (!state.Worker.StateCanOccur(pawn)) return;

            pawn.mindState.mentalStateHandler.TryStartMentalState(state, forceWake: true,
                otherPawn: source as Pawn, causedByDamage: true);
        }
    }
}
