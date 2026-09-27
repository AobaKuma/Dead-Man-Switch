using System.Collections.Generic;
using System.Linq;
using Fortified.Structures;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 通風中樞：在遠離主結構的岩層裡蓋一座 FFF 結構（ModExtension_VaultTunnels.ventCenterStructures），從它的牆上挖通風管道
    /// 到各個分區：該分區已有檢修通道網就接進網路，沒有就接到該分區的房間或走廊。中樞跟主結構之間只有這些管道，
    /// 兩頭都是通風口，要打破或拆掉才過得去。
    ///
    /// 中樞在所有網路規劃完之後才找位置，所以會避開既有的管道與密室；出口從結構的 sketch 算：牆線上兩旁也是牆、
    /// 裡面是空地板、外面在結構外的格子。管道不能貼著中樞的地板或門挖，只能從這些出口進去。每條中樞管道算在它接到的
    /// 那個分區名下，跟該分區的網路一起挖、砌牆、鋪電纜；中樞裡若有電源（例如配電櫃），它供電的網路也算一座配電盤。
    ///
    /// Vent centre: an FFF structure (ModExtension_VaultTunnels.ventCenterStructures) set in the rock well away from the
    /// main structure, with vent ducts dug from its walls out to the sectors: into a sector's tunnel network where it
    /// has one, otherwise to one of its rooms or its corridor. Those ducts are the centre's only link to the main
    /// structure, with a vent at either end to break or take down.
    ///
    /// The centre is placed once every network is planned, so it keeps clear of their tunnels and secret rooms. Its
    /// exits come from the structure's sketch: wall cells with wall either side, bare floor inside and the outside past
    /// the structure. No tunnel may touch the centre's floor or doors, so those exits are the only way in. Each centre
    /// duct belongs to the sector it reaches and is dug, walled and wired with that sector's network; if the centre
    /// carries a power source (a substation cabinet, say), the networks it feeds count as powered.
    /// </summary>
    public static partial class VaultMaintenanceTunnels
    {
        /// <summary>中樞在 owner / reserved 裡的分區編號（不能是 -1，那是「沒有」）。The centre's sector id in owner / reserved (not -1, which means none).</summary>
        private const int HubSector = -2;
        /// <summary>中樞兩個出口之間的最小間距。Minimum spacing between two of the centre's exits.</summary>
        private const int HubExitSpacing = 4;
        /// <summary>找位置時最多試幾個中心點。Centre cells tried when placing.</summary>
        private const int HubPlacementTries = 400;
        /// <summary>放得下但一條管道都接不出去時，最多換幾次位置。Placements that fit but link nothing, before giving up.</summary>
        private const int HubLinkAttempts = 6;

        /// <summary>某張結構在某個朝向下的形狀，座標是 sketch 本地座標。One structure's shape at one rotation, in sketch-local cells.</summary>
        private class HubShape
        {
            public FFF_StructureDef def;
            public Rot4 rot;
            /// <summary>sketch 佔地的中心；FFF 的 Generate 把它對齊到地圖上的中心點。The sketch's centre, which FFF's Generate lines up with the map centre.</summary>
            public IntVec3 center;
            public List<IntVec3> cells;
            public HashSet<IntVec3> solid;
            public List<Site> exits;
            public bool powered;
        }

        private class VentCenter
        {
            public HubShape shape;
            public IntVec3 center;
            /// <summary>佔地與出口，已換成地圖座標。Footprint and exits, in map cells.</summary>
            public List<IntVec3> cells;
            public List<Site> exits;
            public readonly List<Site> used = new List<Site>();
        }

        // ── 規劃 / Planning ────────────────────────────────────────────────

        /// <summary>
        /// 找一個夠遠的位置放中樞，並從它挖管道到各分區；至少接出一條才算數。成功時中樞佔地已記入 reserved，
        /// 管道已記入 owner 與各網路（新的中樞供電網路會加進 networks）。
        /// Finds a spot far enough out for the centre and digs its ducts to the sectors; it only counts with at least one.
        /// On success the footprint is in reserved and the ducts are in owner and their networks (new hub-fed networks
        /// are added to networks).
        /// </summary>
        private static VentCenter TryPlanVentCenter(Context ctx, List<Network> networks, Dictionary<int, List<LayoutRoom>> roomsBySector)
        {
            ModExtension_VaultTunnels ext = ctx.ext;
            if (ext.ventCenterStructures.NullOrEmpty() || !Rand.Chance(ext.ventCenterChance)) return null;

            List<HubShape> shapes = new List<HubShape>();
            foreach (FFF_StructureDef def in ext.ventCenterStructures.Where(d => d != null))
            {
                foreach (Rot4 rot in Rot4.AllRotations)
                {
                    HubShape shape = BuildShape(def, rot);
                    if (shape.exits.Count > 0) shapes.Add(shape);
                }
            }
            if (shapes.Count == 0)
            {
                Log.WarningOnce("[DMS] No vent centre structure has a wall a duct could open through.", 0x5E47C3);
                return null;
            }

            Map map = ctx.map;
            // 至少隔 3 格：中樞外圍一圈岩層與別的管道的牆之間還要有空間。At least 3, leaving room between the centre's rock ring and other tunnels' walls.
            int min = Mathf.Max(3, ext.ventCenterDistance.min);
            int max = Mathf.Max(min, ext.ventCenterDistance.max);
            int[] dist = DistanceField(ctx);
            int reach = shapes.Max(s => s.cells.Max(c => Mathf.Max(Mathf.Abs(c.x - s.center.x), Mathf.Abs(c.z - s.center.z))));

            List<IntVec3> centres = map.AllCells
                .Where(c =>
                {
                    int d = dist[map.cellIndices.CellToIndex(c)];
                    return d >= min && d <= max + reach && InsideMargin(map, c);
                })
                .InRandomOrder()
                .Take(HubPlacementTries)
                .ToList();

            int linkAttempts = 0;
            foreach (IntVec3 centre in centres)
            {
                HubShape shape = shapes.RandomElementByWeight(s => s.def.baseWeight);
                if (!TryFitVentCenter(ctx, shape, centre, dist, min, max, out VentCenter hub)) continue;

                ReserveVentCenter(ctx, hub, true);
                if (LinkVentCenter(ctx, hub, networks, roomsBySector) > 0)
                {
                    ctx.hubPowered = shape.powered;
                    return hub;
                }
                ReserveVentCenter(ctx, hub, false);
                if (++linkAttempts >= HubLinkAttempts) break;
            }
            return null;
        }

        /// <summary>
        /// 從旋轉後的 sketch 算出佔地、實牆與出口。出口：實牆格、兩旁也是實牆、裡面那格有地板且沒有建築（電纜除外）、
        /// 外面那格在結構外，附近也沒有門。
        /// Works out footprint, solid walls and exits from the rotated sketch. An exit is a solid wall cell with solid wall
        /// either side, a floored cell with no building (conduit aside) inside, the outside cell past the structure, and
        /// no door nearby.
        /// </summary>
        private static HubShape BuildShape(FFF_StructureDef def, Rot4 rot)
        {
            Sketch sketch = def.GetSketch();
            if (rot != Rot4.North) sketch.Rotate(rot);

            HashSet<IntVec3> cells = new HashSet<IntVec3>();
            foreach (SketchThing thing in sketch.Things)
            {
                foreach (IntVec3 c in thing.OccupiedRect) cells.Add(c);
            }
            foreach (SketchTerrain terrain in sketch.Terrain) cells.Add(terrain.pos);

            HashSet<IntVec3> solid = new HashSet<IntVec3>(cells.Where(c => IsSketchWall(sketch, c)));
            List<Site> exits = new List<Site>();
            foreach (IntVec3 wall in solid)
            {
                foreach (IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 outer = wall + dir;
                    IntVec3 inner = wall - dir;
                    IntVec3 along = new IntVec3(dir.z, 0, dir.x);
                    if (cells.Contains(outer) || !solid.Contains(wall + along) || !solid.Contains(wall - along)) continue;
                    if (solid.Contains(inner) || !sketch.AnyTerrainAt(inner) || sketch.ThingsAt(inner).Any(t => BlocksFloor(t.def))) continue;
                    if (SketchDoorNear(sketch, wall)) continue;

                    exits.Add(new Site { wall = wall, inner = inner, outer = outer, dir = dir, hub = true });
                }
            }

            return new HubShape
            {
                def = def,
                rot = rot,
                center = sketch.OccupiedRect.CenterCell,
                cells = cells.ToList(),
                solid = solid,
                exits = exits,
                powered = sketch.Things.Any(t => t.def.comps != null
                    && t.def.comps.Any(p => p is CompProperties_Power power && power.PowerConsumption < 0f))
            };
        }

        private static bool IsSketchWall(Sketch sketch, IntVec3 cell)
        {
            SketchThing edifice = sketch.EdificeAt(cell);
            return edifice != null && !edifice.def.IsDoor && edifice.def.Fillage == FillCategory.Full;
        }

        /// <summary>比照 VaultRoomUtility.IsClearFloor：電纜以外的建築都算擋住。As IsClearFloor: any building but conduit is in the way.</summary>
        private static bool BlocksFloor(ThingDef def)
        {
            return def.category == ThingCategory.Building && (def.building == null || !def.building.isPowerConduit);
        }

        private static bool SketchDoorNear(Sketch sketch, IntVec3 cell)
        {
            foreach (IntVec3 c in CellRect.CenteredOn(cell, HatchClearance))
            {
                if (sketch.ThingsAt(c).Any(t => t.def.IsDoor)) return true;
            }
            return false;
        }

        /// <summary>
        /// 中樞以 centre 為中心能不能放：整片佔地都是天然岩石、每一格離主結構（含管道與密室）至少 min 格、最近的一格
        /// 不超過 max 格；佔地外圍一圈也要是岩石，免得破進天然洞穴。
        /// Whether the centre fits around centre: its whole footprint in natural rock, every cell at least min from the
        /// main structure (tunnels and secret rooms included) and the nearest no more than max; the ring round the
        /// footprint must be rock too, so it doesn't breach a natural cave.
        /// </summary>
        private static bool TryFitVentCenter(Context ctx, HubShape shape, IntVec3 centre, int[] dist, int min, int max, out VentCenter hub)
        {
            hub = null;
            Map map = ctx.map;
            IntVec3 offset = centre - shape.center;
            HashSet<IntVec3> footprint = new HashSet<IntVec3>();
            int nearest = int.MaxValue;
            foreach (IntVec3 local in shape.cells)
            {
                IntVec3 c = local + offset;
                if (!InsideMargin(map, c) || !IsNaturalRock(map, c)) return false;
                if (ctx.structure.Contains(c) || ctx.owner.ContainsKey(c) || ctx.reserved.ContainsKey(c)) return false;
                int d = dist[map.cellIndices.CellToIndex(c)];
                if (d < min) return false;
                if (d < nearest) nearest = d;
                footprint.Add(c);
            }
            if (nearest > max) return false;

            foreach (IntVec3 c in footprint)
            {
                for (int i = 0; i < 8; i++)
                {
                    IntVec3 n = c + GenAdj.AdjacentCells[i];
                    if (footprint.Contains(n)) continue;
                    if (!n.InBounds(map) || !IsNaturalRock(map, n)) return false;
                }
            }

            hub = new VentCenter
            {
                shape = shape,
                center = centre,
                cells = footprint.ToList(),
                exits = shape.exits.Select(e => new Site
                {
                    wall = e.wall + offset,
                    inner = e.inner + offset,
                    outer = e.outer + offset,
                    dir = e.dir,
                    hub = true
                }).ToList()
            };
            return true;
        }

        /// <summary>
        /// 每格到最近的結構、管道或密室格的切比雪夫距離（多起點 BFS）。
        /// Chebyshev distance from every cell to the nearest structure, tunnel or secret room cell (multi-source BFS).
        /// </summary>
        private static int[] DistanceField(Context ctx)
        {
            Map map = ctx.map;
            CellIndices indices = map.cellIndices;
            int[] dist = new int[indices.NumGridCells];
            for (int i = 0; i < dist.Length; i++) dist[i] = int.MaxValue;

            Queue<IntVec3> open = new Queue<IntVec3>();
            foreach (IntVec3 c in ctx.structure.Concat(ctx.owner.Keys).Concat(ctx.reserved.Keys))
            {
                if (!c.InBounds(map)) continue;
                int i = indices.CellToIndex(c);
                if (dist[i] == 0) continue;
                dist[i] = 0;
                open.Enqueue(c);
            }

            while (open.Count > 0)
            {
                IntVec3 cur = open.Dequeue();
                int next = dist[indices.CellToIndex(cur)] + 1;
                for (int k = 0; k < 8; k++)
                {
                    IntVec3 n = cur + GenAdj.AdjacentCells[k];
                    if (!n.InBounds(map)) continue;
                    int i = indices.CellToIndex(n);
                    if (dist[i] <= next) continue;
                    dist[i] = next;
                    open.Enqueue(n);
                }
            }
            return dist;
        }

        /// <summary>記入或撤回中樞佔地：整片算 HubSector，非實牆的格子另記進 hubOpen。Books or withdraws the centre's footprint.</summary>
        private static void ReserveVentCenter(Context ctx, VentCenter hub, bool reserve)
        {
            foreach (IntVec3 c in hub.cells)
            {
                bool open = !hub.shape.solid.Contains(c - (hub.center - hub.shape.center));
                if (reserve)
                {
                    ctx.reserved[c] = HubSector;
                    if (open) ctx.hubOpen.Add(c);
                }
                else
                {
                    ctx.reserved.Remove(c);
                    ctx.hubOpen.Remove(c);
                }
            }
        }

        /// <summary>
        /// 從中樞挖 ventCenterLinks 條管道，由近到遠每個分區一條。回傳挖成幾條。
        /// Digs ventCenterLinks ducts out of the centre, one per sector, nearest sectors first. Returns how many went in.
        /// </summary>
        private static int LinkVentCenter(Context ctx, VentCenter hub, List<Network> networks, Dictionary<int, List<LayoutRoom>> roomsBySector)
        {
            int wanted = ctx.ext.ventCenterLinks.RandomInRange;
            IntVec3 centre = hub.center;
            List<int> sectors = Enumerable.Range(0, ctx.corridorInteriors.Count)
                .OrderBy(s => ctx.corridorInteriors[s].ClosestCellTo(centre).DistanceToSquared(centre))
                .ToList();

            int links = 0;
            foreach (int sector in sectors)
            {
                if (links >= wanted) break;
                if (TryLinkVentCenter(ctx, hub, sector, networks, roomsBySector)) links++;
            }
            return links;
        }

        /// <summary>
        /// 從中樞一個還沒用過的出口挖到某分區：該分區有網路就接進離中樞最近的那個網路的管道，沒有就接到它的房間或走廊，
        /// 並為它開一個只靠中樞供電的新網路。
        /// Digs from an unused centre exit into one sector: into the tunnel of that sector's network nearest the centre if
        /// it has one, else to one of its rooms or its corridor, starting a new network fed only from the centre.
        /// </summary>
        private static bool TryLinkVentCenter(Context ctx, VentCenter hub, int sector, List<Network> networks,
            Dictionary<int, List<LayoutRoom>> roomsBySector)
        {
            Vector3 centre = hub.center.ToVector3Shifted();
            Network net = networks.Where(n => n.sector == sector && n.cells.Count > 0)
                .OrderBy(n => (Centre(n) - centre).sqrMagnitude)
                .FirstOrDefault();

            Dictionary<IntVec3, Site> goals = new Dictionary<IntVec3, Site>();
            if (net != null)
            {
                foreach (IntVec3 c in net.cells) goals[c] = null;
            }
            else
            {
                if (roomsBySector.TryGetValue(sector, out List<LayoutRoom> rooms))
                {
                    foreach (LayoutRoom room in rooms)
                    {
                        foreach (Site site in RoomSites(ctx, room, sector)) goals[site.outer] = site;
                    }
                }
                foreach (Site site in CorridorSites(ctx, sector)) goals[site.outer] = site;
            }
            if (goals.Count == 0) return false;

            Dictionary<IntVec3, Site> starts = new Dictionary<IntVec3, Site>();
            foreach (Site exit in hub.exits)
            {
                if (goals.ContainsKey(exit.outer) || starts.ContainsKey(exit.outer)) continue;
                if (hub.used.Any(u => u.wall.DistanceTo(exit.wall) < HubExitSpacing)) continue;
                if (!CanCarve(ctx, exit.outer, sector, HubSector)) continue;
                starts[exit.outer] = exit;
            }
            if (starts.Count == 0) return false;

            List<IntVec3> path = FindPath(ctx, sector, starts.Keys, goals, out IntVec3 origin, HubSector, ctx.ext.ventCenterMaxLegLength);
            if (path == null) return false;

            Site from = starts[origin];
            hub.used.Add(from);
            if (net == null)
            {
                net = new Network { sector = sector, hubFed = true };
                networks.Add(net);
                Site to = goals[path[path.Count - 1]];
                if (to != null) net.hatches.Add(to);
            }
            net.hatches.Add(from);
            foreach (IntVec3 c in path)
            {
                if (ctx.owner.ContainsKey(c)) continue;
                ctx.owner[c] = sector;
                net.cells.Add(c);
            }
            return true;
        }

        // ── 生成 / Spawning ────────────────────────────────────────────────

        /// <summary>
        /// 拆掉佔地裡的岩石再蓋結構；要在網路生成之前，網路的開口才拆得到中樞的牆。
        /// Clears the rock from the footprint and stamps the structure, before the networks spawn so their openings can
        /// cut its walls.
        /// </summary>
        private static void SpawnVentCenter(Context ctx, VentCenter hub, Faction faction)
        {
            foreach (IntVec3 c in hub.cells) VaultRoomUtility.RemoveEdifice(ctx.map, c);
            // 生成期間電網由遊戲在最後統一建立。The game builds power nets once generation ends.
            FFF_StructureUtility.Generate(hub.shape.def, hub.center, ctx.map, faction, hub.shape.rot, reconnectPower: false);
        }
    }
}
