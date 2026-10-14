using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace DMS
{
    /// <summary>
    /// 在地下設施生成的最後一步放下守墓人巡邏隊（見 <see cref="ModExtension_GravekeeperPatrol"/>）。
    /// 這時閘門、獎勵房與入口都定好了，才能規劃路線：
    /// <list type="bullet">
    /// <item>崗位：每扇獎勵房密封門外、走廊側最近的空格。</item>
    /// <item>路線：從離第一個崗位最近的那段走廊開始，沿走廊樹深度優先走過每段走廊的兩端；
    /// 其他崗位插在各自最近的走廊上。路線首尾相接，相鄰兩點之間的路交給尋路。</item>
    /// <item>電梯廳（含牆線）與管制區（獎勵房、指揮室的內部）記在 lord job 上。</item>
    /// </list>
    ///
    /// Places the gravekeeper patrols as the underground facility's last spawning step (see
    /// <see cref="ModExtension_GravekeeperPatrol"/>). Only now are the gates, treasuries and entrance settled, so the
    /// route can be planned:
    /// <list type="bullet">
    /// <item>Posts: the nearest free cell on the corridor side of each treasury sealed door.</item>
    /// <item>Route: starting at the corridor nearest the first post, a depth-first walk of the corridor tree visiting both
    /// ends of every corridor; other posts are slotted in on their nearest corridor. The route loops, and pathfinding
    /// covers the way between consecutive points.</item>
    /// <item>The lift lobby (wall line included) and the restricted rooms (treasury and command room interiors) go on the
    /// lord job.</item>
    /// </list>
    /// </summary>
    public static class GravekeeperPatrolPlacement
    {
        /// <summary>相鄰路線點至少相隔這麼遠，太近的合併。Consecutive route points closer than this are merged.</summary>
        private const float MinWaypointSpacing = 5f;

        public static void Spawn(LayoutStructureSketch sketch, VaultLayoutPlan plan, Map map, Faction faction, float? threatPoints,
            ModExtension_GravekeeperPatrol ext, List<Thing> gateDoors, List<Thing> tombDoors)
        {
            if (ext == null || ext.squad.NullOrEmpty() || plan == null || plan.corridors.Count == 0) return;
            if (threatPoints.HasValue && threatPoints.Value < ext.minThreatPoints) return;
            if (!Rand.Chance(ext.chance)) return;

            faction ??= VaultRoomUtility.DefenderFaction;
            if (faction == null) return;

            List<LayoutRoom> rooms = sketch.structureLayout?.Rooms ?? new List<LayoutRoom>();
            List<CellRect> lobby = rooms.Where(VaultRoomUtility.IsEntrance).SelectMany(r => r.rects).ToList();
            List<CellRect> tombRooms = rooms.Where(IsTomb).SelectMany(r => r.rects).ToList();
            List<CellRect> restricted = rooms
                .Where(r => IsTomb(r) || VaultRoomUtility.HasWorker(r, typeof(RoomContents_VaultCommand)))
                .SelectMany(r => r.rects)
                .Select(r => r.ContractedBy(1))
                .Where(r => r.Area > 0)
                .ToList();

            List<IntVec3> posts = TombPosts(map, tombDoors, tombRooms, lobby);
            List<IntVec3> route = BuildRoute(map, plan, posts, lobby, out List<int> postIndices);
            if (route.Count < 2)
            {
                Log.Warning("[DMS] Gravekeeper patrol: no usable corridor route in this facility; no patrol placed.");
                return;
            }

            int squads = Mathf.Max(1, ext.squadCount.RandomInRange);
            for (int s = 0; s < squads; s++)
            {
                int start = route.Count * s / squads;
                LordJob_GravekeeperPatrol job = new LordJob_GravekeeperPatrol(ext, start)
                {
                    route = new List<IntVec3>(route),
                    tombPosts = new List<int>(postIndices),
                    lobby = new List<CellRect>(lobby),
                    restricted = new List<CellRect>(restricted),
                    gates = gateDoors != null ? new List<Thing>(gateDoors) : new List<Thing>(),
                    tombDoors = tombDoors != null ? new List<Thing>(tombDoors) : new List<Thing>(),
                };

                List<Pawn> pawns = SpawnSquad(ext, faction, map, route[start], job);
                if (pawns.Count == 0) continue;
                LordMaker.MakeNewLord(faction, job, map, pawns);
            }
        }

        private static bool IsTomb(LayoutRoom room)
        {
            return room?.defs != null && room.defs.Any(d => VaultTreasuryUtility.IsTreasuryWorker(d.roomContentsWorkerType));
        }

        /// <summary>每扇獎勵房門外最近的走廊空格。The nearest free corridor cell outside each treasury door.</summary>
        private static List<IntVec3> TombPosts(Map map, List<Thing> tombDoors, List<CellRect> tombRooms, List<CellRect> lobby)
        {
            List<IntVec3> posts = new List<IntVec3>();
            if (tombDoors == null) return posts;

            foreach (Thing door in tombDoors)
            {
                if (door == null || !door.Spawned) continue;

                CellRect occupied = door.OccupiedRect();
                IntVec3 best = IntVec3.Invalid;
                float bestDist = float.MaxValue;
                foreach (IntVec3 cell in occupied.ExpandedBy(2))
                {
                    if (occupied.Contains(cell) || !cell.InBounds(map) || !cell.Standable(map)) continue;
                    if (tombRooms.Any(r => r.Contains(cell)) || lobby.Any(r => r.Contains(cell))) continue;

                    float dist = cell.DistanceTo(occupied.CenterCell);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = cell;
                    }
                }
                if (best.IsValid && !posts.Any(p => p.InHorDistOf(best, MinWaypointSpacing)))
                {
                    posts.Add(best);
                }
            }
            return posts;
        }

        private static List<IntVec3> BuildRoute(Map map, VaultLayoutPlan plan, List<IntVec3> posts, List<CellRect> lobby, out List<int> postIndices)
        {
            int count = plan.corridors.Count;
            List<int>[] links = new List<int>[count];
            for (int i = 0; i < count; i++) links[i] = new List<int>();
            for (int i = 0; i < count; i++)
            {
                int parent = plan.corridors[i].parent;
                if (parent < 0 || parent >= count) continue;
                links[i].Add(parent);
                links[parent].Add(i);
            }

            // 走廊樹深度優先的順序，從崗位所在的那段走廊開始。Depth-first corridor order, starting from the post's corridor.
            int root = posts.Count > 0 ? NearestCorridor(plan, posts[0]) : 0;
            List<int> order = new List<int>();
            bool[] visited = new bool[count];
            void Visit(int c)
            {
                visited[c] = true;
                order.Add(c);
                CellRect here = plan.corridors[c].rect;
                foreach (int next in links[c].OrderBy(k => plan.corridors[k].rect.CenterCell.DistanceToSquared(here.CenterCell)))
                {
                    if (!visited[next]) Visit(next);
                }
            }
            Visit(root);
            for (int i = 0; i < count; i++)
            {
                if (!visited[i]) Visit(i);
            }

            List<IntVec3> route = new List<IntVec3>();
            List<bool> isPost = new List<bool>();
            void Add(IntVec3 cell, bool post)
            {
                if (!cell.IsValid) return;
                if (route.Count > 0 && route[route.Count - 1].InHorDistOf(cell, MinWaypointSpacing))
                {
                    // 崗位比普通點重要，重疊時保留崗位。A post outranks a plain point when they coincide.
                    if (post)
                    {
                        route[route.Count - 1] = cell;
                        isPost[isPost.Count - 1] = true;
                    }
                    return;
                }
                route.Add(cell);
                isPost.Add(post);
            }

            HashSet<int> placedPosts = new HashSet<int>();
            IntVec3 last = plan.corridors[root].rect.CenterCell;
            if (posts.Count > 0)
            {
                // 從墓前出發。Start in front of the tomb.
                Add(posts[0], true);
                placedPosts.Add(0);
                last = posts[0];
            }

            foreach (int c in order)
            {
                VaultLayoutPlan.Corridor corridor = plan.corridors[c];
                CellRect inner = corridor.rect.ContractedBy(1);
                if (inner.Area <= 0) continue;

                Ends(corridor, inner, out IntVec3 near, out IntVec3 far);
                if (far.DistanceToSquared(last) < near.DistanceToSquared(last))
                {
                    (near, far) = (far, near);
                }

                IntVec3 a = Resolve(map, near, inner, lobby);
                IntVec3 b = Resolve(map, far, inner, lobby);
                Add(a, false);
                // 其他崗位掛在離它最近的走廊上。Other posts hang off the corridor they are nearest to.
                for (int i = 1; i < posts.Count; i++)
                {
                    if (placedPosts.Contains(i) || NearestCorridor(plan, posts[i]) != c) continue;
                    Add(posts[i], true);
                    placedPosts.Add(i);
                }
                Add(b, false);

                if (b.IsValid) last = b;
                else if (a.IsValid) last = a;
            }
            for (int i = 0; i < posts.Count; i++)
            {
                if (!placedPosts.Contains(i)) Add(posts[i], true);
            }

            // 路線首尾相接：最後一點跟起點疊在一起就丟掉。The route loops: drop a last point sitting on the start.
            if (route.Count > 2 && route[route.Count - 1].InHorDistOf(route[0], MinWaypointSpacing) && !isPost[isPost.Count - 1])
            {
                route.RemoveAt(route.Count - 1);
                isPost.RemoveAt(isPost.Count - 1);
            }

            postIndices = new List<int>();
            for (int i = 0; i < isPost.Count; i++)
            {
                if (isPost[i]) postIndices.Add(i);
            }
            return route;
        }

        /// <summary>走廊內部沿長軸的兩端（往內收一格）。Both ends of a corridor's interior along its long axis, one cell in.</summary>
        private static void Ends(VaultLayoutPlan.Corridor corridor, CellRect inner, out IntVec3 a, out IntVec3 b)
        {
            IntVec3 centre = inner.CenterCell;
            if (corridor.horizontal)
            {
                int min = Mathf.Min(inner.minX + 1, inner.maxX);
                int max = Mathf.Max(inner.maxX - 1, inner.minX);
                a = new IntVec3(min, 0, centre.z);
                b = new IntVec3(max, 0, centre.z);
            }
            else
            {
                int min = Mathf.Min(inner.minZ + 1, inner.maxZ);
                int max = Mathf.Max(inner.maxZ - 1, inner.minZ);
                a = new IntVec3(centre.x, 0, min);
                b = new IntVec3(centre.x, 0, max);
            }
        }

        /// <summary>目標格附近、走廊內部、站得住的格子（嵌入結構、雜物可能佔住目標格）。A standable cell in the corridor near the target (embeds or clutter may hold the target itself).</summary>
        private static IntVec3 Resolve(Map map, IntVec3 cell, CellRect inner, List<CellRect> lobby)
        {
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, 4f, true))
            {
                if (!inner.Contains(c) || !c.InBounds(map) || !c.Standable(map)) continue;
                if (lobby.Any(r => r.Contains(c))) continue;
                return c;
            }
            return IntVec3.Invalid;
        }

        private static int NearestCorridor(VaultLayoutPlan plan, IntVec3 cell)
        {
            int best = 0;
            int bestDist = int.MaxValue;
            for (int i = 0; i < plan.corridors.Count; i++)
            {
                CellRect rect = plan.corridors[i].rect;
                int dist = rect.ClosestCellTo(cell).DistanceToSquared(cell);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
            return best;
        }

        private static List<Pawn> SpawnSquad(ModExtension_GravekeeperPatrol ext, Faction faction, Map map, IntVec3 near, LordJob_GravekeeperPatrol job)
        {
            List<Pawn> pawns = new List<Pawn>();
            foreach (GravekeeperSquadMember member in ext.squad)
            {
                if (member?.kind == null) continue;
                for (int i = 0; i < member.count; i++)
                {
                    if (!CellFinder.TryFindRandomCellNear(near, map, 6,
                        c => c.Standable(map) && !job.InLobby(c) && !job.InRestricted(c) && c.GetFirstPawn(map) == null,
                        out IntVec3 cell))
                    {
                        continue;
                    }

                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(member.kind, faction, PawnGenerationContext.NonPlayer, map.Tile));
                    if (pawn == null) continue;
                    GenSpawn.Spawn(pawn, cell, map, Rot4.Random);
                    pawns.Add(pawn);
                }
            }
            return pawns;
        }
    }
}
