using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>密室裡要擺的一類設施：從 defs 隨機挑，擺 count 件。One kind of secret-room fixture: count picks from defs.</summary>
    public class TunnelRoomFixture
    {
        public List<ThingDef> defs = new List<ThingDef>();
        public IntRange count = IntRange.One;
    }

    /// <summary>
    /// 掛在 StructureLayoutDef 上：檢修通道網（通風管道）的設定。
    /// On a StructureLayoutDef: settings for the maintenance tunnel (vent duct) network.
    /// </summary>
    public class ModExtension_VaultTunnels : DefModExtension
    {
        /// <summary>一定會用管道接進檢修通道網的設備間。Equipment rooms always ducted into the network.</summary>
        public List<LayoutRoomDef> equipmentRooms = new List<LayoutRoomDef>();

        /// <summary>同分區的其他一般房間各有這個機率也接上管道。Chance each other ordinary room in the sector joins too.</summary>
        public float extraRoomChance = 0.35f;

        /// <summary>沒有設備間的分區仍長出一個網路的機率（從一般房間起頭）。Chance a sector with no equipment room still gets a network, seeded from an ordinary room.</summary>
        public float otherSectorChance = 0.5f;

        /// <summary>
        /// 每個網路挖一間只能經由管道進入的密室（含牆的邊長），配電盤掛在裡面；挖不下才退回管道壁龕。
        /// Each network digs a secret room reachable only from the tunnel (edge length, walls included) and hangs the
        /// substation in it; only if none fits does the substation fall back to a niche off the tunnel.
        /// </summary>
        public IntRange secretRoomSize = new IntRange(6, 8);

        /// <summary>
        /// 密室裡靠牆擺的設施（例如主動式通風口、設施機器），背面貼牆，不擋通風口內側與配電盤的互動格。
        /// Fixtures set against the secret room's walls (e.g. active vents, facility machines), backs to the wall, never
        /// on the cell inside the vent or the substation's interaction cell.
        /// </summary>
        public List<TunnelRoomFixture> secretRoomFixtures = new List<TunnelRoomFixture>();

        /// <summary>管道地板；null = 版面的地板。Tunnel floor; null = the layout's floor.</summary>
        public TerrainDef floorDef;

        /// <summary>
        /// 管道各端開口，預設原版 Vent（只通空氣、不能走，要打破或拆掉才進得去）；也可以換成門。
        /// The opening at each end of a duct: vanilla Vent by default (air only, not walkable; break or deconstruct it
        /// to get in). A door works too.
        /// </summary>
        public ThingDef hatchDef;
        public ThingDef hatchStuff;

        /// <summary>沿管道鋪的電纜，把配電盤接回走廊中線。Conduit along the tunnels, tying the substation into the corridor strip.</summary>
        public ThingDef conduitDef;

        /// <summary>密室（或管道壁龕）裡的壁掛配電盤，取代原本走廊牆上那座。Wall substation in the secret room (or a tunnel niche); replaces the corridor-wall one.</summary>
        public ThingDef substationDef;

        /// <summary>每個網路接回主走廊的出入口數。Hatches back onto the main corridor per network.</summary>
        public IntRange corridorLinks = new IntRange(1, 2);

        /// <summary>單段管道的長度上限。Length cap for a single duct.</summary>
        public int maxLegLength = 45;

        /// <summary>整張地圖一個網路都沒長出來時，仍在最長的走廊旁挖一段檢修迴路放配電盤。
        /// If no network grew anywhere, still dig a service loop off the longest corridor to hold the substation.</summary>
        public bool standaloneWhenNoEquipment = true;
    }

    /// <summary>
    /// 檢修通道網：在結構外的岩層裡挖 1 格寬、兩側砌牆的管道，把同一分區的設備間（必接）與其他一般房間（機率）
    /// 串起來，再在一兩處接回該分區的主走廊，並延伸出一間只能從管道進入的密室。每個開口都是通風口（原版 Vent），
    /// 要打破或拆掉才進得去。配電盤掛在密室裡，電纜沿管道穿過開口接到走廊中線，整條走廊的電都從這裡來。
    ///
    /// 管道只接同一分區：房間的分區由它的門通往哪段走廊決定，網路只開口到那段走廊；不同分區的管道與密室彼此至少
    /// 隔一道牆，不碰天然洞穴，也不貼著既有的門，所以不會多出繞過分區閘門或密封門的路。
    ///
    /// Maintenance tunnels: 1-wide, walled ducts dug through the rock outside the structure, linking a sector's
    /// equipment rooms (always) and other ordinary rooms (by chance), opening back onto that sector's corridor in one
    /// or two places, and running on to a secret room reachable only from the duct. Every opening is a vent (vanilla
    /// Vent) that has to be broken or deconstructed to get through. The substation hangs in the secret room and its
    /// conduit runs through an opening to the corridor strip, so it powers the corridor.
    ///
    /// Tunnels stay inside one sector: a room's sector is the corridor its doors open onto, and its network only opens
    /// onto that corridor. Different sectors' tunnels and secret rooms keep at least a wall between them, never breach
    /// a natural cave and never sit against an existing door, so they add no way around the sector gates or sealed
    /// doors.
    /// </summary>
    public static class VaultMaintenanceTunnels
    {
        private const int MapMargin = 3;
        /// <summary>檢修口附近這麼多格內不能已有別的門（含閘門）。No other door (gates included) this close to a hatch.</summary>
        private const int HatchClearance = 2;
        /// <summary>同一網路兩個走廊檢修口的最小間距。Minimum spacing between one network's corridor hatches.</summary>
        private const int MinLinkSpacing = 8;
        private const int StepCost = 10;
        /// <summary>轉彎成本，讓管道走直線。Turn cost, so ducts run straight.</summary>
        private const int TurnCost = 25;

        private class Site
        {
            public IntVec3 wall;
            public IntVec3 inner;
            public IntVec3 outer;
            public IntVec3 dir;
            public bool corridor;
        }

        private class Network
        {
            public int sector;
            public readonly List<IntVec3> cells = new List<IntVec3>();
            public readonly List<Site> hatches = new List<Site>();
            public IntVec3 niche = IntVec3.Invalid;
            public Rot4 nicheRot;
            /// <summary>密室（含牆）、通往它的通風口、從管道往密室的方向。The secret room (walls included), its vent, and the direction from the tunnel into it.</summary>
            public CellRect secretRoom = CellRect.Empty;
            public IntVec3 secretVent = IntVec3.Invalid;
            public IntVec3 secretDir;
        }

        private class Context
        {
            public Map map;
            public ModExtension_VaultTunnels ext;
            public ThingDef wallDef;
            public TerrainDef floorDef;
            public List<CellRect> corridorInteriors;
            public HashSet<IntVec3> structure = new HashSet<IntVec3>();
            /// <summary>管道格屬於哪個分區（含還沒生成的規劃格）。Which sector owns a tunnel cell, planned ones included.</summary>
            public Dictionary<IntVec3, int> owner = new Dictionary<IntVec3, int>();
            /// <summary>密室佔用的格子（含牆）屬於哪個分區。Which sector a secret room cell (walls included) belongs to.</summary>
            public Dictionary<IntVec3, int> reserved = new Dictionary<IntVec3, int>();
        }

        /// <summary>
        /// 生成整張地圖的檢修通道網，回傳放了幾座配電盤。0 = 呼叫端要自己補電源。
        /// Builds the whole map's tunnel networks and returns how many substations went in; 0 means the caller has to
        /// power the corridor some other way.
        /// </summary>
        public static int Spawn(StructureLayout layout, VaultLayoutPlan plan, Map map, Faction faction,
            ModExtension_VaultTunnels ext, ThingDef wallDef, TerrainDef layoutFloor)
        {
            if (ext == null || plan == null || plan.corridors.Count == 0 || wallDef == null) return 0;

            Context ctx = new Context
            {
                map = map,
                ext = ext,
                wallDef = wallDef,
                floorDef = ext.floorDef ?? layoutFloor ?? TerrainDefOf.Concrete,
                corridorInteriors = plan.corridors.Select(c => c.rect.ContractedBy(1)).ToList()
            };
            foreach (LayoutRoom room in layout.Rooms)
            {
                foreach (CellRect rect in room.rects)
                {
                    foreach (IntVec3 cell in rect) ctx.structure.Add(cell);
                }
            }

            List<Network> networks = new List<Network>();

            // 房間依主分區分組：設備間必接，其他一般房間看機率。Group rooms by home sector: equipment rooms always, others by chance.
            Dictionary<int, List<LayoutRoom>> equipment = new Dictionary<int, List<LayoutRoom>>();
            Dictionary<int, List<LayoutRoom>> others = new Dictionary<int, List<LayoutRoom>>();
            foreach (LayoutRoom room in layout.Rooms)
            {
                if (IsOffLimits(room)) continue;
                int sector = HomeSector(ctx, layout, room);
                if (sector < 0) continue;

                Dictionary<int, List<LayoutRoom>> target = ext.equipmentRooms.Any(room.HasLayoutDef) ? equipment : others;
                if (!target.TryGetValue(sector, out List<LayoutRoom> list)) target[sector] = list = new List<LayoutRoom>();
                list.Add(room);
            }

            foreach (int sector in equipment.Keys.Union(others.Keys).InRandomOrder())
            {
                List<LayoutRoom> anchors = equipment.TryGetValue(sector, out List<LayoutRoom> eq) ? new List<LayoutRoom>(eq) : new List<LayoutRoom>();
                List<LayoutRoom> ordinary = others.TryGetValue(sector, out List<LayoutRoom> o) ? o : new List<LayoutRoom>();
                if (anchors.Count == 0)
                {
                    if (ordinary.Count == 0 || !Rand.Chance(ext.otherSectorChance)) continue;
                    anchors.Add(ordinary.RandomElement());
                }
                anchors.AddRange(ordinary.Where(r => !anchors.Contains(r) && Rand.Chance(ext.extraRoomChance)).ToList());

                Network net = TryBuildNetwork(ctx, sector, anchors);
                if (net != null) networks.Add(net);
            }

            if (networks.Count == 0 && ext.standaloneWhenNoEquipment)
            {
                foreach (int sector in Enumerable.Range(0, plan.corridors.Count).OrderByDescending(i => plan.corridors[i].rect.Area))
                {
                    Network net = TryBuildServiceLoop(ctx, sector);
                    if (net == null) continue;
                    networks.Add(net);
                    break;
                }
            }

            int substations = 0;
            foreach (Network net in networks)
            {
                if (SpawnNetwork(ctx, net, faction)) substations++;
            }
            return substations;
        }

        // ── 分區 / Sectors ──────────────────────────────────────────────────

        /// <summary>走廊、獎勵房（含伺服機房）與電梯廳不接管道。Corridors, treasuries (server halls included) and the lift lobby never get ducts.</summary>
        private static bool IsOffLimits(LayoutRoom room)
        {
            if (room.defs.NullOrEmpty()) return true;
            foreach (LayoutRoomDef def in room.defs)
            {
                System.Type worker = def.roomContentsWorkerType;
                if (worker == null) continue;
                if (typeof(RoomContents_Corridor).IsAssignableFrom(worker)
                    || VaultTreasuryUtility.IsTreasuryWorker(worker)
                    || worker == typeof(RoomContents_VaultEntrance))
                {
                    return true;
                }
            }
            return false;
        }

        private static int SectorOf(Context ctx, IntVec3 cell)
        {
            for (int i = 0; i < ctx.corridorInteriors.Count; i++)
            {
                if (ctx.corridorInteriors[i].Contains(cell)) return i;
            }
            return -1;
        }

        /// <summary>
        /// 房間的門直接通到哪些走廊段；另外回傳門通到的相鄰房間。
        /// The corridor segments this room's doors open straight onto, plus the rooms its other doors lead into.
        /// </summary>
        private static List<int> DoorSectors(Context ctx, StructureLayout layout, LayoutRoom room, List<LayoutRoom> neighbours)
        {
            List<int> sectors = new List<int>();
            foreach (CellRect rect in room.rects)
            {
                foreach (IntVec3 edge in rect.EdgeCells)
                {
                    if (rect.IsCorner(edge) || !(edge.GetEdifice(ctx.map) is Building_Door)) continue;

                    IntVec3 outside = edge + VaultRoomUtility.OutwardDirection(rect, edge);
                    if (room.rects.Any(r => r.ContractedBy(1).Contains(outside))) continue;

                    int sector = SectorOf(ctx, outside);
                    if (sector >= 0)
                    {
                        sectors.Add(sector);
                        continue;
                    }

                    LayoutRoom other = layout.Rooms.FirstOrDefault(r => r != room && r.rects.Any(x => x.ContractedBy(1).Contains(outside)));
                    if (other != null && neighbours != null && !neighbours.Contains(other)) neighbours.Add(other);
                }
            }
            return sectors;
        }

        /// <summary>
        /// 房間的主分區：它自己的門最常通往的走廊段；房間本身沒有走廊門時，退而看隔壁房間的門（獎勵房除外）。
        /// A room's home sector: the corridor its own doors open onto most; failing that, the corridor
        /// a neighbouring room's doors open onto (never through a treasury).
        /// </summary>
        private static int HomeSector(Context ctx, StructureLayout layout, LayoutRoom room)
        {
            List<LayoutRoom> neighbours = new List<LayoutRoom>();
            List<int> sectors = DoorSectors(ctx, layout, room, neighbours);
            if (sectors.Count == 0)
            {
                foreach (LayoutRoom n in neighbours)
                {
                    if (n.defs != null && n.defs.Any(d => VaultTreasuryUtility.IsTreasuryWorker(d.roomContentsWorkerType))) continue;
                    sectors.AddRange(DoorSectors(ctx, layout, n, null));
                }
            }
            if (sectors.Count == 0) return -1;
            return sectors.GroupBy(s => s).OrderByDescending(g => g.Count()).First().Key;
        }

        // ── 網路 / Networks ────────────────────────────────────────────────

        private static Network TryBuildNetwork(Context ctx, int sector, List<LayoutRoom> rooms)
        {
            Network net = new Network { sector = sector };
            List<Site> corridorSites = CorridorSites(ctx, sector);
            if (corridorSites.Count == 0) return null;

            // 第一段：某間房（設備間排前面）→ 走廊。First duct: a room (equipment rooms first) to the corridor.
            List<LayoutRoom> pending = rooms.InRandomOrder().OrderByDescending(r => ctx.ext.equipmentRooms.Any(r.HasLayoutDef)).ToList();
            for (int i = 0; i < pending.Count && net.cells.Count == 0; i++)
            {
                List<Site> roomSites = RoomSites(ctx, pending[i], sector);
                if (roomSites.Count == 0) continue;
                if (TryLeg(ctx, net, roomSites, corridorSites, out Site from, out Site to))
                {
                    net.hatches.Add(from);
                    net.hatches.Add(to);
                    pending.RemoveAt(i);
                }
            }
            if (net.cells.Count == 0) return null;

            // 其餘房間接到既有管道上。The other rooms join the existing tunnel.
            foreach (LayoutRoom room in pending)
            {
                List<Site> roomSites = RoomSites(ctx, room, sector);
                if (roomSites.Count > 0 && TryLeg(ctx, net, null, roomSites, out _, out Site to))
                {
                    net.hatches.Add(to);
                }
            }

            TryExtraCorridorLinks(ctx, net, corridorSites);
            if (!TryPlanPowerRoom(ctx, net))
            {
                Abandon(ctx, net);
                return null;
            }
            return net;
        }

        /// <summary>配電盤的位置：優先密室，挖不下才用管道壁龕。Where the substation goes: a secret room, else a niche off the tunnel.</summary>
        private static bool TryPlanPowerRoom(Context ctx, Network net)
        {
            return TryPlanSecretRoom(ctx, net) || TryPlanNiche(ctx, net);
        }

        /// <summary>沒有設備間時的檢修迴路：從走廊繞出去再繞回同一段走廊。No equipment rooms: a loop out of a corridor and back into it.</summary>
        private static Network TryBuildServiceLoop(Context ctx, int sector)
        {
            List<Site> sites = CorridorSites(ctx, sector);
            foreach (Site start in sites.InRandomOrder().Take(6))
            {
                Network net = new Network { sector = sector };
                List<Site> far = sites.Where(s => s.wall.DistanceTo(start.wall) >= MinLinkSpacing).ToList();
                if (far.Count == 0) continue;
                if (!TryLeg(ctx, net, new List<Site> { start }, far, out Site from, out Site to)) continue;

                net.hatches.Add(from);
                net.hatches.Add(to);
                if (TryPlanPowerRoom(ctx, net)) return net;
                Abandon(ctx, net);
            }
            return null;
        }

        private static void TryExtraCorridorLinks(Context ctx, Network net, List<Site> corridorSites)
        {
            int wanted = ctx.ext.corridorLinks.RandomInRange;
            while (net.hatches.Count(h => h.corridor) < wanted)
            {
                List<Site> far = corridorSites.Where(s => net.hatches.Where(h => h.corridor).All(h => h.wall.DistanceTo(s.wall) >= MinLinkSpacing)).ToList();
                if (far.Count == 0 || !TryLeg(ctx, net, null, far, out _, out Site to)) return;
                net.hatches.Add(to);
            }
        }

        /// <summary>
        /// 挖一段管道：從 sources 的外側格（null = 從既有網路）走到 targets 任一個的外側格，規劃格記入 owner。
        /// Plans one duct from the outer cell of any source site (null = from the existing network) to the outer
        /// cell of any target site, recording the planned cells in owner.
        /// </summary>
        private static bool TryLeg(Context ctx, Network net, List<Site> sources, List<Site> targets, out Site from, out Site to)
        {
            from = null;
            to = null;

            Dictionary<IntVec3, Site> starts = new Dictionary<IntVec3, Site>();
            if (sources != null)
            {
                foreach (Site s in sources) starts[s.outer] = s;
            }
            else
            {
                foreach (IntVec3 c in net.cells) starts[c] = null;
            }
            Dictionary<IntVec3, Site> goals = new Dictionary<IntVec3, Site>();
            foreach (Site t in targets)
            {
                if (!starts.ContainsKey(t.outer)) goals[t.outer] = t;
            }
            if (starts.Count == 0 || goals.Count == 0) return false;

            List<IntVec3> path = FindPath(ctx, net.sector, starts.Keys, goals, out IntVec3 origin);
            if (path == null) return false;

            from = starts.TryGetValue(origin, out Site s0) ? s0 : null;
            to = goals[path[path.Count - 1]];
            foreach (IntVec3 c in path)
            {
                if (ctx.owner.ContainsKey(c)) continue;
                ctx.owner[c] = net.sector;
                net.cells.Add(c);
            }
            return true;
        }

        /// <summary>
        /// 密室：從管道某格往側面，在岩層裡挖一間含牆 secretRoomSize 大小的房間，通風口開在靠管道那面牆上。
        /// 整間（含牆）都要是岩石，外圍一圈不能碰到別的分區或別的密室。
        /// Secret room: off the side of a tunnel cell, a room of secretRoomSize (walls included) dug into the rock, with
        /// its vent in the wall facing the tunnel. The whole footprint must be rock, and the ring around it may not touch
        /// another sector or another secret room.
        /// </summary>
        private static bool TryPlanSecretRoom(Context ctx, Network net)
        {
            HashSet<IntVec3> hatchOuters = new HashSet<IntVec3>(net.hatches.Select(h => h.outer));
            foreach (IntVec3 t in net.cells.Where(c => !hatchOuters.Contains(c)).InRandomOrder().Take(40).ToList())
            {
                foreach (IntVec3 d in GenAdj.CardinalDirections.InRandomOrder())
                {
                    IntVec3 vent = t + d;
                    IntVec3 along = new IntVec3(d.z, 0, d.x);
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        int width = ctx.ext.secretRoomSize.RandomInRange;
                        int depth = ctx.ext.secretRoomSize.RandomInRange;
                        int offset = Rand.RangeInclusive(1, width - 2);   // 通風口不在牆角 / keep the vent off the corners
                        CellRect rect = CellRect.FromLimits(vent - along * offset, vent + along * (width - 1 - offset) + d * (depth - 1));
                        if (!SecretRoomFits(ctx, rect, net.sector)) continue;

                        foreach (IntVec3 c in rect) ctx.reserved[c] = net.sector;
                        net.secretRoom = rect;
                        net.secretVent = vent;
                        net.secretDir = d;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool SecretRoomFits(Context ctx, CellRect rect, int sector)
        {
            Map map = ctx.map;
            foreach (IntVec3 c in rect)
            {
                if (c.x < MapMargin || c.z < MapMargin || c.x >= map.Size.x - MapMargin || c.z >= map.Size.z - MapMargin) return false;
                if (ctx.structure.Contains(c) || ctx.owner.ContainsKey(c) || ctx.reserved.ContainsKey(c)) return false;
                if (!IsNaturalRock(map, c)) return false;
            }
            foreach (IntVec3 c in rect.ExpandedBy(1).EdgeCells)
            {
                if (!c.InBounds(map) || ctx.reserved.ContainsKey(c)) return false;
                if (ctx.owner.TryGetValue(c, out int other) && other != sector) return false;
            }
            return true;
        }

        /// <summary>
        /// 配電盤壁龕：從管道某格往側面多挖一格死巷，配電盤掛在死巷底，互動格就是管道那一格。
        /// Substation niche: one extra dead-end cell off the side of the tunnel; the panel hangs at its far end
        /// and its interaction cell is the tunnel cell.
        /// </summary>
        private static bool TryPlanNiche(Context ctx, Network net)
        {
            HashSet<IntVec3> hatchOuters = new HashSet<IntVec3>(net.hatches.Select(h => h.outer));
            Vector3 centre = Vector3.zero;
            foreach (IntVec3 c in net.cells) centre += c.ToVector3Shifted();
            centre /= net.cells.Count;

            foreach (IntVec3 t in net.cells.Where(c => !hatchOuters.Contains(c)).OrderBy(c => (c.ToVector3Shifted() - centre).sqrMagnitude))
            {
                foreach (Rot4 rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West }.InRandomOrder())
                {
                    IntVec3 n = t + rot.FacingCell;
                    if (ctx.owner.ContainsKey(n) || !CanCarve(ctx, n, net.sector)) continue;

                    // 真的是死巷：除了 t 之外不能再碰到管道。A true dead end: touches no tunnel except t.
                    bool deadEnd = true;
                    foreach (IntVec3 adj in GenAdj.CardinalDirections)
                    {
                        IntVec3 c = n + adj;
                        if (c != t && ctx.owner.ContainsKey(c)) deadEnd = false;
                    }
                    if (!deadEnd) continue;

                    ctx.owner[n] = net.sector;
                    net.niche = n;
                    net.nicheRot = rot;
                    return true;
                }
            }
            return false;
        }

        private static void Abandon(Context ctx, Network net)
        {
            foreach (IntVec3 c in net.cells) ctx.owner.Remove(c);
            if (net.niche.IsValid) ctx.owner.Remove(net.niche);
            if (!net.secretRoom.IsEmpty)
            {
                foreach (IntVec3 c in net.secretRoom) ctx.reserved.Remove(c);
            }
        }

        // ── 出入口候選 / Hatch sites ──────────────────────────────────────

        /// <summary>
        /// 走廊的檢修口只開在長邊側牆、離兩端至少兩格：往內直走才會穿過中線電纜；端牆上的會跟電纜平行、接不上，
        /// 太靠端點則會碰到攝影機那一小段自己的電纜。
        /// Corridor hatches only go in the long side walls, at least two cells from either end: walking straight in
        /// then crosses the centre strip. One in an end wall would run parallel to it, and one right by an end would
        /// meet a camera's own stub of conduit instead.
        /// </summary>
        private static List<Site> CorridorSites(Context ctx, int sector)
        {
            CellRect interior = ctx.corridorInteriors[sector];
            bool horizontal = interior.Width >= interior.Height;
            return Sites(ctx, new[] { interior.ExpandedBy(1) }, inner => interior.Contains(inner), sector, corridor: true)
                .Where(s => horizontal
                    ? s.dir.x == 0 && s.inner.x >= interior.minX + 2 && s.inner.x <= interior.maxX - 2
                    : s.dir.z == 0 && s.inner.z >= interior.minZ + 2 && s.inner.z <= interior.maxZ - 2)
                .ToList();
        }

        private static List<Site> RoomSites(Context ctx, LayoutRoom room, int sector)
        {
            return Sites(ctx, room.rects, inner => room.rects.Any(r => r.ContractedBy(1).Contains(inner)), sector, corridor: false);
        }

        /// <summary>
        /// 牆上可以開檢修口的格子：非牆角、兩旁是牆、裡面那格是空地、外面那格是可挖的岩層、附近沒有別的門。
        /// Wall cells a hatch can go in: not a corner, walls either side, clear floor inside, diggable rock outside,
        /// and no other door nearby.
        /// </summary>
        private static List<Site> Sites(Context ctx, IEnumerable<CellRect> rects, System.Func<IntVec3, bool> innerOk, int sector, bool corridor)
        {
            List<Site> sites = new List<Site>();
            foreach (CellRect rect in rects)
            {
                foreach (IntVec3 wall in rect.EdgeCells)
                {
                    if (rect.IsCorner(wall) || !wall.InBounds(ctx.map)) continue;

                    IntVec3 dir = VaultRoomUtility.OutwardDirection(rect, wall);
                    IntVec3 inner = wall - dir;
                    IntVec3 outer = wall + dir;
                    if (!innerOk(inner) || !VaultRoomUtility.IsClearFloor(ctx.map, inner)) continue;
                    if (ctx.structure.Contains(outer) || !IsSolidWall(ctx.map, wall)) continue;

                    IntVec3 along = new IntVec3(dir.z, 0, dir.x);
                    if (!IsSolidWall(ctx.map, wall + along) || !IsSolidWall(ctx.map, wall - along)) continue;
                    if (AnyDoorNear(ctx.map, wall, HatchClearance)) continue;
                    if (!CanCarve(ctx, outer, sector)) continue;

                    sites.Add(new Site { wall = wall, inner = inner, outer = outer, dir = dir, corridor = corridor });
                }
            }
            return sites;
        }

        private static bool IsSolidWall(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return false;
            Building edifice = cell.GetEdifice(map);
            return edifice != null && !edifice.def.IsDoor && edifice.def.Fillage == FillCategory.Full;
        }

        private static bool AnyDoorNear(Map map, IntVec3 cell, int radius)
        {
            foreach (IntVec3 c in CellRect.CenteredOn(cell, radius))
            {
                if (c.InBounds(map) && c.GetEdifice(map) is Building_Door) return true;
            }
            return false;
        }

        // ── 挖掘規則與尋路 / Digging rules and pathing ───────────────────

        /// <summary>
        /// 能不能在這格挖管道：在結構外、不在密室裡、是天然岩石；八鄰格只能是岩石、結構、或同分區的管道／密室，
        /// 四鄰格不能是結構的門。這保證管道不會破進天然洞穴、不會碰到別的分區、也不會多出一扇門。
        /// Whether a tunnel may go here: outside the structure, in natural rock; the eight neighbours may only be
        /// rock, structure, or this sector's tunnel, and no cardinal neighbour may be a structure door. That keeps
        /// tunnels out of natural caves, away from other sectors, and from gaining a door they weren't given.
        /// </summary>
        private static bool CanCarve(Context ctx, IntVec3 cell, int sector)
        {
            Map map = ctx.map;
            if (cell.x < MapMargin || cell.z < MapMargin || cell.x >= map.Size.x - MapMargin || cell.z >= map.Size.z - MapMargin) return false;
            if (ctx.structure.Contains(cell) || ctx.reserved.ContainsKey(cell)) return false;
            if (ctx.owner.TryGetValue(cell, out int own)) return own == sector;
            if (!IsNaturalRock(map, cell)) return false;

            for (int i = 0; i < 8; i++)
            {
                IntVec3 n = cell + GenAdj.AdjacentCells[i];
                if (!n.InBounds(map)) return false;
                if (ctx.owner.TryGetValue(n, out int other) || ctx.reserved.TryGetValue(n, out other))
                {
                    if (other != sector) return false;
                    continue;
                }
                if (ctx.structure.Contains(n))
                {
                    if (i < 4 && n.GetEdifice(map) is Building_Door) return false;
                    continue;
                }
                if (!IsNaturalRock(map, n)) return false;
            }
            return true;
        }

        private static bool IsNaturalRock(Map map, IntVec3 cell)
        {
            Building edifice = cell.GetEdifice(map);
            return edifice != null && edifice.def.building != null && edifice.def.building.isNaturalRock;
        }

        private readonly struct Node
        {
            public readonly int cost;
            public readonly IntVec3 cell;
            public Node(int cost, IntVec3 cell) { this.cost = cost; this.cell = cell; }
        }

        private class NodeComparer : IComparer<Node>
        {
            public int Compare(Node a, Node b) => a.cost.CompareTo(b.cost);
        }

        private static readonly NodeComparer Comparer = new NodeComparer();

        /// <summary>多起點 Dijkstra（含轉彎成本與長度上限），回傳起點到終點的格子序列。Multi-source Dijkstra with turn cost and a length cap.</summary>
        private static List<IntVec3> FindPath(Context ctx, int sector, IEnumerable<IntVec3> sources, Dictionary<IntVec3, Site> goals, out IntVec3 origin)
        {
            origin = IntVec3.Invalid;
            Dictionary<IntVec3, int> cost = new Dictionary<IntVec3, int>();
            Dictionary<IntVec3, IntVec3> parent = new Dictionary<IntVec3, IntVec3>();
            Dictionary<IntVec3, int> length = new Dictionary<IntVec3, int>();
            Dictionary<IntVec3, IntVec3> root = new Dictionary<IntVec3, IntVec3>();
            FastPriorityQueue<Node> open = new FastPriorityQueue<Node>(Comparer);

            foreach (IntVec3 s in sources)
            {
                cost[s] = 0;
                length[s] = 0;
                root[s] = s;
                open.Push(new Node(0, s));
            }

            while (open.Count > 0)
            {
                Node node = open.Pop();
                IntVec3 cur = node.cell;
                if (node.cost > cost[cur]) continue;

                if (goals.ContainsKey(cur))
                {
                    origin = root[cur];
                    List<IntVec3> path = new List<IntVec3> { cur };
                    while (parent.TryGetValue(cur, out IntVec3 p))
                    {
                        path.Add(p);
                        cur = p;
                    }
                    path.Reverse();
                    return path;
                }

                if (length[cur] >= ctx.ext.maxLegLength) continue;
                IntVec3 inDir = parent.TryGetValue(cur, out IntVec3 prev) ? cur - prev : IntVec3.Invalid;

                foreach (IntVec3 d in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cur + d;
                    if (!CanCarve(ctx, next, sector)) continue;

                    int step = StepCost + (inDir.IsValid && d != inDir ? TurnCost : 0);
                    int nc = node.cost + step;
                    if (cost.TryGetValue(next, out int old) && old <= nc) continue;

                    cost[next] = nc;
                    parent[next] = cur;
                    length[next] = length[cur] + 1;
                    root[next] = root[cur];
                    open.Push(new Node(nc, next));
                }
            }
            return null;
        }

        // ── 生成 / Spawning ────────────────────────────────────────────────

        private static bool SpawnNetwork(Context ctx, Network net, Faction faction)
        {
            Map map = ctx.map;
            List<IntVec3> carved = new List<IntVec3>(net.cells);
            if (net.niche.IsValid) carved.Add(net.niche);

            // 挖開並鋪地板。Dig out and floor.
            foreach (IntVec3 cell in carved)
            {
                cell.GetEdifice(map)?.Destroy(DestroyMode.Vanish);
                map.terrainGrid.SetTerrain(cell, ctx.floorDef);
            }

            // 兩側砌牆（岩層換成設施牆）；密室的格子由密室自己處理。
            // Line with the facility's wall in place of the rock; the secret room's cells are its own business.
            ThingDef wallStuff = ctx.wallDef.MadeFromStuff ? GenStuff.DefaultStuffFor(ctx.wallDef) : null;
            foreach (IntVec3 cell in carved)
            {
                for (int i = 0; i < 8; i++)
                {
                    IntVec3 n = cell + GenAdj.AdjacentCells[i];
                    if (!n.InBounds(map) || ctx.structure.Contains(n) || ctx.owner.ContainsKey(n) || ctx.reserved.ContainsKey(n)) continue;
                    if (n.GetEdifice(map)?.def == ctx.wallDef) continue;
                    GenSpawn.Spawn(ThingMaker.MakeThing(ctx.wallDef, wallStuff), n, map, WipeMode.Vanish);
                }
            }

            // 開口：設施牆不可破壞，要明確拆掉才放得下通風口。
            // Openings: the facility wall is indestructible, so remove it explicitly first.
            foreach (Site site in net.hatches)
            {
                SpawnOpening(ctx, site.wall, site.dir);
            }

            IntVec3 substationCell = IntVec3.Invalid;
            if (!net.secretRoom.IsEmpty)
            {
                substationCell = SpawnSecretRoom(ctx, net, wallStuff, faction);
            }
            else if (net.niche.IsValid && ctx.ext.substationDef != null)
            {
                VaultRoomUtility.SpawnSecurity(ctx.ext.substationDef, net.niche, map, net.nicheRot, faction);
                substationCell = net.niche;
            }
            if (!substationCell.IsValid) return false;

            // 電纜：整條管道，再穿過走廊開口接到走廊中線。至少一個開口接上走廊電網，這座配電盤才算數。
            // Conduit through the tunnel, then through each corridor opening to the strip. The substation only counts
            // once at least one opening has tied it into the corridor grid.
            if (ctx.ext.conduitDef == null) return false;
            foreach (IntVec3 cell in net.cells) SpawnConduit(ctx, cell);
            if (!net.secretRoom.IsEmpty)
            {
                // 密室：沿內緣一圈，再穿過通風口接到管道。Secret room: round its inner edge, then under the vent to the tunnel.
                foreach (IntVec3 cell in net.secretRoom.ContractedBy(1).EdgeCells) SpawnConduit(ctx, cell);
                SpawnConduit(ctx, net.secretVent);
            }

            bool linked = false;
            CellRect interior = ctx.corridorInteriors[net.sector];
            foreach (Site site in net.hatches.Where(h => h.corridor))
            {
                // 走廊電纜沿牆鋪時，開口那格牆下本來就有電纜；否則從開口往內拉到中線。
                // With the corridor conduit along the walls, the opening's wall cell already carries it; otherwise
                // run a spur inward to the centre strip.
                bool wallLive = site.wall.GetTransmitter(map) != null;
                if (!wallLive && !VaultRoomUtility.TryRunConduit(map, ctx.ext.conduitDef, site.inner, -site.dir, interior, site.wall)) continue;
                SpawnConduit(ctx, site.wall);
                linked = true;
            }
            return linked;
        }

        /// <summary>
        /// 把一格設施牆（或岩石）換成開口。通風口沿穿牆方向擺：它連通的是正前方與正後方那兩格。
        /// Replaces a wall (or rock) cell with an opening. A vent is turned to point through the wall, since it joins the
        /// cells directly in front of and behind it.
        /// </summary>
        private static void SpawnOpening(Context ctx, IntVec3 cell, IntVec3 dir)
        {
            Map map = ctx.map;
            ThingDef def = ctx.ext.hatchDef ?? ThingDefOf.Door;
            ThingDef stuff = def.MadeFromStuff ? ctx.ext.hatchStuff ?? GenStuff.DefaultStuffFor(def) : null;
            Thing.allowDestroyNonDestroyable = true;
            try
            {
                cell.GetEdifice(map)?.Destroy(DestroyMode.Vanish);
            }
            finally
            {
                Thing.allowDestroyNonDestroyable = false;
            }
            Rot4 rot = def.IsDoor ? Rot4.North : Rot4.FromIntVec3(dir);
            GenSpawn.Spawn(ThingMaker.MakeThing(def, stuff), cell, map, rot, WipeMode.Vanish);
        }

        /// <summary>
        /// 挖出密室、砌牆、開通風口，並把配電盤掛在內緣一面牆上（避開通風口正前方那格）。回傳配電盤的位置，失敗為 Invalid。
        /// Digs the secret room, walls it, opens its vent and hangs the substation on an inner wall (not the cell right
        /// inside the vent). Returns the substation's cell, or Invalid.
        /// </summary>
        private static IntVec3 SpawnSecretRoom(Context ctx, Network net, ThingDef wallStuff, Faction faction)
        {
            Map map = ctx.map;
            CellRect rect = net.secretRoom;
            CellRect interior = rect.ContractedBy(1);

            foreach (IntVec3 cell in interior)
            {
                cell.GetEdifice(map)?.Destroy(DestroyMode.Vanish);
                map.terrainGrid.SetTerrain(cell, ctx.floorDef);
            }
            foreach (IntVec3 cell in rect.EdgeCells)
            {
                if (cell == net.secretVent || !cell.InBounds(map)) continue;
                GenSpawn.Spawn(ThingMaker.MakeThing(ctx.wallDef, wallStuff), cell, map, WipeMode.Vanish);
            }
            SpawnOpening(ctx, net.secretVent, net.secretDir);

            if (ctx.ext.substationDef == null) return IntVec3.Invalid;
            IntVec3 entry = net.secretVent + net.secretDir;
            IntVec3 substation = IntVec3.Invalid;
            IntVec3 interaction = IntVec3.Invalid;
            foreach (IntVec3 cell in interior.EdgeCells.InRandomOrder())
            {
                if (cell == entry || !VaultRoomUtility.IsClearFloor(map, cell)) continue;
                foreach (Rot4 rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
                {
                    IntVec3 wall = cell + rot.FacingCell;
                    IntVec3 front = cell - rot.FacingCell;
                    if (interior.Contains(wall) || wall == net.secretVent || !interior.Contains(front) || !front.Standable(map)) continue;
                    if (!VaultRoomUtility.CanAttachToWall(map, cell, rot, wall)) continue;

                    VaultRoomUtility.SpawnSecurity(ctx.ext.substationDef, cell, map, rot, faction);
                    substation = cell;
                    interaction = front;
                    break;
                }
                if (substation.IsValid) break;
            }
            if (!substation.IsValid) return IntVec3.Invalid;

            // 靠牆擺設施，通風口內側與配電盤互動格留空。Fixtures against the walls, leaving the vent's inside cell and the substation's interaction cell clear.
            HashSet<IntVec3> keepClear = new HashSet<IntVec3> { entry, interaction };
            foreach (TunnelRoomFixture fixture in ctx.ext.secretRoomFixtures)
            {
                if (fixture.defs.NullOrEmpty()) continue;
                int count = fixture.count.RandomInRange;
                for (int i = 0; i < count; i++)
                {
                    TryPlaceFixture(map, interior, fixture.defs.RandomElement(), keepClear);
                }
            }
            return substation;
        }

        /// <summary>
        /// 找一個位置把設施整件放進房內、背面那一排整排貼牆（朝向指著牆），格子都空著且不佔 keepClear。
        /// Finds a spot where the fixture sits wholly inside the room with its whole back row against a wall (facing the
        /// wall), on free cells, none of them in keepClear.
        /// </summary>
        private static bool TryPlaceFixture(Map map, CellRect interior, ThingDef def, HashSet<IntVec3> keepClear)
        {
            foreach (IntVec3 cell in interior.Cells.InRandomOrder())
            {
                foreach (Rot4 rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West }.InRandomOrder())
                {
                    CellRect rect = GenAdj.OccupiedRect(cell, rot, def.Size);
                    if (!rect.FullyContainedWithin(interior)) continue;
                    if (rect.GetEdgeCells(rot).Any(c => interior.Contains(c + rot.FacingCell))) continue;
                    if (rect.Cells.Any(c => keepClear.Contains(c) || !VaultRoomUtility.IsClearFloor(map, c))) continue;

                    Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                    GenSpawn.Spawn(thing, cell, map, rot, WipeMode.Vanish);
                    return true;
                }
            }
            return false;
        }

        private static void SpawnConduit(Context ctx, IntVec3 cell)
        {
            if (!cell.InBounds(ctx.map) || cell.GetTransmitter(ctx.map) != null) return;
            GenSpawn.Spawn(ctx.ext.conduitDef, cell, ctx.map);
        }

        // ── 備援 / Fallback ────────────────────────────────────────────────

        /// <summary>
        /// 一條管道都挖不出來時的備援：照舊在走廊牆上掛一座配電盤，並拉電纜接到中線。
        /// Fallback when no tunnel could be dug: hang a substation on a corridor wall as before, with a spur to the strip.
        /// </summary>
        public static bool SpawnCorridorSubstation(Map map, LayoutRoom corridor, ThingDef substationDef, ThingDef conduitDef, Faction faction)
        {
            if (corridor == null || substationDef == null) return false;

            foreach (CellRect rect in corridor.rects.OrderByDescending(r => r.Area))
            {
                CellRect interior = rect.ContractedBy(1);
                // 只掛長邊側牆，電纜往內直走才碰得到中線。Long side walls only, so the spur meets the strip.
                Rot4[] rots = interior.Width >= interior.Height ? new[] { Rot4.North, Rot4.South } : new[] { Rot4.East, Rot4.West };
                foreach (IntVec3 cell in interior.EdgeCells.InRandomOrder())
                {
                    if (!VaultRoomUtility.IsClearFloor(map, cell)) continue;
                    foreach (Rot4 rot in rots)
                    {
                        IntVec3 wall = cell + rot.FacingCell;
                        IntVec3 front = cell - rot.FacingCell;
                        if (interior.Contains(wall) || !interior.Contains(front) || !front.Standable(map)) continue;
                        if (!VaultRoomUtility.CanAttachToWall(map, cell, rot, wall)) continue;
                        // 牆下已有走廊電纜就直接接上；否則拉一段到中線。Wall conduit already there connects it; else a spur to the strip.
                        if (conduitDef != null && wall.GetTransmitter(map) == null
                            && !VaultRoomUtility.TryRunConduit(map, conduitDef, front, -rot.FacingCell, interior, cell)) continue;

                        VaultRoomUtility.SpawnSecurity(substationDef, cell, map, rot, faction);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
