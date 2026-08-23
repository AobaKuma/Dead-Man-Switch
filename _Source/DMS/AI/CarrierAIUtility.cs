using System.Collections.Generic;
using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DMS
{
    /// <summary>
    /// 母艦型機兵（集群織鳥）戰術 AI 的共用計算。
    ///
    /// 這裡所有的距離都是從「單位當下真正的有效射程」推出來的，
    /// 不寫死格數，所以原版（掛載 HMG 射程 27.9）與 CE（同一把槍 75）
    /// 都能自動得到合理的滯空距離，不需要兩套數值。
    /// </summary>
    public static class CarrierAIUtility
    {
        private static readonly List<IntVec3> tmpHostileCells = new List<IntVec3>();

        private static readonly List<CellScore> tmpCandidates = new List<CellScore>();

        private struct CellScore
        {
            public IntVec3 cell;

            public float score;
        }

        /// <summary>
        /// 單位所有遠程武裝裡最遠的有效射程：手持主武器與所有子砲塔一起算。
        /// 一把能打的都沒有時回傳 fallback。
        /// </summary>
        public static float EffectiveReach(Pawn pawn, float fallback = 25f)
        {
            float best = 0f;

            ThingWithComps primary = pawn.equipment?.Primary;
            if (primary != null)
            {
                CompEquippable compEquippable = primary.TryGetComp<CompEquippable>();
                if (compEquippable != null)
                {
                    List<Verb> allVerbs = compEquippable.AllVerbs;
                    for (int i = 0; i < allVerbs.Count; i++)
                    {
                        best = Mathf.Max(best, RangedRangeOf(allVerbs[i]));
                    }
                }
            }

            CompMultipleTurretGun turretComp = pawn.TryGetComp<CompMultipleTurretGun>();
            if (turretComp?.turrets != null)
            {
                for (int j = 0; j < turretComp.turrets.Count; j++)
                {
                    SubTurret subTurret = turretComp.turrets[j];
                    if (subTurret == null || !subTurret.Initialized || subTurret.turret == null)
                    {
                        continue;
                    }
                    best = Mathf.Max(best, RangedRangeOf(subTurret.CurrentEffectiveVerb));
                }
            }

            return (best > 0f) ? best : fallback;
        }

        private static float RangedRangeOf(Verb verb)
        {
            if (verb?.verbProps == null || verb.verbProps.IsMeleeAttack)
            {
                return 0f;
            }
            return verb.EffectiveRange;
        }

        /// <summary>
        /// 掃描範圍內敵對「單位」最遠的遠程射程，用來決定要退到多遠才咬不到。
        /// 刻意不算砲塔與迫擊砲：那些射程會把母艦推到地圖外，反而變成呆站。
        /// </summary>
        public static float HostileMaxRange(Pawn pawn, float scanRadius)
        {
            Map map = pawn.Map;
            if (map == null)
            {
                return 0f;
            }

            float best = 0f;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (other == pawn || other.Dead || other.Downed || !other.Spawned)
                {
                    continue;
                }
                if (!other.HostileTo(pawn))
                {
                    continue;
                }
                if (!other.Position.InHorDistOf(pawn.Position, scanRadius))
                {
                    continue;
                }
                Verb verb = other.TryGetAttackVerb(pawn, !other.IsColonist);
                best = Mathf.Max(best, RangedRangeOf(verb));
            }
            return best;
        }

        /// <summary>
        /// 找一個「離目標距離落在 [bandMin, bandMax] 之內」的滯空點。
        /// 評分同時考慮：對目標有無視線、離期望距離多近、離所有威脅多遠、要走多久。
        /// </summary>
        public static bool TryFindStandoffCell(Pawn pawn, Thing target, float bandMin, float bandMax,
            float desired, float searchRadius, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            Map map = pawn.Map;
            if (map == null || target == null || !target.Spawned)
            {
                return false;
            }

            IntVec3 root = pawn.Position;
            IntVec3 targetCell = target.Position;
            float minSq = bandMin * bandMin;
            float maxSq = bandMax * bandMax;

            CollectHostileCells(pawn, searchRadius + bandMax);

            tmpCandidates.Clear();
            int cellCount = GenRadial.NumCellsInRadius(searchRadius);
            for (int i = 0; i < cellCount; i++)
            {
                IntVec3 c = root + GenRadial.RadialPattern[i];
                if (!c.InBounds(map))
                {
                    continue;
                }
                float distSq = (c - targetCell).LengthHorizontalSquared;
                if (distSq < minSq || distSq > maxSq)
                {
                    continue;
                }
                if (!c.Standable(map) || !c.WalkableBy(map, pawn) || c.IsForbidden(pawn))
                {
                    continue;
                }
                if (c.ContainsStaticFire(map) || PawnUtility.KnownDangerAt(c, map, pawn))
                {
                    continue;
                }
                if (!map.pawnDestinationReservationManager.CanReserve(c, pawn))
                {
                    continue;
                }

                float dist = Mathf.Sqrt(distSq);
                float score = 0f;
                // 停在打得到的位置最重要：主武器要有視線，副砲塔才有活幹。
                if (GenSight.LineOfSight(c, targetCell, map, skipFirstCell: true))
                {
                    score += 40f;
                }
                // 越接近期望距離越好。
                score -= Mathf.Abs(dist - desired) * 3f;
                // 離所有已知威脅越遠越好（不只當前目標）。
                score += Mathf.Min(NearestHostileDistance(c), bandMax) * 2f;
                // 能少走一步是一步。
                score -= (c - root).LengthHorizontal * 0.6f;

                tmpCandidates.Add(new CellScore { cell = c, score = score });
            }

            tmpHostileCells.Clear();
            if (tmpCandidates.Count == 0)
            {
                return false;
            }

            tmpCandidates.Sort((CellScore a, CellScore b) => b.score.CompareTo(a.score));

            // 可通行判定最貴，只對分數最高的一小撮做。
            int checkLimit = Mathf.Min(tmpCandidates.Count, 24);
            for (int k = 0; k < checkLimit; k++)
            {
                IntVec3 candidate = tmpCandidates[k].cell;
                if (candidate == root || pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly))
                {
                    result = candidate;
                    tmpCandidates.Clear();
                    return true;
                }
            }

            tmpCandidates.Clear();
            return false;
        }

        private static void CollectHostileCells(Pawn pawn, float scanRadius)
        {
            tmpHostileCells.Clear();
            Map map = pawn.Map;
            if (map == null)
            {
                return;
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (other == pawn || other.Dead || other.Downed || !other.Spawned)
                {
                    continue;
                }
                if (!other.HostileTo(pawn))
                {
                    continue;
                }
                if (!other.Position.InHorDistOf(pawn.Position, scanRadius))
                {
                    continue;
                }
                tmpHostileCells.Add(other.Position);
            }
        }

        private static float NearestHostileDistance(IntVec3 cell)
        {
            if (tmpHostileCells.Count == 0)
            {
                return 0f;
            }
            float best = float.MaxValue;
            for (int i = 0; i < tmpHostileCells.Count; i++)
            {
                float d = (tmpHostileCells[i] - cell).LengthHorizontalSquared;
                if (d < best)
                {
                    best = d;
                }
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>
        /// 找出範圍內最近、且看得到的敵對單位，投放判定用。
        /// </summary>
        public static Thing FindNearbyHostile(Pawn pawn, float radius)
        {
            return (Thing)AttackTargetFinder.BestAttackTarget(pawn,
                TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable | TargetScanFlags.NeedLOSToPawns,
                null, 0f, radius, IntVec3.Invalid, float.MaxValue, canBashDoors: false,
                canTakeTargetsCloserThanEffectiveMinRange: true);
        }
    }
}
