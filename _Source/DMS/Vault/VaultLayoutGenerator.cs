using System.Collections.Generic;
using System.Linq;
using Fortified.Structures;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 StructureLayoutDef 上，調整 <see cref="VaultLayoutGenerator"/> 的走廊網與房間尺寸。
    /// On the StructureLayoutDef; tunes <see cref="VaultLayoutGenerator"/>'s corridor network and rooms.
    /// </summary>
    public class ModExtension_VaultLayout : DefModExtension
    {
        /// <summary>房間沿走廊方向的寬度（含牆）。Room width along the corridor, walls included.</summary>
        public IntRange roomWidthRange = new IntRange(9, 14);

        /// <summary>房間離開走廊的深度（含牆）。Room depth away from the corridor, walls included.</summary>
        public IntRange roomDepthRange = new IntRange(9, 14);

        /// <summary>主幹上要長幾條支道。Branches off the spine.</summary>
        public IntRange branchCountRange = new IntRange(1, 3);

        /// <summary>支道再長出子支道的機率（每一條各擲一次）。Chance a branch grows a sub-branch, rolled per slot.</summary>
        public float subBranchChance = 0.35f;

        /// <summary>
        /// 每條支道最多長幾條子支道，彼此也隔 branchSpacing。預設 1 與舊行為相同；超大型設施靠這個把版面填滿。
        /// Most sub-branches a branch may grow, also spaced by branchSpacing. 1 keeps the old behaviour; the very large
        /// facilities rely on more to fill their footprint.
        /// </summary>
        public int maxSubBranches = 1;

        /// <summary>兩條支道在主幹上至少隔幾格。Minimum spacing between branches along the spine.</summary>
        public int branchSpacing = 18;

        // ── 十字樞紐 / Cross hubs ─────────────────────────────────────────

        /// <summary>
        /// 主幹上放幾座十字樞紐。樞紐是兩條交叉的寬走廊，主幹從兩端穿過、兩側各長一條支道，每個接口都是分區閘門。
        /// 預設 0 = 舊行為。
        /// Cross hubs on the spine. A hub is two crossing wide bars: the spine runs in and out of its ends and a branch
        /// grows off each side, with a sector gate at every arm. 0 (the default) keeps the old behaviour.
        /// </summary>
        public IntRange hubCountRange = IntRange.Zero;

        /// <summary>樞紐十字的總跨度（含牆）。The hub cross's full span, walls included.</summary>
        public int hubSize = 17;

        /// <summary>樞紐每一臂的寬度（含牆），要比走廊寬。Width of each hub arm, walls included; wider than a corridor.</summary>
        public int hubArmWidth = 11;

        /// <summary>樞紐兩側各自長出支道的機率。Chance each side of a hub grows a branch.</summary>
        public float hubBranchChance = 0.85f;

        /// <summary>相鄰兩間房合併成一間大房的機率。Chance two neighbouring rooms merge into one big room.</summary>
        public float mergeChance = 0.25f;

        /// <summary>沿走廊留空不蓋房的機率，讓輪廓不規則。Chance to leave a gap instead of a room, for a ragged outline.</summary>
        public float gapChance = 0.12f;

        /// <summary>走廊盡頭再放一間房。Put a room at each corridor dead end.</summary>
        public bool endRooms = true;

        /// <summary>
        /// 不同分區之間至少隔幾格岩層（牆與牆之間）。房間不能落在別的分區的房間或走廊這個距離內，所以每條支道的起頭
        /// 會留一段兩側不掛房間的走廊，穿過這道岩層才接到下一個分區；支道、子支道、樞紐之間也保持這個間距。
        /// 設成 6 以上時，任何分區的用電設施（連壁掛的，從所掛的牆算起）都搆不到別的分區的電纜：原版自動接線的範圍是 6 格。
        /// 0 = 舊行為（分區可以背靠背共用牆）。只在 sectorGates 開啟時生效。
        ///
        /// Minimum cells of rock between different sectors, wall to wall. No room may come within this distance of
        /// another sector's rooms or corridors, so every branch starts with a stretch of bare corridor, no rooms on
        /// either side, crossing that rock before it reaches the next sector; branches, sub-branches and hubs keep the
        /// same spacing. At 6 or more nothing powered in one sector (wall-mounted things measured from their wall) can
        /// reach another sector's conduit, since vanilla auto-connects within 6 cells.
        /// 0 keeps the old behaviour (sectors may share walls back to back). Only applies with sectorGates on.
        /// </summary>
        public int sectorGap = 0;

        // ── 入口檢查點 / Entrance checkpoints ──────────────────────────────

        /// <summary>
        /// 入口檢查點的房型（掛 <see cref="ModExtension_VaultCheckpoint"/>，指定要蓋的 FFF 結構）。
        /// 沿走廊掛房間時，有 checkpointChance 的機率改成「走廊 → 檢查點 → 房間」：檢查點與走廊、房間各共用一道牆線，
        /// 房間往外推出檢查點的深度，只能穿過檢查點的兩扇門進出。null = 不放。
        /// The entrance checkpoint room type (carrying <see cref="ModExtension_VaultCheckpoint"/>, which names the FFF
        /// structure to stamp). When hanging a room along a corridor there is a checkpointChance it becomes
        /// corridor → checkpoint → room instead: the checkpoint shares a wall line with each, the room is pushed out by
        /// the checkpoint's depth, and the only way in from the corridor is through the checkpoint's two doors.
        /// null = none.
        /// </summary>
        public LayoutRoomDef checkpointRoomDef;

        /// <summary>每間沿走廊的房間改走檢查點的機率。Chance each room along a corridor gets a checkpoint in front of it.</summary>
        public float checkpointChance = 0f;

        // ── 電梯廳 / Lift lobby ───────────────────────────────────────────────

        /// <summary>
        /// 電梯廳接走廊用的門，由寬到窄試，放得下的第一種就用（見 <see cref="VaultEntranceDoor"/>）。
        /// 不論有沒有設，電梯廳都會被調到直接貼著走廊的房間；空的話就只保留版面原本的門。
        /// Doors for the lift lobby's corridor opening, tried widest first; the first that fits is used (see
        /// <see cref="VaultEntranceDoor"/>). Set or not, the lobby is always moved to a room right on a corridor; empty
        /// keeps the layout's own door.
        /// </summary>
        public List<ThingDef> entranceDoorDefs = new List<ThingDef>();

        // ── 走廊嵌入結構 / Corridor embeds ─────────────────────────────────────

        /// <summary>
        /// 房間都掛好之後，沿走廊兩側沒掛房間的空牆往岩層裡嵌的 FFF 結構（壁龕、小隔間……），依各自的 baseWeight 抽。
        /// 結構預設朝北：z = 0 那排落在走廊的牆線上（牆、開口、門照結構畫的），往 +z 長進岩層；size.x 沿走廊。
        /// FFF structures (niches, small bays, …) set into the rock along the bare stretches of corridor wall once every
        /// room is hung, picked by baseWeight. They face north by default: the z = 0 row lands on the corridor's wall line
        /// (walls, openings and doors as drawn) and they grow into the rock towards +z; size.x runs along the corridor.
        /// </summary>
        public List<FFF_StructureDef> embedStructures = new List<FFF_StructureDef>();

        /// <summary>空牆上放得下一個嵌入結構時真的放的機率；沒放就跳過一小段再試。
        /// Chance an embed actually goes in where one fits; otherwise skip ahead a little and try again.</summary>
        public float embedChance = 0.5f;

        // ── 分區閘門 / Sector gates ─────────────────────────────────────────

        /// <summary>
        /// 在每條支道與母走廊的路口封一排密封門，把設施切成幾個分區；閘門旁放一台控制台，駭入即開。
        /// Seal a row of doors where each branch meets its parent corridor, cutting the vault into
        /// sectors; a console beside each gate opens it when hacked.
        /// </summary>
        public bool sectorGates = true;

        /// <summary>每個路口實際封門的機率。Chance a given junction actually gets a gate.</summary>
        public float gateChance = 1f;

        /// <summary>
        /// 可用的密封門，各種寬度混著拼滿走廊：一定要包含 1 格寬的，才保證拼得完。
        /// Sealed doors of assorted widths, mixed to fill the corridor; include a 1-wide one so any
        /// width can be completed.
        /// </summary>
        public List<ThingDef> gateDoorDefs = new List<ThingDef>();

        /// <summary>
        /// 設了就改用單扇門：這扇門放在路口正中，兩側剩下的格子用 gateWallDef 補滿；gateDoorDefs 此時不用。
        /// When set, gates use this one door centred in the junction, with gateWallDef filling the cells on
        /// either side; gateDoorDefs is ignored.
        /// </summary>
        public ThingDef gateDoorDef;

        /// <summary>單扇門兩側補的牆（例如 DMS_SuperWall）。Wall filling either side of the single gate door (e.g. DMS_SuperWall).</summary>
        public ThingDef gateWallDef;

        /// <summary>閘門旁的控制台，帶 CompVaultConsole。Console beside each gate, carrying CompVaultConsole.</summary>
        public ThingDef gateConsoleDef;
    }

    /// <summary>
    /// 產生器留給後續步驟的走廊結構：每段走廊接在哪一段上、路口在哪。
    /// 版面本身不存這些，所以用 <see cref="VaultLayoutGenerator.Plans"/> 暫掛在 StructureLayout 上。
    /// What the generator hands to later steps: which corridor each segment hangs off and where the
    /// junction is. The layout doesn't keep this, so it rides along in
    /// <see cref="VaultLayoutGenerator.Plans"/> keyed by the StructureLayout.
    /// </summary>
    public class VaultLayoutPlan
    {
        public class Corridor
        {
            public CellRect rect;
            /// <summary>母走廊的索引，主幹為 -1。Index of the parent corridor; -1 for the spine.</summary>
            public int parent = -1;
            /// <summary>走廊沿 x 走。Runs along x.</summary>
            public bool horizontal;
            /// <summary>從母走廊往哪個方向長出來（沿本段的軸）。Which way it grows off the parent, along its own axis.</summary>
            public int side;
            /// <summary>跟母走廊是同一座樞紐的兩臂，接口是內部的、不設閘門。Same hub as its parent; the joint is internal and never gated.</summary>
            public bool internalJunction;
        }

        public List<Corridor> corridors = new List<Corridor>();

        /// <summary>
        /// 入口檢查點：矩形、蓋結構時的朝向（結構的 z=0 那一面朝走廊）、對應的版面房間。
        /// An entrance checkpoint: its rect, the rotation to stamp it with (the structure's z = 0 side faces the
        /// corridor), and its layout room.
        /// </summary>
        public class Checkpoint
        {
            public CellRect rect;
            public Rot4 rot;
            public LayoutRoom room;
        }

        public List<Checkpoint> checkpoints = new List<Checkpoint>();

        /// <summary>
        /// 走廊嵌入結構：蓋在哪（含貼著走廊的那排牆線）、用什麼朝向、蓋哪一張。不是版面房間，原版不會填它。
        /// A corridor embed: where it goes (the row on the corridor's wall line included), its rotation and which
        /// structure. Not a layout room, so vanilla never fills it.
        /// </summary>
        public class Embed
        {
            public CellRect rect;
            public Rot4 rot;
            public FFF_StructureDef structure;
        }

        public List<Embed> embeds = new List<Embed>();

        /// <summary>產生時容器的左下角（版面座標）。The container's corner at generation time, in layout space.</summary>
        public IntVec3 origin;

        /// <summary>
        /// 原版生成時只把房間矩形移到地圖座標，走廊結構不會跟著動；生成後用結構實際的容器位置補上位移。
        /// Vanilla moves the room rects into map space on spawn but not this plan; shift it by where the
        /// container actually landed.
        /// </summary>
        public void MoveToMap(CellRect spawnedContainer)
        {
            IntVec3 offset = spawnedContainer.Min - origin;
            if (offset == IntVec3.Zero) return;
            foreach (Corridor c in corridors) c.rect = c.rect.MovedBy(offset);
            foreach (Embed e in embeds) e.rect = e.rect.MovedBy(offset);
            origin = spawnedContainer.Min;
        }
    }

    /// <summary>
    /// 「先開通道、再沿通道掛房間」的版面產生器。
    /// 先在容器裡鋪一條主幹走廊，長出幾條支道，再沿每段走廊兩側與盡頭一間一間貼房間；
    /// 沒被房間佔到的容器範圍就留白（地下就是岩層），所以整體輪廓不會是方的。
    ///
    /// Corridor-first layout: lay a spine corridor across the container, grow branches off it, then
    /// hang rooms along both sides and the dead ends of every corridor. Whatever the rooms don't
    /// reach stays empty (rock, underground), so the footprint is anything but a rectangle.
    ///
    /// 矩形約定與原版相同：rect 含牆，相鄰的房間共用同一條牆線（重疊一格）。
    /// Same rect convention as vanilla: rects include their walls and neighbours share a wall line.
    /// </summary>
    public static class VaultLayoutGenerator
    {
        private static readonly IntRange SpineLengthPct = new IntRange(65, 100);
        private static readonly IntRange BranchLengthPct = new IntRange(45, 100);

        /// <summary>剛產生的版面對應的走廊結構；用過就移除。Corridor plans for freshly generated layouts; removed once consumed.</summary>
        public static readonly Dictionary<StructureLayout, VaultLayoutPlan> Plans = new Dictionary<StructureLayout, VaultLayoutPlan>();

        public static VaultLayoutPlan TakePlan(StructureLayout layout)
        {
            if (layout != null && Plans.TryGetValue(layout, out VaultLayoutPlan plan))
            {
                Plans.Remove(layout);
                return plan;
            }
            return null;
        }

        public static StructureLayout Generate(StructureGenParams parms, CellRect container, LayoutRoomDef corridorDef,
            int corridorExpansion, ModExtension_VaultLayout ext)
        {
            ext ??= new ModExtension_VaultLayout();
            int corridorWidth = corridorExpansion * 2 + 1;

            StructureLayout layout = new StructureLayout(parms.sketch, container);

            VaultLayoutPlan plan = new VaultLayoutPlan { origin = container.Min };
            GrowCorridors(container, corridorWidth, ext, plan);

            Occupancy occ = new Occupancy
            {
                container = container,
                gap = SectorGap(ext),
                corridors = plan.corridors.Select(c => c.rect).ToList(),
                corridorSectors = SectorIds(plan, ext),
                checkpointSize = VaultCheckpointUtility.SizeOf(ext.checkpointRoomDef),
                checkpointChance = ext.checkpointChance,
            };
            for (int i = 0; i < occ.corridors.Count; i++) occ.Take(occ.corridors[i], occ.corridorSectors[i]);
            for (int i = 0; i < occ.corridors.Count; i++)
            {
                HangRooms(occ, occ.corridors[i], occ.corridorSectors[i], ext);
            }

            // 房間都掛好了，剩下的空牆才拿來嵌結構。Every room is hung; only the bare wall left gets embeds.
            if (!ext.embedStructures.NullOrEmpty())
            {
                for (int i = 0; i < occ.corridors.Count; i++)
                {
                    PlaceEmbeds(occ, i, ext);
                }
                plan.embeds.AddRange(occ.embeds);
            }
            List<CellRect> corridors = occ.corridors;
            List<CellRect> rooms = occ.rooms;

            // 走廊：重疊的段合併成一個房間，跟原版 MergeAddCorridors 一樣。
            // Corridors: overlapping segments merge into one room, as vanilla's MergeAddCorridors does.
            foreach (List<CellRect> group in GroupOverlapping(corridors))
            {
                LayoutRoom room = layout.AddRoom(group);
                room.requiredDef = corridorDef;
                room.noExteriorDoors = true;
            }

            foreach (List<CellRect> group in MergeNeighbours(rooms, ext.mergeChance))
            {
                layout.AddRoom(group);
            }

            // 檢查點最後加：共用的牆線格在 roomIds 裡歸給後加的房間，路口那排牆因此算檢查點的。
            // 門在 DMS_LayoutWorker_Vault 開完原版的門之後才依結構重排（VaultCheckpointUtility.FixDoors）。
            // Checkpoints go in last: shared wall-line cells belong to the later room in roomIds, so the joint rows
            // count as the checkpoint's. Their doors are laid out from the structure once DMS_LayoutWorker_Vault has
            // let vanilla place its own (VaultCheckpointUtility.FixDoors).
            foreach (VaultLayoutPlan.Checkpoint cp in occ.checkpoints)
            {
                cp.room = layout.AddRoom(new List<CellRect> { cp.rect }, ext.checkpointRoomDef);
                cp.room.noExteriorDoors = true;
                plan.checkpoints.Add(cp);
            }

            layout.FinalizeRooms();
            Plans[layout] = plan;
            return layout;
        }

        // ── 分區 / Sectors ────────────────────────────────────────────────────

        /// <summary>掛房間時要看的東西：已佔的矩形與它們所屬的分區。What hanging rooms has to see: the taken rects and their sectors.</summary>
        private class Occupancy
        {
            public CellRect container;
            public int gap;
            public List<CellRect> corridors;
            public List<int> corridorSectors;
            public readonly List<CellRect> rooms = new List<CellRect>();

            /// <summary>檢查點的尺寸（x 沿走廊、z 離開走廊，含牆）；沒設檢查點時為 null。Checkpoint size (x along the corridor, z away from it, walls included); null when there are none.</summary>
            public IntVec2? checkpointSize;
            public float checkpointChance;
            public readonly List<VaultLayoutPlan.Checkpoint> checkpoints = new List<VaultLayoutPlan.Checkpoint>();
            public readonly List<VaultLayoutPlan.Embed> embeds = new List<VaultLayoutPlan.Embed>();

            /// <summary>
            /// 所有已佔的矩形與所屬分區，走廊排在最前面（索引與 corridors 相同），之後依放置順序是房間、檢查點、嵌入結構。
            /// Every taken rect with its sector: corridors first (same indices as corridors), then rooms, checkpoints and
            /// embeds in the order they were placed.
            /// </summary>
            public readonly List<(CellRect rect, int sector)> taken = new List<(CellRect, int)>();

            public void Take(CellRect rect, int sector) => taken.Add((rect, sector));
        }

        private static int SectorGap(ModExtension_VaultLayout ext) => ext.sectorGates ? Mathf.Max(0, ext.sectorGap) : 0;

        /// <summary>
        /// 走廊之間的最小間距：原本的 2 格，分區間隙更大時跟著放大。
        /// Minimum spacing between corridors: the old 2 cells, or the sector gap when that is larger.
        /// </summary>
        private static int CorridorClearance(ModExtension_VaultLayout ext) => Mathf.Max(2, SectorGap(ext));

        /// <summary>
        /// 每段走廊屬於哪個分區（以分區起頭那段走廊的索引表示）。每個會設閘門的路口都開一個新分區；
        /// 樞紐兩臂之間是內部接口，沿用母走廊的分區。
        /// Which sector each corridor belongs to, named by the index of the corridor that starts it. Every junction
        /// that can be gated starts a new sector; the joint between a hub's two bars is internal and keeps the parent's.
        /// </summary>
        private static List<int> SectorIds(VaultLayoutPlan plan, ModExtension_VaultLayout ext)
        {
            List<int> ids = new List<int>(plan.corridors.Count);
            for (int i = 0; i < plan.corridors.Count; i++)
            {
                VaultLayoutPlan.Corridor c = plan.corridors[i];
                // 母走廊一定排在前面（AddCorridor 的順序）。Parents always come first (AddCorridor order).
                bool starts = c.parent < 0 || (ext.sectorGates && !c.internalJunction);
                ids.Add(starts ? i : ids[c.parent]);
            }
            return ids;
        }

        // ── 走廊 / Corridors ──────────────────────────────────────────────────

        private static void GrowCorridors(CellRect container, int width, ModExtension_VaultLayout ext, VaultLayoutPlan plan)
        {
            // 主幹沿較長的軸走，不一定貫穿整個容器。The spine follows the longer axis and needn't span it.
            bool horizontal = container.Width >= container.Height;
            int axisLength = horizontal ? container.Width : container.Height;
            int spineLength = Mathf.Clamp(axisLength * SpineLengthPct.RandomInRange / 100, width * 3, axisLength);
            int spineStart = Rand.RangeInclusive(0, axisLength - spineLength);
            int crossCentre = (horizontal ? container.Height : container.Width) / 2;

            int axisMin = horizontal ? container.minX : container.minZ;
            int crossAbs = (horizontal ? container.minZ : container.minX) + crossCentre;
            int spineA = axisMin + spineStart;
            int spineB = spineA + spineLength - 1;

            // 樞紐位置：離主幹兩端與彼此都要夠遠。Hub positions: clear of the spine's ends and of each other.
            int hubHalf = ext.hubSize / 2;
            List<int> hubs = new List<int>();
            int hubCount = ext.hubCountRange.RandomInRange;
            int hubMinPos = spineA + hubHalf + width;
            int hubMaxPos = spineB - hubHalf - width;
            for (int i = 0; i < hubCount && hubMaxPos >= hubMinPos; i++)
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    int candidate = Rand.RangeInclusive(hubMinPos, hubMaxPos);
                    if (hubs.All(h => Mathf.Abs(h - candidate) >= Mathf.Max(ext.hubSize + width * 2, ext.branchSpacing)))
                    {
                        hubs.Add(candidate);
                        break;
                    }
                }
            }
            hubs.Sort();

            // 主幹：一段走廊、一座樞紐、一段走廊……樞紐是前一段的子節點，下一段與兩側支道是樞紐的子節點，
            // 所以樞紐的每一臂都是一個會設閘門的路口。
            // The spine: corridor, hub, corridor, … A hub is a child of the segment before it, and the next segment
            // and the side branches are the hub's children, so every arm of a hub is a gated junction.
            List<(int index, int from, int to)> segments = new List<(int, int, int)>();
            int cursor = spineA;
            int parent = -1;
            foreach (int h in hubs)
            {
                int hubFrom = h - hubHalf;
                int hubTo = h + hubHalf;

                int seg = AddCorridor(plan, AxisRect(horizontal, cursor, hubFrom, crossAbs, width, container), parent, horizontal, 1);
                segments.Add((seg, cursor, hubFrom));

                CellRect barAlong = AxisRect(horizontal, hubFrom, hubTo, crossAbs, ext.hubArmWidth, container);
                CellRect barAcross = AxisRect(!horizontal, crossAbs - hubHalf, crossAbs + hubHalf, h, ext.hubArmWidth, container);
                int along = AddCorridor(plan, barAlong, seg, horizontal, 1);
                int across = AddCorridor(plan, barAcross, along, !horizontal, 0);
                plan.corridors[across].internalJunction = true;

                // 樞紐的支道跟樞紐前一段主幹在幾何上本來就只隔幾格（斜對角），兩者經由樞紐相連，不算間距。
                // A hub branch sits only a few cells diagonally from the spine segment before the hub by construction;
                // the two are joined through the hub, so that segment is exempt from the spacing.
                List<CellRect> hubRects = new List<CellRect> { barAlong, barAcross, plan.corridors[seg].rect };
                for (int side = -1; side <= 1; side += 2)
                {
                    if (!Rand.Chance(ext.hubBranchChance)) continue;
                    CellRect branch = GrowBranch(container, barAcross, !horizontal, h, width, side, plan, hubRects, CorridorClearance(ext));
                    if (branch.IsEmpty) continue;
                    int branchIndex = AddCorridor(plan, branch, across, !horizontal, side);
                    GrowSubBranches(container, width, ext, plan, branchIndex, horizontal);
                }

                parent = along;
                cursor = hubTo;
            }
            int last = AddCorridor(plan, AxisRect(horizontal, cursor, spineB, crossAbs, width, container), parent, horizontal, 1);
            segments.Add((last, cursor, spineB));

            // T 字支道：在主幹段上隔開一段距離各長一條，避開樞紐，隨機往哪一側。
            // T branches: spaced out along the spine segments, clear of the hubs, each going off one side.
            int branches = ext.branchCountRange.RandomInRange;
            List<int> used = new List<int>(hubs);
            // 支道離樞紐的最小距離：支道半寬 + 走廊間距 + 1 才碰不到樞紐的橫臂；不小於舊值。
            // Closest a branch may sit to a hub: its half width + the corridor clearance + 1 keeps it off the hub's
            // bar; never less than the old value.
            int hubClearance = Mathf.Max(hubHalf + width + 2, hubHalf + width / 2 + CorridorClearance(ext) + 1);
            for (int i = 0; i < branches; i++)
            {
                int pos = -1;
                int segIndex = -1;
                for (int attempt = 0; attempt < 20 && pos < 0; attempt++)
                {
                    (int index, int from, int to) seg = segments.RandomElement();
                    int lo = seg.from + width;
                    int hi = seg.to - width;
                    if (hi < lo) continue;
                    int candidate = Rand.RangeInclusive(lo, hi);
                    if (used.All(u => Mathf.Abs(u - candidate) >= ext.branchSpacing)
                        && hubs.All(h => Mathf.Abs(h - candidate) >= hubClearance))
                    {
                        pos = candidate;
                        segIndex = seg.index;
                    }
                }
                if (pos < 0) break;
                used.Add(pos);

                int branchSide = Rand.Bool ? 1 : -1;
                CellRect parentRect = plan.corridors[segIndex].rect;
                CellRect branch = GrowBranch(container, parentRect, !horizontal, pos, width, branchSide, plan, new List<CellRect> { parentRect },
                    CorridorClearance(ext));
                if (branch.IsEmpty) continue;
                int branchIndex = AddCorridor(plan, branch, segIndex, !horizontal, branchSide);
                GrowSubBranches(container, width, ext, plan, branchIndex, horizontal);
            }
        }

        private static int AddCorridor(VaultLayoutPlan plan, CellRect rect, int parent, bool horizontal, int side)
        {
            plan.corridors.Add(new VaultLayoutPlan.Corridor { rect = rect, parent = parent, horizontal = horizontal, side = side });
            return plan.corridors.Count - 1;
        }

        /// <summary>
        /// 沿某軸從 from 到 to（含）的矩形，垂直方向以 cross 為中心、寬 width。座標都是絕對值。
        /// A rect along one axis from from to to (inclusive), width wide and centred on cross across it. Absolute coords.
        /// </summary>
        private static CellRect AxisRect(bool horizontal, int from, int to, int cross, int width, CellRect container)
        {
            int half = width / 2;
            CellRect rect = horizontal
                ? CellRect.FromLimits(from, cross - half, to, cross - half + width - 1)
                : CellRect.FromLimits(cross - half, from, cross - half + width - 1, to);
            return rect.ClipInsideRect(container);
        }

        /// <summary>在一條支道上長出最多 maxSubBranches 條子支道，彼此隔 branchSpacing。Grows up to maxSubBranches sub-branches off a branch, spaced by branchSpacing.</summary>
        private static void GrowSubBranches(CellRect container, int width, ModExtension_VaultLayout ext, VaultLayoutPlan plan,
            int branchIndex, bool subHorizontal)
        {
            CellRect branch = plan.corridors[branchIndex].rect;
            int subLength = !subHorizontal ? branch.Width : branch.Height;
            int subMin = !subHorizontal ? branch.minX : branch.minZ;

            // 子支道離支道兩端的距離：至少一個走廊寬，有分區間隙時還要讓子支道跟支道的母走廊隔開那麼多格。
            // 支道可能往任一側長，路口可能在任一端，所以兩端都留。
            // How far a sub-branch keeps from either end of its branch: at least a corridor width, and with a sector
            // gap far enough that it clears the branch's parent by that much. The branch may grow either way, so the
            // junction can be at either end; keep both clear.
            int margin = Mathf.Max(width, width / 2 + CorridorClearance(ext) + 1);
            if (SectorGap(ext) > 0 && subLength - margin - 1 < margin) return;

            List<int> subUsed = new List<int>();
            for (int s = 0; s < ext.maxSubBranches; s++)
            {
                if (!Rand.Chance(ext.subBranchChance)) continue;

                int subPos = -1;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    int candidate = subMin + Rand.RangeInclusive(margin, Mathf.Max(margin, subLength - margin - 1));
                    if (subUsed.All(u => Mathf.Abs(u - candidate) >= ext.branchSpacing))
                    {
                        subPos = candidate;
                        break;
                    }
                }
                if (subPos < 0) break;
                subUsed.Add(subPos);

                int subSide = Rand.Bool ? 1 : -1;
                CellRect sub = GrowBranch(container, branch, subHorizontal, subPos, width, subSide, plan, new List<CellRect> { branch },
                    CorridorClearance(ext));
                if (!sub.IsEmpty)
                {
                    AddCorridor(plan, sub, branchIndex, subHorizontal, subSide);
                }
            }
        }

        /// <summary>
        /// 從 parent 走廊在 pos 處往 side 方向長一條支道，跟 parent 共用牆線；長度隨機，太短就不長。
        /// 支道會在碰到 exclude（母走廊，或整座樞紐）以外的走廊前 clearance 格停下：兩條走廊一旦相疊就會併成同一個
        /// 房間、變成沒有閘門的路口；有分區間隙時，clearance 也讓不同分區的走廊之間留出那道岩層。
        /// Grows a branch off parent at pos towards side, sharing parent's wall line. It stops clearance cells short of
        /// any corridor outside exclude (the parent, or its whole hub), since overlapping corridors merge into one room:
        /// a junction with no gate. With a sector gap, clearance also keeps that band of rock between sectors' corridors.
        /// Random length; too short to be worth it returns empty.
        /// </summary>
        private static CellRect GrowBranch(CellRect container, CellRect parent, bool horizontal, int pos, int width, int side,
            VaultLayoutPlan plan, List<CellRect> exclude, int clearance)
        {
            CellRect rect = GrowBranchRaw(container, parent, horizontal, pos, width, side, out int minLength);
            if (rect.IsEmpty) return rect;

            List<CellRect> others = plan.corridors.Select(c => c.rect).Where(r => !exclude.Contains(r)).ToList();
            while (others.Any(o => o.Overlaps(rect.ExpandedBy(clearance))))
            {
                // 從遠端往回縮一格。Pull the far end back by one.
                int length = (horizontal ? rect.Width : rect.Height) - 1;
                if (length < minLength) return CellRect.Empty;
                rect = horizontal
                    ? OffSide(parent, false, side, rect.minZ, rect.Height, length)
                    : OffSide(parent, true, side, rect.minX, rect.Width, length);
            }
            return rect;
        }

        private static CellRect GrowBranchRaw(CellRect container, CellRect parent, bool horizontal, int pos, int width, int side,
            out int minLength)
        {
            minLength = width * 2;

            // 支道水平時 pos 是 z，從 parent 的左或右牆線往外長；垂直時反過來。
            // A horizontal branch has pos as z and grows off parent's left/right wall line; vertical, the other way round.
            int start = horizontal ? (side > 0 ? parent.maxX : parent.minX) : (side > 0 ? parent.maxZ : parent.minZ);
            int room = side > 0
                ? (horizontal ? container.maxX : container.maxZ) - start + 1
                : start - (horizontal ? container.minX : container.minZ) + 1;
            int length = Mathf.Clamp(room * BranchLengthPct.RandomInRange / 100, 0, room);
            if (length < minLength) return CellRect.Empty;
            return OffSide(parent, !horizontal, side, pos - width / 2, width, length).ClipInsideRect(container);
        }

        /// <summary>
        /// 貼在 baseRect 某一側、共用它的牆線往外長 depth 格的矩形。along 為 true 表示 baseRect 沿 x 走：矩形長在上下兩側，
        /// 從 x = cursor 起寬 w 格；否則長在左右兩側，從 z = cursor 起。side &gt; 0 是往 +z／+x 長。
        /// A rect on one side of baseRect, sharing its wall line and reaching depth cells out. With along set baseRect runs
        /// along x, so the rect sits above or below it, w wide from x = cursor; otherwise it sits left or right, from
        /// z = cursor. side &gt; 0 grows towards +z / +x.
        /// </summary>
        private static CellRect OffSide(CellRect baseRect, bool along, int side, int cursor, int w, int depth)
        {
            if (along)
            {
                return new CellRect(cursor, side > 0 ? baseRect.maxZ : baseRect.minZ - depth + 1, w, depth);
            }
            return new CellRect(side > 0 ? baseRect.maxX : baseRect.minX - depth + 1, cursor, depth, w);
        }

        /// <summary>
        /// 用 OffSide 往外長的結構的朝向：結構的 z = 0 那面貼著 baseRect。
        /// The rotation for a structure grown out with OffSide: its z = 0 side against baseRect.
        /// </summary>
        private static Rot4 OffSideRot(bool along, int side) => along ? (side > 0 ? Rot4.North : Rot4.South) : (side > 0 ? Rot4.East : Rot4.West);

        /// <summary>深度從 depthRange 隨機值往下試，第一個 Fits 的就用。Tries depths from a random pick in the range downwards; the first that fits wins.</summary>
        private static bool TryFitDepth(Occupancy occ, int sector, IntRange depthRange, System.Func<int, CellRect> rectAt, out CellRect placed)
        {
            for (int depth = depthRange.RandomInRange; depth >= depthRange.min; depth--)
            {
                placed = rectAt(depth);
                if (Fits(occ, placed, sector)) return true;
            }
            placed = CellRect.Empty;
            return false;
        }

        // ── 房間 / Rooms ──────────────────────────────────────────────────────

        private static void HangRooms(Occupancy occ, CellRect corridor, int sector, ModExtension_VaultLayout ext)
        {
            bool horizontal = corridor.Width >= corridor.Height;
            int length = horizontal ? corridor.Width : corridor.Height;
            int start = horizontal ? corridor.minX : corridor.minZ;

            // 兩側 / Both long sides
            for (int side = -1; side <= 1; side += 2)
            {
                int cursor = start;
                while (cursor + ext.roomWidthRange.min - 1 <= start + length - 1)
                {
                    int w = ext.roomWidthRange.RandomInRange;
                    if (cursor + w - 1 > start + length - 1)
                    {
                        w = start + length - cursor;
                        if (w < ext.roomWidthRange.min) break;
                    }

                    if (Rand.Chance(ext.gapChance))
                    {
                        cursor += w - 1;
                        continue;
                    }

                    if (Rand.Chance(occ.checkpointChance)
                        && TryPlaceCheckpointRoom(occ, corridor, sector, horizontal, side, cursor, w, ext.roomDepthRange))
                    {
                        cursor += w - 1;
                    }
                    else if (TryPlaceRoom(occ, corridor, sector, horizontal, side, cursor, w, ext.roomDepthRange, out CellRect placed))
                    {
                        AddRoom(occ, placed, sector);
                        cursor += w - 1;
                    }
                    else
                    {
                        cursor += 2;
                    }
                }
            }

            // 盡頭 / Dead ends
            if (ext.endRooms)
            {
                for (int end = -1; end <= 1; end += 2)
                {
                    TryPlaceEndRoom(occ, corridor, sector, horizontal, end, ext);
                }
            }
        }

        private static void AddRoom(Occupancy occ, CellRect rect, int sector)
        {
            occ.rooms.Add(rect);
            occ.Take(rect, sector);
        }

        // ── 走廊嵌入結構 / Corridor embeds ─────────────────────────────────────

        /// <summary>
        /// 沿一段走廊的兩側往前掃，每個位置抽一張放得下的嵌入結構，擲 embedChance：成功就放下並跳過它的寬度，
        /// 失敗就跳過最窄那張的寬度；什麼都放不下就前進一格。
        /// Sweeps both sides of a corridor: at each position one of the embeds that fit is picked and embedChance rolled;
        /// on success it goes in and the sweep jumps past it, on failure it skips the narrowest embed's width; where
        /// nothing fits it moves on one cell.
        /// </summary>
        private static void PlaceEmbeds(Occupancy occ, int corridorIndex, ModExtension_VaultLayout ext)
        {
            CellRect corridor = occ.corridors[corridorIndex];
            int sector = occ.corridorSectors[corridorIndex];
            bool horizontal = corridor.Width >= corridor.Height;
            int start = horizontal ? corridor.minX : corridor.minZ;
            int end = horizontal ? corridor.maxX : corridor.maxZ;
            List<FFF_StructureDef> structures = ext.embedStructures.Where(s => s != null).ToList();
            if (structures.Count == 0) return;
            int minWidth = structures.Min(s => s.size.x);

            for (int side = -1; side <= 1; side += 2)
            {
                int cursor = start;
                while (cursor + minWidth - 1 <= end)
                {
                    List<VaultLayoutPlan.Embed> fitting = new List<VaultLayoutPlan.Embed>();
                    foreach (FFF_StructureDef s in structures)
                    {
                        if (TryFitEmbed(occ, corridorIndex, sector, horizontal, side, cursor, s, out VaultLayoutPlan.Embed e)) fitting.Add(e);
                    }
                    if (!fitting.TryRandomElementByWeight(e => e.structure.baseWeight, out VaultLayoutPlan.Embed chosen))
                    {
                        cursor++;
                        continue;
                    }
                    if (!Rand.Chance(ext.embedChance))
                    {
                        cursor += minWidth;
                        continue;
                    }

                    occ.embeds.Add(chosen);
                    occ.Take(chosen.rect, sector);
                    cursor += chosen.structure.size.x;
                }
            }
        }

        /// <summary>
        /// 嵌入結構能不能從 cursor 起貼在走廊這一側：整排 z = 0 要落在這段走廊的牆線上（不超出走廊兩端），
        /// 其餘部分只能是岩層：不碰任何房間、檢查點、別的嵌入結構或別段走廊（連共用牆線都不行），
        /// 並守住分區間隙（<see cref="Fits"/>）。結構實際佔地要跟算出來的矩形一致（跟 FFF 生成時的定位方式相同）。
        /// Whether an embed can sit on this side of the corridor from cursor: its whole z = 0 row on this corridor's wall
        /// line (not past either end), and the rest in plain rock: touching no room, checkpoint, other embed or other
        /// corridor (not even a shared wall line), and keeping the sector gap (<see cref="Fits"/>). The structure's real
        /// footprint must match the rect worked out (positioned as FFF's Generate does).
        /// </summary>
        private static bool TryFitEmbed(Occupancy occ, int corridorIndex, int sector, bool horizontal, int side, int cursor,
            FFF_StructureDef structure, out VaultLayoutPlan.Embed embed)
        {
            embed = null;
            CellRect corridor = occ.corridors[corridorIndex];
            int w = structure.size.x;
            int depth = structure.size.z;
            if (cursor + w - 1 > (horizontal ? corridor.maxX : corridor.maxZ)) return false;

            CellRect rect = OffSide(corridor, horizontal, side, cursor, w, depth);
            Rot4 rot = OffSideRot(horizontal, side);

            if (!Fits(occ, rect, sector)) return false;
            // 除了自己這段走廊，什麼都不能碰（連共用牆線都不行）。Nothing but its own corridor may touch it, not even a shared wall line.
            for (int i = 0; i < occ.taken.Count; i++)
            {
                if (i != corridorIndex && occ.taken[i].rect.Overlaps(rect)) return false;
            }
            if (FFF_StructureUtility.FootprintAt(structure, rect.CenterCell, rot) != rect) return false;

            embed = new VaultLayoutPlan.Embed { rect = rect, rot = rot, structure = structure };
            return true;
        }

        /// <summary>
        /// 在走廊某側掛「檢查點 + 房間」：檢查點置中於房間寬度內、貼著走廊牆線，房間接在檢查點的後牆線上。
        /// 兩者都放得下才一起放，否則回傳 false 讓呼叫端照常掛一間房。
        /// Hangs checkpoint + room on one side of the corridor: the checkpoint is centred within the room's width
        /// against the corridor's wall line, and the room starts on the checkpoint's back wall line. Both go in or
        /// neither does; false lets the caller hang an ordinary room instead.
        /// </summary>
        private static bool TryPlaceCheckpointRoom(Occupancy occ, CellRect corridor, int sector, bool horizontal, int side,
            int cursor, int w, IntRange depthRange)
        {
            if (occ.checkpointSize == null) return false;
            int cpWidth = occ.checkpointSize.Value.x;
            int cpDepth = occ.checkpointSize.Value.z;
            if (w < cpWidth) return false;

            CellRect cp = OffSide(corridor, horizontal, side, cursor + (w - cpWidth) / 2, cpWidth, cpDepth);
            if (!Fits(occ, cp, sector)) return false;

            // 房間跟檢查點只共用後牆線，彼此不衝突，可以分開檢查。
            // Room and checkpoint only share the back wall line, so they can be checked independently.
            if (!TryFitDepth(occ, sector, depthRange, depth => OffSide(cp, horizontal, side, cursor, w, depth), out CellRect room))
            {
                return false;
            }
            AddRoom(occ, room, sector);
            occ.checkpoints.Add(new VaultLayoutPlan.Checkpoint { rect = cp, rot = OffSideRot(horizontal, side) });
            occ.Take(cp, sector);
            return true;
        }

        /// <summary>
        /// 在走廊某側、沿走廊 cursor 起 w 格寬處貼一間房，深度從 depthRange 往下試到放得下為止。
        /// Attaches a room w wide at cursor on one side of the corridor, trying depths from the range
        /// downwards until one fits.
        /// </summary>
        private static bool TryPlaceRoom(Occupancy occ, CellRect corridor, int sector, bool horizontal, int side, int cursor, int w,
            IntRange depthRange, out CellRect placed)
        {
            return TryFitDepth(occ, sector, depthRange, depth => OffSide(corridor, horizontal, side, cursor, w, depth), out placed);
        }

        /// <summary>盡頭的房間：接在走廊端牆外，至少跟走廊一樣寬、置中。An end room past the corridor's end wall, at least as wide as the corridor and centred on it.</summary>
        private static void TryPlaceEndRoom(Occupancy occ, CellRect corridor, int sector, bool horizontal, int end,
            ModExtension_VaultLayout ext)
        {
            int corridorWidth = horizontal ? corridor.Height : corridor.Width;
            int w = Mathf.Max(ext.roomWidthRange.RandomInRange, corridorWidth);
            int crossMin = (horizontal ? corridor.minZ : corridor.minX) - (w - corridorWidth) / 2;

            if (TryFitDepth(occ, sector, ext.roomDepthRange, depth => OffSide(corridor, !horizontal, end, crossMin, w, depth), out CellRect rect))
            {
                AddRoom(occ, rect, sector);
            }
        }

        /// <summary>
        /// 房間要整個在容器裡，而且內部（去掉牆）不能碰到任何既有矩形，既有矩形的內部也不能碰到它；
        /// 牆線可以共用，房間本身不能疊。有分區間隙時，別的分區的房間與走廊都要離它 gap 格以上
        /// （中間至少 gap 格岩層），所以不同分區不會背靠背共用牆。
        /// The room must sit inside the container, and its interior may not touch any existing rect,
        /// nor may any existing interior touch it; walls may be shared, rooms may not overlap. With a sector
        /// gap, every other sector's rooms and corridors must also stay gap cells away (at least gap cells of
        /// rock in between), so different sectors never share a wall.
        /// </summary>
        private static bool Fits(Occupancy occ, CellRect rect, int sector)
        {
            CellRect container = occ.container;
            if (rect.minX < container.minX || rect.minZ < container.minZ || rect.maxX > container.maxX || rect.maxZ > container.maxZ)
            {
                return false;
            }

            CellRect interior = rect.ContractedBy(1);
            CellRect halo = rect.ExpandedBy(occ.gap);
            foreach ((CellRect other, int otherSector) in occ.taken)
            {
                if (interior.Overlaps(other) || other.ContractedBy(1).Overlaps(rect)) return false;
                if (occ.gap > 0 && otherSector != sector && halo.Overlaps(other)) return false;
            }
            return true;
        }

        // ── 合併 / Grouping ───────────────────────────────────────────────────

        private static List<List<CellRect>> GroupOverlapping(List<CellRect> rects)
        {
            List<CellRect> pending = new List<CellRect>(rects);
            List<List<CellRect>> groups = new List<List<CellRect>>();

            while (pending.Count > 0)
            {
                List<CellRect> group = new List<CellRect> { pending[0] };
                pending.RemoveAt(0);

                bool grew = true;
                while (grew)
                {
                    grew = false;
                    for (int i = pending.Count - 1; i >= 0; i--)
                    {
                        if (group.Any(g => g.Overlaps(pending[i])))
                        {
                            group.Add(pending[i]);
                            pending.RemoveAt(i);
                            grew = true;
                        }
                    }
                }

                groups.Add(group);
            }

            return groups;
        }

        /// <summary>
        /// 共用一整段牆的相鄰房間有機率併成一間（兩個矩形的大房）。
        /// Neighbouring rooms that share a good stretch of wall sometimes merge into one two-rect room.
        /// </summary>
        private static List<List<CellRect>> MergeNeighbours(List<CellRect> rooms, float mergeChance)
        {
            List<List<CellRect>> groups = new List<List<CellRect>>();
            HashSet<int> used = new HashSet<int>();

            for (int i = 0; i < rooms.Count; i++)
            {
                if (used.Contains(i)) continue;
                used.Add(i);
                List<CellRect> group = new List<CellRect> { rooms[i] };

                if (Rand.Chance(mergeChance))
                {
                    for (int j = i + 1; j < rooms.Count; j++)
                    {
                        if (used.Contains(j)) continue;
                        if (rooms[i].GetAdjacencyScore(rooms[j]) >= 5)
                        {
                            used.Add(j);
                            group.Add(rooms[j]);
                            break;
                        }
                    }
                }

                groups.Add(group);
            }

            return groups;
        }
    }
}
