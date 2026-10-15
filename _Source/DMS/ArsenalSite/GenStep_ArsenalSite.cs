using System.Collections.Generic;
using System.Linq;
using Fortified;
using Fortified.Structures;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace DMS
{
    /// <summary>
    /// 軍火庫舊址的地圖生成，依序：
    /// <list type="number">
    /// <item>正門方向：挑一側通往地圖邊緣最暢通（岩石、水最少）的方向，主幹道從這裡出去接圍欄大門。</item>
    /// <item>核心：中央放 Boss 停機坪（<see cref="bossPadDef"/>），四周 1~3 座洞庫（<see cref="bunkerDef"/>，主結構，至少一座），
    /// 入口都朝向停機坪；第一座在正門對面，其餘放兩翼。核心落點在地圖中心附近挑岩石、水最少的位置。</item>
    /// <item>監視塔：<see cref="towerCount"/> 座帶 <see cref="towerTag"/> 的結構，沿一圈環平均分角度放在核心外圍。</item>
    /// <item>規模：洞庫與監視塔的數量依站點威脅點數（<see cref="bunkersByPoints"/>、<see cref="towersByPoints"/>），
    /// 點數低時結構較少。</item>
    /// <item>道路：停機坪外一圈方形環路，四條直線輻射道（三條到洞庫門口、一條穿過大門到地圖邊），
    /// 每座監視塔再以 L 形支線接到最近的既有道路。全部是軸向直線，寬度 <see cref="roadWidth"/>。</item>
    /// <item>圍欄：離地圖邊 <see cref="fenceInset"/> 格一整圈，鋼牆立柱與鐵絲網交錯，主幹道經過的地方留大門；
    /// 遇到山體、水面就斷開（地形本身就是屏障）。</item>
    /// <item>守墓人：依 SitePartDef 上的 <see cref="ModExtension_ArsenalSite"/> 放一隊 1~5 架，在停機坪集結，
    /// 由 <see cref="LordJob_GravekeeperPatrol"/> 沿著洞庫門口、監視塔與環路巡視（洞庫內部是管制區）。
    /// Boss 駭蜥不在地圖上：玩家要用停機坪上的誘餌雷達召喚，牠死了洞庫的 Boss 擊殺中繼器才放行。</item>
    /// </list>
    /// 8 座廢墟衛星結構（DMS_RuinSat_*）另由 <see cref="DMS_GenStep_ScatterStructures"/> 在 order 1400 隨機散布。
    ///
    /// Map generation for the old arsenal site, in order:
    /// <list type="number">
    /// <item>Front: the side with the clearest run to the map edge (least rock and water); the trunk road leaves this
    /// way to the fence gate.</item>
    /// <item>Core: the boss pad (<see cref="bossPadDef"/>) in the middle and 1~3 bunkers (<see cref="bunkerDef"/>, the
    /// main structure, always at least one) round it, entrances facing the pad: the first opposite the front, the rest on
    /// the flanks. The core goes near the map centre wherever rock and water are scarcest.</item>
    /// <item>Watchtowers: <see cref="towerCount"/> structures tagged <see cref="towerTag"/>, spread evenly round a ring
    /// outside the core.</item>
    /// <item>Scale: bunker and watchtower counts follow the site's threat points (<see cref="bunkersByPoints"/>,
    /// <see cref="towersByPoints"/>), so low-point sites get fewer structures.</item>
    /// <item>Roads: a square ring round the pad, four straight spokes (three to the bunker entrances, one through the gate
    /// to the map edge), and an L-shaped branch from each watchtower to the nearest road already laid. All axis-aligned,
    /// <see cref="roadWidth"/> wide.</item>
    /// <item>Fence: a full loop <see cref="fenceInset"/> cells in from the map edge, steel posts alternating with barbed
    /// wire, with a gate where the trunk road passes; it breaks off at rock and water, which are barriers already.</item>
    /// <item>Gravekeepers: one squad of 1~5 per the SitePartDef's <see cref="ModExtension_ArsenalSite"/>, mustering on
    /// the pad and walking the bunker entrances, the watchtowers and the ring under
    /// <see cref="LordJob_GravekeeperPatrol"/> (the bunker interiors are restricted). The tarbosaurus boss is not on the
    /// map: the player calls it in with the decoy radar on the pad, and the bunkers' boss-kill relays release once it
    /// dies.</item>
    /// </list>
    /// Eight ruin satellites (DMS_RuinSat_*) are scattered separately by <see cref="DMS_GenStep_ScatterStructures"/> at
    /// order 1400.
    /// </summary>
    public class GenStep_ArsenalSite : GenStep
    {
        // ── 核心 / Core ───────────────────────────────────────────────────────

        public FFF_StructureDef bossPadDef;
        public FFF_StructureDef bunkerDef;

        /// <summary>
        /// 洞庫數範圍，至少 1（主結構）、最多 3（第四側留給正門）。有 <see cref="bunkersByPoints"/> 時依站點威脅點數取值，否則在範圍內隨機。
        /// Bunker count range: at least 1 (the main structure), at most 3 (the fourth side is the front). Driven by the
        /// site's threat points through <see cref="bunkersByPoints"/> when set, random within the range otherwise.
        /// </summary>
        public IntRange bunkerCount = new IntRange(3, 3);

        /// <summary>威脅點數 → 洞庫數（小數部分隨機進位）。Threat points → bunkers (the fraction rounds randomly).</summary>
        public SimpleCurve bunkersByPoints;

        /// <summary>停機坪邊緣到洞庫入口之間的空地寬度。Open ground between the pad's edge and a bunker entrance.</summary>
        public int bunkerGap = 14;

        /// <summary>核心落點離地圖中心最多偏多遠。How far from the map centre the core may move.</summary>
        public int coreSearchRadius = 24;

        // ── 監視塔 / Watchtowers ──────────────────────────────────────────────

        public string towerTag;

        /// <summary>監視塔數範圍；有 <see cref="towersByPoints"/> 時依威脅點數取值，否則在範圍內隨機。Watchtower count range; driven by <see cref="towersByPoints"/> when set.</summary>
        public IntRange towerCount = new IntRange(2, 6);

        /// <summary>威脅點數 → 監視塔數（小數部分隨機進位）。Threat points → watchtowers (the fraction rounds randomly).</summary>
        public SimpleCurve towersByPoints;

        /// <summary>監視塔環半徑 = 地圖短邊 × 這個比例（至少 <see cref="towerMinRadius"/>，且在核心之外）。Ring radius as a share of the map's short side.</summary>
        public float towerRadiusPct = 0.32f;
        public float towerMinRadius = 55f;

        /// <summary>監視塔與其他結構之間至少留幾格。Minimum gap between a watchtower and any other structure.</summary>
        public int towerSpacing = 8;

        // ── 道路 / Roads ─────────────────────────────────────────────────────

        public TerrainDef roadTerrain;

        /// <summary>道路寬度，用奇數（以中線為準向兩側展開）。Road width; use an odd number (it spreads both ways from the centre line).</summary>
        public int roadWidth = 3;

        // ── 圍欄 / Fence ─────────────────────────────────────────────────────

        public ThingDef fencePostDef;
        public ThingDef fencePostStuff;
        public ThingDef fenceWireDef;

        /// <summary>圍欄離地圖邊幾格。要小於 FFF 的邊緣安全帶，散布的結構才會全在圍欄內。Cells between the fence and the map edge; keep it inside FFF's edge band so scattered structures land inside.</summary>
        public int fenceInset = 6;

        /// <summary>大門開口寬度。Width of the gate opening.</summary>
        public int gateWidth = 7;

        public override int SeedPart => 1730412859;

        private static readonly Rot4[] Sides = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };

        /// <summary>一次生成的所有中間結果；genStep 是全遊戲共用的實例，所以狀態不能放在欄位上。Everything one run works out; the genStep instance is shared game-wide, so no state lives on fields.</summary>
        private class Plan
        {
            public Map map;
            public Faction faction;
            public float points;
            public Rot4 front;
            public IntVec3 center;
            public CellRect pad;
            public int ringRadius;
            public readonly List<Bunker> bunkers = new List<Bunker>();
            public readonly List<CellRect> towers = new List<CellRect>();
            public readonly List<IntVec3> towerDoors = new List<IntVec3>();
            public readonly List<CellRect> blocked = new List<CellRect>();
            public readonly HashSet<IntVec3> road = new HashSet<IntVec3>();
        }

        private class Bunker
        {
            public Rot4 side;
            public CellRect rect;
            /// <summary>入口外的巡邏崗位。The patrol post outside the entrance.</summary>
            public IntVec3 post;
        }

        public override void Generate(Map map, GenStepParams parms)
        {
            if (map == null) return;
            if (bossPadDef == null || bunkerDef == null)
            {
                Log.Error("[DMS] GenStep_ArsenalSite: bossPadDef and bunkerDef are required.");
                return;
            }

            Plan plan = new Plan
            {
                map = map,
                faction = map.ParentFaction ?? parms.sitePart?.site?.Faction,
                points = parms.sitePart?.parms.threatPoints ?? StorytellerUtility.DefaultSiteThreatPointsNow(),
            };
            plan.front = PickFront(map);

            // 主結構至少一座。At least one main structure.
            int bunkers = ScaledCount(bunkersByPoints, bunkerCount, plan.points, 1, 3);
            List<Rot4> bunkerSides = BunkerSides(plan.front, bunkers);
            plan.center = PickCoreCenter(map, bunkerSides);

            PlaceCore(plan, bunkerSides);
            PlaceTowers(plan, ScaledCount(towersByPoints, towerCount, plan.points, 0, int.MaxValue));
            PlanRoads(plan);
            LayRoads(plan);
            BuildFence(plan);
            SpawnGuards(plan, parms);
        }

        // ── 規模 / Scale ──────────────────────────────────────────────────────

        /// <summary>
        /// 有曲線就依威脅點數取值（小數部分隨機進位），沒有就在範圍內隨機；結果夾在範圍與 [floor, ceiling] 之間。
        /// From the curve by threat points when there is one (the fraction rounds randomly), random within the range
        /// otherwise; clamped to the range and to [floor, ceiling].
        /// </summary>
        private static int ScaledCount(SimpleCurve curve, IntRange range, float points, int floor, int ceiling)
        {
            int count = curve != null ? GenMath.RoundRandom(curve.Evaluate(points)) : range.RandomInRange;
            return Mathf.Clamp(Mathf.Clamp(count, range.min, range.max), floor, ceiling);
        }

        /// <summary>
        /// 洞庫的方位：第一座在正門對面（主幹道穿過停機坪直達它門口），其餘依序放左右兩翼（順序隨機）。
        /// Where the bunkers go: the first opposite the front (the trunk road runs across the pad straight to its door),
        /// the rest on the two flanks in random order.
        /// </summary>
        private static List<Rot4> BunkerSides(Rot4 front, int count)
        {
            List<Rot4> sides = new List<Rot4> { front.Opposite };
            sides.AddRange(new[] { new Rot4((front.AsInt + 1) % 4), new Rot4((front.AsInt + 3) % 4) }.InRandomOrder());
            return sides.Take(count).ToList();
        }

        // ── 核心 / Core ───────────────────────────────────────────────────────

        /// <summary>從地圖中心到各邊，挑岩石與水最少的一側當正門。The side with the least rock and water between the centre and the edge.</summary>
        private Rot4 PickFront(Map map)
        {
            IntVec3 c = map.Center;
            Rot4 best = Rot4.Random;
            float bestScore = float.MaxValue;
            foreach (Rot4 side in Sides)
            {
                float score = Rand.Value; // 平手時隨機。Random tie-break.
                foreach (IntVec3 cell in Corridor(map, c, side))
                {
                    if (IsBadGround(cell, map)) score += 1f;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = side;
                }
            }
            return best;
        }

        /// <summary>從 from 沿 side 直到地圖邊、寬 roadWidth 的走廊。The roadWidth-wide strip from <paramref name="from"/> to the map edge along <paramref name="side"/>.</summary>
        private IEnumerable<IntVec3> Corridor(Map map, IntVec3 from, Rot4 side)
        {
            IntVec3 end = from;
            while ((end + side.FacingCell).InBounds(map)) end += side.FacingCell;
            return Segment(from, end).Where(c => c.InBounds(map));
        }

        /// <summary>在地圖中心附近找核心足跡裡壞地面最少的落點。Near the map centre, the core position whose footprint has the least bad ground.</summary>
        private IntVec3 PickCoreCenter(Map map, List<Rot4> bunkerSides)
        {
            CellRect usable = FFF_StructureUtility.UsableRect(map).ContractedBy(2);
            IntVec3 best = map.Center;
            float bestScore = float.MaxValue;
            for (int dx = -coreSearchRadius; dx <= coreSearchRadius; dx += 4)
            {
                for (int dz = -coreSearchRadius; dz <= coreSearchRadius; dz += 4)
                {
                    IntVec3 c = map.Center + new IntVec3(dx, 0, dz);
                    List<CellRect> rects = CoreRects(c, bunkerSides, out _);
                    if (!rects.All(r => Inside(usable, r))) continue;

                    float score = rects.Sum(r => r.Cells.Count(cell => IsBadGround(cell, map)));
                    score += (dx * dx + dz * dz) * 0.01f; // 同分時偏向中心。Lean towards the centre.
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
            }
            return best;
        }

        /// <summary>以 center 為核心中心時停機坪與各洞庫的足跡（第一個是停機坪）。The pad and bunker footprints with the core on <paramref name="center"/>; the pad comes first.</summary>
        private List<CellRect> CoreRects(IntVec3 center, List<Rot4> bunkerSides, out List<IntVec3> bunkerCenters)
        {
            CellRect pad = FFF_StructureUtility.FootprintAt(bossPadDef, center, Rot4.North);
            List<CellRect> rects = new List<CellRect> { pad };
            bunkerCenters = new List<IntVec3>();
            foreach (Rot4 side in bunkerSides)
            {
                // 洞庫以 rot = side 生成時，作者的南側（入口）會朝向核心。先以核心中心試算足跡，量出入口那一側到中心的距離，
                // 再沿 side 推到停機坪外 bunkerGap 格。
                // Spawned with rot = side, the bunker's authored south face (the entrance) faces the core. Measure the
                // entrance edge with a trial footprint on the core centre, then push it out along side to bunkerGap past the pad.
                int reach = FarEdge(pad, center, side) + bunkerGap + 1;
                CellRect probe = FFF_StructureUtility.FootprintAt(bunkerDef, center, side);
                IntVec3 bunkerCenter = center + side.FacingCell * (reach - NearEdge(probe, center, side));
                bunkerCenters.Add(bunkerCenter);
                rects.Add(FFF_StructureUtility.FootprintAt(bunkerDef, bunkerCenter, side));
            }
            return rects;
        }

        private void PlaceCore(Plan plan, List<Rot4> bunkerSides)
        {
            Map map = plan.map;
            List<CellRect> rects = CoreRects(plan.center, bunkerSides, out List<IntVec3> bunkerCenters);

            plan.pad = rects[0];
            ClearGround(map, plan.pad);
            FFF_StructureUtility.Generate(bossPadDef, plan.center, map, plan.faction, Rot4.North, reconnectPower: false);
            Block(plan, plan.pad);

            for (int i = 0; i < bunkerSides.Count; i++)
            {
                Rot4 side = bunkerSides[i];
                CellRect rect = rects[i + 1];
                ClearGround(map, rect);
                FFF_StructureUtility.Generate(bunkerDef, bunkerCenters[i], map, plan.faction, side, reconnectPower: false);
                Block(plan, rect);

                int entrance = NearEdge(rect, plan.center, side);
                plan.bunkers.Add(new Bunker { side = side, rect = rect, post = plan.center + side.FacingCell * (entrance - 3) });
            }

            plan.ringRadius = Sides.Max(s => FarEdge(plan.pad, plan.center, s)) + 1 + bunkerGap / 2;
        }

        // ── 監視塔 / Watchtowers ──────────────────────────────────────────────

        private void PlaceTowers(Plan plan, int count)
        {
            if (count <= 0) return;
            if (towerTag.NullOrEmpty()) return;
            List<FFF_StructureDef> pool = DefDatabase<FFF_StructureDef>.AllDefsListForReading
                .Where(d => d.tags != null && d.tags.Contains(towerTag)).ToList();
            if (pool.Count == 0)
            {
                Log.Warning($"[DMS] GenStep_ArsenalSite: no FFF_StructureDef carries tag '{towerTag}'; no watchtowers.");
                return;
            }

            Map map = plan.map;

            CellRect usable = FFF_StructureUtility.UsableRect(map).ContractedBy(2);
            CellRect core = Bounds(plan.blocked);
            float coreReach = Sides.Max(s => FarEdge(core, plan.center, s));
            float radius0 = Mathf.Max(Mathf.Min(map.Size.x, map.Size.z) * towerRadiusPct, towerMinRadius, coreReach + towerSpacing + 6f);
            CellRect frontRoad = FrontCorridorRect(plan).ExpandedBy(towerSpacing / 2 + 1);

            float slice = 360f / count;
            float angle0 = Rand.Range(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                FFF_StructureDef def = pool.RandomElement();
                bool placed = false;
                float radius = radius0;
                for (int shrink = 0; shrink < 6 && !placed; shrink++, radius *= 0.9f)
                {
                    for (int attempt = 0; attempt < 8 && !placed; attempt++)
                    {
                        float angle = (angle0 + slice * i + Rand.Range(-0.35f, 0.35f) * slice) * Mathf.Deg2Rad;
                        float r = radius * Rand.Range(0.9f, 1.1f);
                        IntVec3 at = plan.center + new IntVec3(Mathf.RoundToInt(Mathf.Cos(angle) * r), 0, Mathf.RoundToInt(Mathf.Sin(angle) * r));
                        Rot4 rot = DirectionOf(at - plan.center);
                        CellRect rect = FFF_StructureUtility.FootprintAt(def, at, rot);

                        if (!Inside(usable, rect) || rect.Overlaps(frontRoad)) continue;
                        if (plan.blocked.Any(b => b.ExpandedBy(towerSpacing).Overlaps(rect))) continue;
                        if (rect.Cells.Count(c => IsBadGround(c, map)) > rect.Area / 10) continue;

                        ClearGround(map, rect);
                        FFF_StructureUtility.Generate(def, at, map, plan.faction, rot, reconnectPower: false);
                        Block(plan, rect);
                        plan.towers.Add(rect);
                        // 監視塔以 rot = 從核心看過去的方向生成，朝核心那一側的外面接支線。
                        // Spawned with rot = the direction from the core, so the branch road joins on the side facing the core.
                        plan.towerDoors.Add(EdgeOutside(rect, rot.Opposite));
                        placed = true;
                    }
                }
                if (!placed && Prefs.DevMode)
                {
                    Log.Message($"[DMS] GenStep_ArsenalSite: no room for watchtower {def.defName} on a {map.Size.x}x{map.Size.z} map; skipped.");
                }
            }
        }

        // ── 道路 / Roads ─────────────────────────────────────────────────────

        private void PlanRoads(Plan plan)
        {
            IntVec3 c = plan.center;
            int r = plan.ringRadius;

            // 停機坪外的方形環路。The square ring round the pad.
            IntVec3 sw = c + new IntVec3(-r, 0, -r), se = c + new IntVec3(r, 0, -r);
            IntVec3 nw = c + new IntVec3(-r, 0, r), ne = c + new IntVec3(r, 0, r);
            AddRoad(plan, sw, se);
            AddRoad(plan, se, ne);
            AddRoad(plan, ne, nw);
            AddRoad(plan, nw, sw);

            // 輻射道：停機坪邊緣到洞庫入口，正門那條一路通到地圖邊。
            // Spokes: pad edge to each bunker entrance; the front one runs on to the map edge.
            foreach (Bunker bunker in plan.bunkers)
            {
                IntVec3 from = c + bunker.side.FacingCell * (FarEdge(plan.pad, c, bunker.side) + 1);
                IntVec3 to = c + bunker.side.FacingCell * (NearEdge(bunker.rect, c, bunker.side) - 1);
                AddRoad(plan, from, to);
            }
            foreach (IntVec3 cell in FrontCorridorRect(plan)) plan.road.Add(cell);

            // 監視塔支線：由近到遠，各自以 L 形接到最近的既有道路，所以遠的塔可以接在近的塔的支線上。
            // Watchtower branches, nearest first, each an L to the closest road so far, so far towers can join nearer branches.
            foreach (IntVec3 door in plan.towerDoors.OrderBy(d => d.DistanceToSquared(c)))
            {
                IntVec3 target = NearestRoadCell(plan, door);
                IntVec3 cornerA = new IntVec3(door.x, 0, target.z);
                IntVec3 cornerB = new IntVec3(target.x, 0, door.z);
                bool useA = BlockedCost(plan, door, cornerA, target) <= BlockedCost(plan, door, cornerB, target);
                IntVec3 corner = useA ? cornerA : cornerB;
                AddRoad(plan, door, corner);
                AddRoad(plan, corner, target);
            }
        }

        /// <summary>從停機坪邊緣沿正門方向到地圖邊的主幹道。The trunk road from the pad's edge out the front to the map edge.</summary>
        private CellRect FrontCorridorRect(Plan plan)
        {
            Map map = plan.map;
            IntVec3 from = plan.center + plan.front.FacingCell * (FarEdge(plan.pad, plan.center, plan.front) + 1);
            IntVec3 to = from;
            while ((to + plan.front.FacingCell).InBounds(map)) to += plan.front.FacingCell;
            return RoadRect(from, to).ClipInsideMap(map);
        }

        private static IntVec3 NearestRoadCell(Plan plan, IntVec3 from)
        {
            IntVec3 best = plan.center;
            int bestDist = int.MaxValue;
            foreach (IntVec3 cell in plan.road)
            {
                int dist = cell.DistanceToSquared(from);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = cell;
                }
            }
            return best;
        }

        private int BlockedCost(Plan plan, IntVec3 a, IntVec3 corner, IntVec3 b)
        {
            return RoadRect(a, corner).Cells.Concat(RoadRect(corner, b).Cells).Count(c => plan.blocked.Any(r => r.Contains(c)));
        }

        private void AddRoad(Plan plan, IntVec3 a, IntVec3 b)
        {
            foreach (IntVec3 cell in RoadRect(a, b)) plan.road.Add(cell);
        }

        /// <summary>a 到 b 的軸向線段，向四周展開半個路寬（端點也展開，L 形轉角才會補滿）。The axis-aligned segment a→b padded by half the road width all round, so L corners fill in.</summary>
        private CellRect RoadRect(IntVec3 a, IntVec3 b)
        {
            return CellRect.FromLimits(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z))
                .ExpandedBy(Mathf.Max(0, roadWidth / 2));
        }

        private IEnumerable<IntVec3> Segment(IntVec3 a, IntVec3 b) => RoadRect(a, b).Cells;

        /// <summary>
        /// 把規劃好的道路鋪上去。結構足跡內、水面、岩石或其他建築底下的格子跳過；植物與碎石清掉。
        /// Lays the planned road. Cells inside a structure, on water, under rock or another building are skipped; plants and
        /// chunks are cleared.
        /// </summary>
        private void LayRoads(Plan plan)
        {
            if (roadTerrain == null) return;
            Map map = plan.map;
            foreach (IntVec3 c in plan.road)
            {
                if (!c.InBounds(map) || plan.blocked.Any(r => r.Contains(c))) continue;
                TerrainDef terrain = c.GetTerrain(map);
                if (terrain.IsWater || terrain.bridge || c.GetEdifice(map) != null) continue;

                ClearCell(map, c);
                map.terrainGrid.SetTerrain(c, roadTerrain);
            }
        }

        // ── 圍欄 / Fence ─────────────────────────────────────────────────────

        private void BuildFence(Plan plan)
        {
            if (fencePostDef == null) return;
            Map map = plan.map;
            CellRect fence = CellRect.WholeMap(map).ContractedBy(fenceInset);
            if (fence.Width < 4 || fence.Height < 4) return;

            // 主幹道穿過圍欄的地方留門。The gate where the trunk road crosses the fence.
            CellRect gate = FrontCorridorRect(plan).ExpandedBy(Mathf.Max(0, (gateWidth - roadWidth) / 2));

            List<List<IntVec3>> sides = new List<List<IntVec3>>
            {
                Line(fence.minX, fence.minZ, fence.maxX, fence.minZ),
                Line(fence.minX, fence.maxZ, fence.maxX, fence.maxZ),
                Line(fence.minX, fence.minZ + 1, fence.minX, fence.maxZ - 1),
                Line(fence.maxX, fence.minZ + 1, fence.maxX, fence.maxZ - 1),
            };

            foreach (List<IntVec3> line in sides)
            {
                // 可以立圍欄的連續區段；山體、水面、結構、大門都會把圍欄切開。
                // Runs of fenceable cells; rock, water, structures and the gate all cut the fence.
                int i = 0;
                while (i < line.Count)
                {
                    if (!FenceCellOk(plan, line[i], gate))
                    {
                        i++;
                        continue;
                    }
                    int j = i;
                    while (j + 1 < line.Count && FenceCellOk(plan, line[j + 1], gate)) j++;
                    PlaceFenceRun(map, line, i, j);
                    i = j + 1;
                }
            }
        }

        private static List<IntVec3> Line(int x0, int z0, int x1, int z1)
        {
            List<IntVec3> cells = new List<IntVec3>();
            if (z0 == z1)
            {
                for (int x = x0; x <= x1; x++) cells.Add(new IntVec3(x, 0, z0));
            }
            else
            {
                for (int z = z0; z <= z1; z++) cells.Add(new IntVec3(x0, 0, z));
            }
            return cells;
        }

        private bool FenceCellOk(Plan plan, IntVec3 c, CellRect gate)
        {
            Map map = plan.map;
            if (!c.InBounds(map) || gate.Contains(c) || plan.blocked.Any(r => r.Contains(c))) return false;
            if (c.GetEdifice(map) != null || c.GetTerrain(map).IsWater) return false;
            return GenConstruct.CanBuildOnTerrain(fencePostDef, c, map, Rot4.North, null, fencePostStuff);
        }

        /// <summary>立柱—鐵絲網—鐵絲網—立柱…，區段兩端一定是立柱。Post, wire, wire, post…; a run always starts and ends on a post.</summary>
        private void PlaceFenceRun(Map map, List<IntVec3> line, int a, int b)
        {
            int k = a;
            PlacePost(map, line[k]);
            k++;
            while (k <= b)
            {
                if (k + 2 <= b)
                {
                    if (!PlaceWire(map, line[k], line[k + 1]))
                    {
                        PlacePost(map, line[k]);
                        PlacePost(map, line[k + 1]);
                    }
                    PlacePost(map, line[k + 2]);
                    k += 3;
                }
                else
                {
                    PlacePost(map, line[k]);
                    k++;
                }
            }
        }

        private void PlacePost(Map map, IntVec3 c)
        {
            ClearCell(map, c);
            GenSpawn.Spawn(ThingMaker.MakeThing(fencePostDef, fencePostStuff), c, map, Rot4.North, WipeMode.VanishOrMoveAside);
        }

        /// <summary>
        /// 在 a、b 兩格放一段鐵絲網。鐵絲網是 2x1，找出剛好蓋住這兩格的錨點與方向（橫向或縱向都行）。
        /// One stretch of barbed wire over cells a and b. The wire is 2x1, so find the anchor and rotation whose footprint
        /// covers exactly those two cells, whichever way the fence runs.
        /// </summary>
        private bool PlaceWire(Map map, IntVec3 a, IntVec3 b)
        {
            if (fenceWireDef == null) return false;
            foreach (IntVec3 anchor in new[] { a, b })
            {
                foreach (Rot4 rot in Sides)
                {
                    CellRect rect = GenAdj.OccupiedRect(anchor, rot, fenceWireDef.size);
                    if (rect.Area != 2 || !rect.Contains(a) || !rect.Contains(b)) continue;
                    if (!GenConstruct.CanBuildOnTerrain(fenceWireDef, anchor, map, rot, null, null)) return false;

                    ClearCell(map, a);
                    ClearCell(map, b);
                    GenSpawn.Spawn(ThingMaker.MakeThing(fenceWireDef), anchor, map, rot, WipeMode.VanishOrMoveAside);
                    return true;
                }
            }
            return false;
        }

        // ── 守墓人 / Gravekeepers ─────────────────────────────────────────────

        private void SpawnGuards(Plan plan, GenStepParams parms)
        {
            ModExtension_ArsenalSite ext = parms.sitePart?.def.GetModExtension<ModExtension_ArsenalSite>();
            if (ext == null || ext.squad.NullOrEmpty())
            {
                Log.Warning("[DMS] GenStep_ArsenalSite: the site part has no ModExtension_ArsenalSite with a squad; no guards placed.");
                return;
            }

            Map map = plan.map;
            Faction faction = plan.faction ?? Faction.OfMechanoids;
            List<PawnKindDef> roster = GravekeeperPatrolPlacement.RollRoster(ext.squad, ext.GuardCountFor(parms.sitePart.parms.threatPoints));
            if (roster.Count == 0) return;

            List<IntVec3> route = BuildRoute(plan, out List<int> posts);
            if (route.Count < 2)
            {
                Log.Warning("[DMS] GenStep_ArsenalSite: no usable patrol route; the guards will hold the pad.");
                route = new List<IntVec3> { plan.center, plan.center };
                posts = new List<int>();
            }

            List<CellRect> restricted = plan.bunkers.Select(b => b.rect.ContractedBy(1)).Where(r => r.Area > 0).ToList();
            List<Thing> sealedDoors = plan.bunkers
                .SelectMany(b => b.rect.Cells)
                .SelectMany(c => c.GetThingList(map))
                .Where(t => t is Building_RollingDoor_AccessLink)
                .Distinct()
                .ToList();

            LordJob_GravekeeperPatrol job = new LordJob_GravekeeperPatrol
            {
                route = route,
                tombPosts = posts,
                restricted = restricted,
                tombDoors = sealedDoors,
                overwatchKinds = new List<PawnKindDef>(ext.overwatchKinds ?? new List<PawnKindDef>()),
                followRadius = ext.followRadius,
                overwatchFollowRadius = ext.overwatchFollowRadius,
                sightRange = ext.sightRange,
                engageAlertLevel = ext.engageAlertLevel,
                engageOnSight = ext.engageOnSight,
                calmDownTicks = ext.calmDownTicks,
                tombPauseTicks = ext.postPauseTicks,
                challengeMessageKey = ext.challengeMessageKey,
                engageMessageKey = ext.engageMessageKey,
                searchMessageKey = ext.searchMessageKey,
            };
            // 分隊在停機坪集結，從離停機坪最近的路線點出發。The squad musters on the pad and sets off from the nearest route point.
            job.startIndex = Mathf.Max(0, job.NearestRouteIndex(plan.center));

            List<Pawn> pawns = new List<Pawn>();
            foreach (PawnKindDef kind in roster)
            {
                if (!CellFinder.TryFindRandomCellNear(plan.center, map, 8,
                        c => c.Standable(map) && !job.InRestricted(c) && c.GetFirstPawn(map) == null, out IntVec3 cell))
                {
                    continue;
                }
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer, map.Tile));
                if (pawn == null) continue;
                GenSpawn.Spawn(pawn, cell, map, Rot4.Random);
                pawns.Add(pawn);
            }
            if (pawns.Count > 0)
            {
                LordMaker.MakeNewLord(faction, job, map, pawns);
            }
        }

        /// <summary>
        /// 巡邏路線：洞庫門口（崗位）、監視塔支線口、環路四角與主幹道中段，依繞核心的角度排成一圈。
        /// The patrol loop: bunker entrances (posts), watchtower branch ends, the ring's corners and the middle of the trunk
        /// road, ordered by angle round the core.
        /// </summary>
        private List<IntVec3> BuildRoute(Plan plan, out List<int> posts)
        {
            Map map = plan.map;
            IntVec3 c = plan.center;
            int r = plan.ringRadius;

            List<(IntVec3 cell, bool post)> stops = new List<(IntVec3, bool)>();
            foreach (Bunker bunker in plan.bunkers) stops.Add((bunker.post, true));
            foreach (IntVec3 door in plan.towerDoors) stops.Add((door, false));
            stops.Add((c + new IntVec3(r, 0, r), false));
            stops.Add((c + new IntVec3(-r, 0, r), false));
            stops.Add((c + new IntVec3(r, 0, -r), false));
            stops.Add((c + new IntVec3(-r, 0, -r), false));

            CellRect fence = CellRect.WholeMap(map).ContractedBy(fenceInset);
            int toFence = FarEdge(fence, c, plan.front); // 中心到正門那側圍欄的距離。Centre to the front fence line.
            stops.Add((c + plan.front.FacingCell * Mathf.Max(r + 4, toFence / 2), false));

            List<IntVec3> route = new List<IntVec3>();
            posts = new List<int>();
            foreach ((IntVec3 cell, bool post) in stops.OrderBy(s => Mathf.Atan2(s.cell.z - c.z, s.cell.x - c.x)))
            {
                if (!CellFinder.TryFindRandomCellNear(cell, map, 4, x => x.Standable(map) && !plan.blocked.Any(b => b.Contains(x)), out IntVec3 stand))
                {
                    continue;
                }
                if (post) posts.Add(route.Count);
                route.Add(stand);
            }
            return route;
        }

        // ── 共用 / Shared ────────────────────────────────────────────────────

        private static void Block(Plan plan, CellRect rect)
        {
            plan.blocked.Add(rect);
            FFF_StructureUtility.ReserveUsedRect(plan.map, rect, 2);
        }

        /// <summary>岩石、水面、不可站立的格子。Rock, water or otherwise unstandable ground.</summary>
        private static bool IsBadGround(IntVec3 c, Map map)
        {
            if (!c.InBounds(map)) return true;
            if (c.GetTerrain(map).IsWater) return true;
            Building edifice = c.GetEdifice(map);
            return edifice != null && edifice.def.building != null && edifice.def.building.isNaturalRock;
        }

        /// <summary>結構落地前清掉足跡內的天然岩石、植物與碎石，免得洞庫大廳裡還卡著山。Clears natural rock, plants and chunks from a footprint before a structure lands.</summary>
        private static void ClearGround(Map map, CellRect rect)
        {
            foreach (IntVec3 c in rect)
            {
                if (!c.InBounds(map)) continue;
                List<Thing> things = c.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing t = things[i];
                    if (t.def.building != null && t.def.building.isNaturalRock && t.def.destroyable)
                    {
                        t.Destroy(DestroyMode.Vanish);
                    }
                }
                ClearCell(map, c);
            }
        }

        /// <summary>清掉一格上的植物與碎石。Clears plants and chunks off a cell.</summary>
        private static void ClearCell(Map map, IntVec3 c)
        {
            List<Thing> things = c.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (!t.def.destroyable) continue;
                bool chunk = t.def.thingCategories != null && t.def.thingCategories.Contains(ThingCategoryDefOf.Chunks);
                if (t.def.category == ThingCategory.Plant || chunk)
                {
                    t.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static bool Inside(CellRect outer, CellRect inner)
        {
            return inner.minX >= outer.minX && inner.maxX <= outer.maxX && inner.minZ >= outer.minZ && inner.maxZ <= outer.maxZ;
        }

        private static CellRect Bounds(List<CellRect> rects)
        {
            return CellRect.FromLimits(rects.Min(r => r.minX), rects.Min(r => r.minZ), rects.Max(r => r.maxX), rects.Max(r => r.maxZ));
        }

        /// <summary>offset 的主要方向。The dominant direction of <paramref name="offset"/>.</summary>
        private static Rot4 DirectionOf(IntVec3 offset)
        {
            if (Mathf.Abs(offset.x) > Mathf.Abs(offset.z)) return offset.x > 0 ? Rot4.East : Rot4.West;
            return offset.z > 0 ? Rot4.North : Rot4.South;
        }

        /// <summary>從 c 沿 dir 到 r 靠近 c 那一側邊的距離。From c along dir to the edge of r nearest c.</summary>
        private static int NearEdge(CellRect r, IntVec3 c, Rot4 dir)
        {
            switch (dir.AsInt)
            {
                case 0: return r.minZ - c.z;
                case 1: return r.minX - c.x;
                case 2: return c.z - r.maxZ;
                default: return c.x - r.maxX;
            }
        }

        /// <summary>從 c 沿 dir 到 r 遠離 c 那一側邊的距離。From c along dir to the edge of r furthest from c.</summary>
        private static int FarEdge(CellRect r, IntVec3 c, Rot4 dir)
        {
            switch (dir.AsInt)
            {
                case 0: return r.maxZ - c.z;
                case 1: return r.maxX - c.x;
                case 2: return c.z - r.minZ;
                default: return c.x - r.minX;
            }
        }

        /// <summary>r 在 side 那一側邊的中點再往外一格。One cell outside the middle of r's <paramref name="side"/> edge.</summary>
        private static IntVec3 EdgeOutside(CellRect r, Rot4 side)
        {
            IntVec3 mid = r.CenterCell;
            switch (side.AsInt)
            {
                case 0: return new IntVec3(mid.x, 0, r.maxZ + 1);
                case 1: return new IntVec3(r.maxX + 1, 0, mid.z);
                case 2: return new IntVec3(mid.x, 0, r.minZ - 1);
                default: return new IntVec3(r.minX - 1, 0, mid.z);
            }
        }
    }
}
