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

        /// <summary>
        /// 相鄰兩座樞紐中心之間的最小距離。拉大它能在樞紐之間留出大片岩層，降低通道密度（通風中樞也才放得進去）。
        /// 0 = 舊行為：樞紐跨度加兩條走廊寬，或 branchSpacing，取大者。
        /// Minimum distance between neighbouring hub centres. Raising it leaves broad rock between hubs and thins the
        /// corridors out (which is also what gives the vent centre room). 0 keeps the old rule: the hub span plus two
        /// corridor widths, or branchSpacing, whichever is larger.
        /// </summary>
        public int hubSpacing = 0;

        // ── 直線長度與迴廊 / Straight runs and cloisters ─────────────────────

        /// <summary>
        /// 單一直線走廊的長度上限（含牆），0 = 不限。主幹（穿過樞紐也算同一段）與支道超過就轉折：接一段垂直的連接走廊，
        /// 往旁邊偏 jogOffsetRange 格再繼續；樞紐兩側的支道隔著樞紐在同一條線上，各自的第一段只給一半。
        /// Length cap on one straight corridor, walls included; 0 = none. The spine (straight through its hubs) and the
        /// branches bend past it: a perpendicular connector, a jogOffsetRange step sideways, and on. A hub's two side
        /// branches line up across it, so each one's first run only gets half.
        /// </summary>
        public int maxStraightLength = 0;

        /// <summary>轉折時中線往旁邊偏移的格數。How far the centre line steps sideways at a bend.</summary>
        public IntRange jogOffsetRange = new IntRange(18, 30);

        /// <summary>
        /// 迴廊（四條走廊圍成一圈、中庭放房間）的數量。一部分放在主幹上：中庭擋住視線，直線到那裡就斷；其餘有機率接在支道盡頭。
        /// 迴廊整圈跟進來的走廊同一個分區，只在主幹出口設閘門。預設 0 = 不放。
        /// Cloisters (four corridors round a courtyard of rooms). Some sit on the spine, where the courtyard breaks the line
        /// of sight; the rest may cap branch ends. A cloister is the incoming corridor's sector, gated only where the spine
        /// leaves it. 0 (the default) = none.
        /// </summary>
        public IntRange loopCountRange = IntRange.Zero;

        /// <summary>迴廊的邊長（含牆）。A cloister's side length, walls included.</summary>
        public IntRange loopSizeRange = new IntRange(31, 39);

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

        /// <summary>沒有直線長度上限時用的值（夠大、加減不會溢位）。The cap when straight runs are unlimited: big, but safe to add to.</summary>
        private const int NoCap = 100000;

        /// <summary>支道盡頭還有迴廊額度時，真的接一座迴廊的機率。Chance a branch end takes a cloister while any are left.</summary>
        private const float BranchLoopChance = 0.5f;

        /// <summary>生成走廊時共用的狀態。Shared state while the corridors grow.</summary>
        private class Growth
        {
            public CellRect container;
            public int width;
            public ModExtension_VaultLayout ext;
            public VaultLayoutPlan plan;
            /// <summary>單一直線段的長度上限（含牆）。Length cap on one straight run, walls included.</summary>
            public int cap;
            /// <summary>還能接在支道盡頭的迴廊數。Cloisters still available for branch ends.</summary>
            public int loopsLeft;

            public int Clearance => CorridorClearance(ext);
        }

        /// <summary>
        /// 主幹上的樞紐或迴廊。From / To 是它沿主幹佔的範圍（含牆），half 用來算它跟支道、轉折的距離。
        /// A hub or cloister on the spine. From / To is the stretch of spine it takes (walls included); half is used for
        /// how far branches and bends keep from it.
        /// </summary>
        private class SpineNode
        {
            public int centre;
            public int half;
            public int size;
            public bool loop;

            public int From => centre - half;
            public int To => From + size - 1;
        }

        private static void GrowCorridors(CellRect container, int width, ModExtension_VaultLayout ext, VaultLayoutPlan plan)
        {
            Growth g = new Growth
            {
                container = container,
                width = width,
                ext = ext,
                plan = plan,
                cap = ext.maxStraightLength > 0 ? Mathf.Max(ext.maxStraightLength, width * 4) : NoCap
            };

            // 主幹沿較長的軸走，不一定貫穿整個容器。The spine follows the longer axis and needn't span it.
            bool horizontal = container.Width >= container.Height;
            int axisLength = horizontal ? container.Width : container.Height;
            int spineLength = Mathf.Clamp(axisLength * SpineLengthPct.RandomInRange / 100, width * 3, axisLength);
            int spineStart = Rand.RangeInclusive(0, axisLength - spineLength);
            int crossCentre = (horizontal ? container.Height : container.Width) / 2;

            int axisMin = horizontal ? container.minX : container.minZ;
            int crossMin = horizontal ? container.minZ : container.minX;
            int crossMax = horizontal ? container.maxZ : container.maxX;
            int crossAbs = crossMin + crossCentre;
            int spineA = axisMin + spineStart;
            int spineB = spineA + spineLength - 1;

            // 迴廊：一部分放在主幹上當節點，剩下的留給支道盡頭。Cloisters: some sit on the spine as nodes, the rest wait for branch ends.
            int loops = Mathf.Max(0, ext.loopCountRange.RandomInRange);
            int spineLoops = Rand.RangeInclusive(0, loops);
            g.loopsLeft = loops - spineLoops;

            // 節點位置：離主幹兩端與彼此都要夠遠。十字樞紐先放，迴廊只用剩下的空位，才不會把樞紐擠掉；
            // 主幹上放不下的迴廊改留給支道盡頭。
            // Node positions, clear of the spine's ends and each other. The cross hubs go first and cloisters only take
            // what room is left, so they never crowd the hubs out; a cloister that doesn't fit on the spine is left for a
            // branch end instead.
            int hubHalf = ext.hubSize / 2;
            List<SpineNode> nodes = new List<SpineNode>();
            int hubCount = ext.hubCountRange.RandomInRange;
            for (int i = 0; i < hubCount; i++)
            {
                TryPlaceNode(g, nodes, new SpineNode { half = hubHalf, size = hubHalf * 2 + 1 }, spineA, spineB);
            }
            for (int i = 0; i < spineLoops; i++)
            {
                int size = LoopSize(ext, width);
                if (size + width * 2 > crossMax - crossMin + 1
                    || !TryPlaceNode(g, nodes, new SpineNode { half = size / 2, size = size, loop = true }, spineA, spineB))
                {
                    g.loopsLeft++;
                }
            }
            nodes.SortBy(n => n.centre);

            // 主幹的轉折：主幹換線時，節點要能整個落在容器裡。Spine bends: whichever line the spine is on, its nodes must fit the container.
            int reserve = Mathf.Max(width, nodes.Count > 0 ? nodes.Max(n => n.half) : 0) + 1;
            List<(int at, int cross)> bends = PlanSpineBends(g, nodes, spineA, spineB, crossAbs, crossMin + reserve, crossMax - reserve);

            // 主幹：一段走廊、一個節點或轉折、一段走廊……樞紐是前一段的子節點，下一段與兩側支道是樞紐的子節點，
            // 所以樞紐的每一臂都是一個會設閘門的路口；迴廊只在出口設閘門；轉折是內部接口。
            // The spine: corridor, node or bend, corridor, … A hub is a child of the segment before it, and the next
            // segment and the side branches are the hub's children, so every arm of a hub is a gated junction; a cloister
            // is gated only on its way out; a bend is internal.
            List<(int index, int from, int to)> segments = new List<(int, int, int)>();
            int cursor = spineA;
            int parent = -1;
            bool internalNext = false;
            int cross = crossAbs;
            List<(int along, int across, int h, List<CellRect> hubRects)> hubArms = new List<(int, int, int, List<CellRect>)>();
            List<(int at, SpineNode node, int bend)> events = nodes.Select(n => (n.From, n, -1))
                .Concat(bends.Select((b, i) => (b.at, (SpineNode)null, i)))
                .OrderBy(e => e.Item1)
                .ToList();
            foreach ((int at, SpineNode node, int bend) in events)
            {
                int seg = AddCorridor(plan, AxisRect(horizontal, cursor, at, cross, width, container), parent, horizontal, 1, internalNext);
                segments.Add((seg, cursor, at));
                internalNext = false;

                if (node == null)
                {
                    // 轉折：在這段的末端接一段垂直的連接走廊，下一段從連接走廊的外牆線、換到新的線上繼續（見 BendConnector）。
                    // Bend: a perpendicular connector at this segment's end; the next segment carries on from the
                    // connector's outer wall line, on the new line (see BendConnector).
                    int next = bends[bend].cross;
                    CellRect connector = BendConnector(!horizontal, cross, next, width, at - width + 1, at);
                    parent = AddCorridor(plan, connector.ClipInsideRect(container), seg, !horizontal, next > cross ? 1 : -1, true);
                    cursor = at;
                    cross = next;
                    internalNext = true;
                }
                else if (node.loop)
                {
                    parent = AddLoop(g, horizontal, node.From, 1, cross, node.size, seg);
                    cursor = node.To;
                }
                else
                {
                    int h = node.centre;
                    int hubFrom = node.From;
                    int hubTo = node.To;
                    CellRect barAlong = AxisRect(horizontal, hubFrom, hubTo, cross, ext.hubArmWidth, container);
                    CellRect barAcross = AxisRect(!horizontal, cross - hubHalf, cross + hubHalf, h, ext.hubArmWidth, container);
                    int along = AddCorridor(plan, barAlong, seg, horizontal, 1);
                    int across = AddCorridor(plan, barAcross, along, !horizontal, 0, true);

                    hubArms.Add((along, across, h, new List<CellRect> { barAlong, barAcross, plan.corridors[seg].rect }));
                    parent = along;
                    cursor = hubTo;
                }
            }
            int last = AddCorridor(plan, AxisRect(horizontal, cursor, spineB, cross, width, container), parent, horizontal, 1, internalNext);
            segments.Add((last, cursor, spineB));

            // 樞紐兩側的支道：整條主幹（含轉折與迴廊）都放好之後才長，否則子支道可能佔到主幹之後才轉過去的那條線。
            // 樞紐的支道跟樞紐前後兩段主幹在幾何上本來就只隔幾格（斜對角），都經由樞紐相連，不算間距。
            // 兩側支道隔著樞紐在同一條線上，所以各自的第一段只能用一半的長度上限（扣掉樞紐本身）。
            // Hub side branches grow only once the whole spine (bends and cloisters too) is down; otherwise a sub-branch
            // could take a line the spine only bends onto later. A hub branch sits only a few cells diagonally from the
            // spine segments either side of the hub by construction; they're all joined through the hub, so those
            // segments are exempt from the spacing. The two side branches line up across the hub, so each one's first run
            // only gets half the cap, less the hub itself.
            int hubRunCap = g.cap >= NoCap ? NoCap : Mathf.Max(width * 2, (g.cap - ext.hubSize) / 2);
            foreach ((int along, int across, int h, List<CellRect> hubRects) in hubArms)
            {
                CellRect barAcross = plan.corridors[across].rect;
                // 樞紐之後的那段主幹（樞紐橫臂以外、以 along 為母走廊的那一條）。The spine segment after the hub: along's child other than the cross bar.
                int after = plan.corridors.FindIndex(c => c.parent == along && c.rect != barAcross);
                if (after >= 0) hubRects.Add(plan.corridors[after].rect);
                for (int side = -1; side <= 1; side += 2)
                {
                    if (!Rand.Chance(ext.hubBranchChance)) continue;
                    List<int> runs = AddBranch(g, barAcross, across, !horizontal, h, side, hubRects, hubRunCap);
                    GrowSubBranches(g, runs, horizontal);
                }
            }

            // T 字支道：在主幹段上隔開一段距離各長一條，避開節點與轉折，隨機往哪一側。
            // T branches: spaced out along the spine segments, clear of the nodes and bends, each going off one side.
            int branches = ext.branchCountRange.RandomInRange;
            List<int> used = nodes.Select(n => n.centre).Concat(bends.Select(b => b.at)).ToList();
            for (int i = 0; i < branches; i++)
            {
                int pos = -1;
                int segIndex = -1;
                for (int attempt = 0; attempt < 40 && pos < 0; attempt++)
                {
                    (int index, int from, int to) seg = segments.RandomElement();
                    int lo = seg.from + width;
                    int hi = seg.to - width;
                    if (hi < lo) continue;
                    int candidate = Rand.RangeInclusive(lo, hi);
                    if (used.All(u => Mathf.Abs(u - candidate) >= ext.branchSpacing)
                        && nodes.All(n => Mathf.Abs(n.centre - candidate) >= NodeClearance(g, n)))
                    {
                        pos = candidate;
                        segIndex = seg.index;
                    }
                }
                if (pos < 0) break;
                used.Add(pos);

                int branchSide = Rand.Bool ? 1 : -1;
                CellRect parentRect = plan.corridors[segIndex].rect;
                // 視線會穿過母走廊，第一段扣掉它的寬度。The view carries across the parent, so the first run gives up its width.
                List<int> runs = AddBranch(g, parentRect, segIndex, !horizontal, pos, branchSide, new List<CellRect> { parentRect }, g.cap - width);
                GrowSubBranches(g, runs, horizontal);
            }
        }

        /// <summary>
        /// 支道離節點中心的最小距離：半跨度 + 支道半寬 + 走廊間距 + 1 才碰不到節點的橫臂；不小於舊值。
        /// Closest a branch may sit to a node's centre: its half span + the branch's half width + the corridor clearance
        /// + 1 keeps it off the node's bar; never less than the old value.
        /// </summary>
        private static int NodeClearance(Growth g, SpineNode node)
        {
            return Mathf.Max(node.half + g.width + 2, node.half + g.width / 2 + g.Clearance + 1);
        }

        /// <summary>
        /// 在主幹上找個位置放節點：離兩端至少一個走廊寬，跟既有節點的中心至少隔兩者半跨度加兩個走廊寬，
        /// 也不小於 branchSpacing 與 hubSpacing。
        /// Finds the node a place on the spine: a corridor width clear of either end, and from every other node's centre
        /// at least both half spans plus two corridor widths, and never less than branchSpacing or hubSpacing.
        /// </summary>
        private static bool TryPlaceNode(Growth g, List<SpineNode> nodes, SpineNode node, int spineA, int spineB)
        {
            int lo = spineA + node.half + g.width;
            int hi = spineB - (node.size - node.half - 1) - g.width;
            if (hi < lo) return false;

            // 間距拉大後隨機位置常撞到，多試幾次。Wide spacing makes random picks collide more often; try harder.
            for (int attempt = 0; attempt < 60; attempt++)
            {
                int candidate = Rand.RangeInclusive(lo, hi);
                if (nodes.All(o => Mathf.Abs(o.centre - candidate) >= Mathf.Max(node.half + o.half + 1 + g.width * 2, g.ext.branchSpacing, g.ext.hubSpacing)))
                {
                    node.centre = candidate;
                    nodes.Add(node);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 規劃主幹的轉折，讓每一段直線（含穿過樞紐的部分）都不超過長度上限。迴廊中間是房間，視線到那裡就斷，
        /// 所以迴廊之後重新起算；樞紐則是直通的，要算進同一段。轉折偏向放在允許範圍的後段，免得轉太多次；
        /// 連接走廊要離節點夠遠，換過去的線也要讓後面的節點整個落在容器裡。找不到位置就停，那一段就維持原樣。
        /// Plans the spine's bends so no straight run (hubs included, since they're straight through) exceeds the cap.
        /// A cloister has rooms in its middle, so the line of sight breaks there and counting restarts after it. Bends
        /// lean towards the late end of what's allowed so there aren't too many; their connectors keep clear of the nodes,
        /// and the new line must leave room for the nodes further on. Where none fits, planning stops and that run stays.
        /// </summary>
        private static List<(int at, int cross)> PlanSpineBends(Growth g, List<SpineNode> nodes, int spineA, int spineB,
            int cross, int crossLo, int crossHi)
        {
            List<(int, int)> bends = new List<(int, int)>();
            if (g.cap >= NoCap) return bends;

            int w = g.width;
            int minRun = w * 3;
            int margin = g.Clearance + 1;
            int runStart = spineA;
            for (int guard = 0; guard < 32; guard++)
            {
                // 視線會多看進迴廊近端那條走廊的寬度；迴廊之後從遠端那條的內側起算。
                // The view reaches across the cloister's near side; after the cloister it restarts inside the far side.
                SpineNode loop = nodes.FirstOrDefault(n => n.loop && n.From >= runStart);
                int runEnd = loop != null ? loop.From + w - 1 : spineB;
                if (runEnd - runStart + 1 <= g.cap)
                {
                    if (loop == null) break;
                    runStart = loop.To - w + 1;
                    continue;
                }

                // 轉折的連接走廊佔 [at - w + 1, at]。The bend's connector takes [at - w + 1, at].
                int hi = Mathf.Min(runStart + g.cap - 1, (loop?.From ?? spineB) - minRun);
                int lo = runStart + minRun;
                List<int> clear = new List<int>();
                for (int at = lo; at <= hi; at++)
                {
                    if (nodes.All(n => at < n.From - margin || at - w + 1 > n.To + margin)) clear.Add(at);
                }
                if (clear.Count == 0) break;

                List<int> late = clear.Where(at => at >= hi - g.cap / 4).ToList();
                int pick = late.Count > 0 ? late.RandomElement() : clear[clear.Count - 1];
                if (!TryPickBendCross(g, cross, crossLo, crossHi, out int next)) break;

                bends.Add((pick, next));
                cross = next;
                runStart = pick - w + 1;
            }
            return bends;
        }

        /// <summary>轉折往哪一側、偏移多少：jogOffsetRange 隨機，兩側先試隨機的一側。Which way and how far a bend steps: jogOffsetRange, either side in random order.</summary>
        private static bool TryPickBendCross(Growth g, int cross, int lo, int hi, out int next)
        {
            int offset = Mathf.Max(g.ext.jogOffsetRange.RandomInRange, g.width + 2);
            int first = Rand.Bool ? 1 : -1;
            for (int k = 0; k < 2; k++)
            {
                next = cross + (k == 0 ? first : -first) * offset;
                if (next >= lo && next <= hi) return true;
            }
            next = cross;
            return false;
        }

        /// <summary>迴廊的邊長（含牆）：至少留得下中庭，取奇數讓入口置中。A cloister's side, walls included: room for a courtyard, odd so the entrance centres.</summary>
        private static int LoopSize(ModExtension_VaultLayout ext, int width)
        {
            int size = Mathf.Max(ext.loopSizeRange.RandomInRange, width * 2 + 9);
            return size % 2 == 0 ? size + 1 : size;
        }

        /// <summary>
        /// 迴廊：一圈四條走廊圍成的方框，中庭留給房間。近端那條的外牆線落在 near 上（跟進來的走廊共用），往 dir 方向長
        /// size 格，垂直方向以 cross 為中心。四條都是內部接口，整圈跟進來的走廊同一個分區。回傳遠端那條的索引。
        /// A cloister: four corridors round a square, the courtyard left for rooms. The near side's outer wall line sits
        /// on near (shared with the corridor coming in), it reaches size cells towards dir, centred on cross the other
        /// way. All four joints are internal, so the ring is the incoming corridor's sector. Returns the far side's index.
        /// </summary>
        private static int AddLoop(Growth g, bool horizontal, int near, int dir, int cross, int size, int parent)
        {
            int w = g.width;
            int far = near + dir * (size - 1);
            int c0 = cross - size / 2;
            int c1 = c0 + size - 1;
            int nearBar = AddCorridor(g.plan, Limits(!horizontal, c0, c1, near, near + dir * (w - 1)), parent, !horizontal, dir, true);
            // 兩側只長在兩條橫邊的內牆線之間，跟它們各共用一條牆線（T 字接法）；整塊疊在角落會讓原版
            // FinalizeRooms 兩邊都不砌外牆，角落就破了。
            // The sides only run between the two bars' inner wall lines, sharing one wall line with each (a T joint);
            // overlapping whole at the corners makes vanilla's FinalizeRooms skip the outer walls on both, leaving the
            // corners open.
            int innerNear = near + dir * (w - 1);
            int innerFar = far - dir * (w - 1);
            int sideLo = AddCorridor(g.plan, Limits(horizontal, innerNear, innerFar, c0, c0 + w - 1), nearBar, horizontal, dir, true);
            AddCorridor(g.plan, Limits(horizontal, innerNear, innerFar, c1 - w + 1, c1), nearBar, horizontal, dir, true);
            return AddCorridor(g.plan, Limits(!horizontal, c0, c1, far - dir * (w - 1), far), sideLo, !horizontal, dir, true);
        }

        /// <summary>
        /// 在 runs 裡的直線段上長出最多 maxSubBranches 條子支道，同一段上的彼此隔 branchSpacing。
        /// Grows up to maxSubBranches sub-branches off the given straight runs, spaced by branchSpacing on the same run.
        /// </summary>
        private static void GrowSubBranches(Growth g, List<int> runs, bool subHorizontal)
        {
            if (runs.Count == 0) return;
            ModExtension_VaultLayout ext = g.ext;
            int width = g.width;

            // 子支道離直線段兩端的距離：至少一個走廊寬，有分區間隙時還要讓子支道跟母走廊隔開那麼多格。
            // 路口可能在任一端（母走廊或轉折），所以兩端都留。
            // How far a sub-branch keeps from either end of its run: at least a corridor width, and with a sector gap far
            // enough that it clears the parent by that much. The junction (parent or bend) can be at either end; keep
            // both clear.
            int margin = Mathf.Max(width, width / 2 + g.Clearance + 1);
            Dictionary<int, List<int>> used = new Dictionary<int, List<int>>();
            for (int s = 0; s < ext.maxSubBranches; s++)
            {
                if (!Rand.Chance(ext.subBranchChance)) continue;

                int subPos = -1;
                int runIndex = -1;
                for (int attempt = 0; attempt < 10 && subPos < 0; attempt++)
                {
                    int r = runs.RandomElementByWeight(i => Length(g.plan.corridors[i].rect, !subHorizontal));
                    CellRect run = g.plan.corridors[r].rect;
                    int subLength = Length(run, !subHorizontal);
                    int subMin = !subHorizontal ? run.minX : run.minZ;
                    if (SectorGap(ext) > 0 && subLength - margin - 1 < margin) continue;

                    int candidate = subMin + Rand.RangeInclusive(margin, Mathf.Max(margin, subLength - margin - 1));
                    if (!used.TryGetValue(r, out List<int> taken) || taken.All(u => Mathf.Abs(u - candidate) >= ext.branchSpacing))
                    {
                        subPos = candidate;
                        runIndex = r;
                    }
                }
                if (subPos < 0) break;
                if (!used.TryGetValue(runIndex, out List<int> list)) used[runIndex] = list = new List<int>();
                list.Add(subPos);

                int subSide = Rand.Bool ? 1 : -1;
                CellRect runRect = g.plan.corridors[runIndex].rect;
                AddBranch(g, runRect, runIndex, subHorizontal, subPos, subSide, new List<CellRect> { runRect }, g.cap - width);
            }
        }

        private static int Length(CellRect rect, bool horizontal) => horizontal ? rect.Width : rect.Height;

        /// <summary>
        /// 從 parent 在 pos 處往 side 方向長一條支道並加進版面，跟 parent 共用牆線；總長隨機。第一段直線最長 firstRunCap，
        /// 之後每段最長 cap，超過就轉折：在段末接一段垂直的連接走廊，往旁邊偏 jogOffsetRange 格再繼續。第一段接母走廊的
        /// 路口是閘門，轉折都是內部接口。每段在碰到 exclude（母走廊，或整座樞紐）以外的走廊前 clearance 格停下：兩條走廊
        /// 一旦相疊就會併成同一個房間、變成沒有閘門的路口；有分區間隙時，clearance 也讓不同分區的走廊之間留出那道岩層。
        /// 第一段太短就不長。長完之後，還有迴廊額度時有機率在盡頭接一座迴廊。回傳各直線段的索引（子支道長在上面）。
        /// Grows a branch off parent at pos towards side and adds it to the plan, sharing parent's wall line; random total
        /// length. The first straight run is at most firstRunCap and every later one at most cap; past that it bends: a
        /// perpendicular connector at the run's end, a jogOffsetRange step sideways, and on. The first run's joint with
        /// the parent is the gated junction; the bends are internal. Each run stops clearance cells short of any corridor
        /// outside exclude (the parent, or its whole hub), since overlapping corridors merge into one room: a junction
        /// with no gate. With a sector gap, clearance also keeps that band of rock between sectors' corridors. Too short a
        /// first run means no branch. Once grown, a branch end may take a cloister while any are left. Returns the
        /// straight runs' indices (sub-branches grow off them).
        /// </summary>
        private static List<int> AddBranch(Growth g, CellRect parentRect, int parentIndex, bool horizontal, int pos, int side,
            List<CellRect> exclude, int firstRunCap)
        {
            List<int> runs = new List<int>();
            CellRect raw = GrowBranchRaw(g.container, parentRect, horizontal, pos, g.width, side, out int minLength);
            if (raw.IsEmpty) return runs;

            int w = g.width;
            int clearance = g.Clearance;
            int start = horizontal ? (side > 0 ? raw.minX : raw.maxX) : (side > 0 ? raw.minZ : raw.maxZ);
            int end = horizontal ? (side > 0 ? raw.maxX : raw.minX) : (side > 0 ? raw.maxZ : raw.minZ);
            // 自己的各段彼此相疊是正常的，只跟別人比。Own pieces overlap each other by design; only check against the rest.
            List<CellRect> others = g.plan.corridors.Select(c => c.rect).Where(r => !exclude.Contains(r)).ToList();
            List<int> pieces = new List<int>();

            int cursor = start;
            int cross = pos;
            int parent = parentIndex;
            int cap = Mathf.Max(firstRunCap, minLength);
            while (true)
            {
                bool first = runs.Count == 0;
                int min = first ? minLength : w * 2;
                // 長度從視線的起點（轉折後是連接走廊的內牆）算；轉折後的矩形從連接走廊的外牆線起，少了連接走廊那段。
                // Length counts from where the view starts (after a bend, the connector's inside wall); after a bend the
                // rect starts on the connector's outer wall line, short of the connector's own width.
                int skip = first ? 0 : w - 1;
                int length = Mathf.Min(Mathf.Abs(end - cursor) + 1, cap);
                CellRect run = RunRect(horizontal, cursor + side * skip, side, length - skip, cross, w);
                bool shrunk = false;
                while (!run.FullyContainedWithin(g.container) || others.Any(o => o.Overlaps(run.ExpandedBy(clearance))))
                {
                    // 從遠端往回縮一格。Pull the far end back by one.
                    length--;
                    shrunk = true;
                    if (length - skip < min)
                    {
                        run = CellRect.Empty;
                        break;
                    }
                    run = RunRect(horizontal, cursor + side * skip, side, length - skip, cross, w);
                }
                if (run.IsEmpty) break;

                int index = AddCorridor(g.plan, run, parent, horizontal, side, !first);
                runs.Add(index);
                pieces.Add(index);

                int far = cursor + side * (length - 1);
                int nextCursor = far - side * (w - 1);
                // 撞到東西、走到底，或剩下的不夠再長一段，就停。Stop on a collision, at the end, or when too little is left for another run.
                if (shrunk || far == end || Mathf.Abs(end - nextCursor) + 1 < w * 3) break;
                if (!TryBend(g, horizontal, far, side, cross, others, out CellRect connector, out int nextCross)) break;

                parent = AddCorridor(g.plan, connector, index, !horizontal, nextCross > cross ? 1 : -1, true);
                pieces.Add(parent);
                cursor = nextCursor;
                cross = nextCross;
                cap = g.cap;
            }

            if (runs.Count > 0 && g.loopsLeft > 0 && Rand.Chance(BranchLoopChance))
            {
                // 轉折後的矩形比視線短了連接走廊那段，上限跟著扣掉。After a bend the rect is shorter than the view by the connector; so is its cap.
                TryAddBranchLoop(g, horizontal, side, runs[runs.Count - 1], pieces, cross, cap - (runs.Count > 1 ? w - 1 : 0));
            }
            return runs;
        }

        /// <summary>
        /// 支道的轉折：在直線段末端 far 接一段垂直的連接走廊，偏移 jogOffsetRange 格（兩側先試隨機的一側）。
        /// 連接走廊和接下來最短的一段都要落在容器裡、離別的走廊 clearance 格以上。
        /// A branch bend: a perpendicular connector at the run's far end, stepping jogOffsetRange sideways (either side in
        /// random order). The connector and the shortest next run must both fit the container and keep clearance from
        /// every other corridor.
        /// </summary>
        private static bool TryBend(Growth g, bool horizontal, int far, int side, int cross, List<CellRect> others,
            out CellRect connector, out int nextCross)
        {
            int w = g.width;
            int offset = Mathf.Max(g.ext.jogOffsetRange.RandomInRange, w + 2);
            int first = Rand.Bool ? 1 : -1;
            for (int k = 0; k < 2; k++)
            {
                int next = cross + (k == 0 ? first : -first) * offset;
                CellRect conn = BendConnector(!horizontal, cross, next, w, far - side * (w - 1), far);
                CellRect stub = RunRect(horizontal, far, side, w * 2, next, w);
                if (!conn.FullyContainedWithin(g.container) || !stub.FullyContainedWithin(g.container)) continue;
                if (others.Any(o => o.Overlaps(conn.ExpandedBy(g.Clearance)) || o.Overlaps(stub.ExpandedBy(g.Clearance)))) continue;

                connector = conn;
                nextCross = next;
                return true;
            }
            connector = CellRect.Empty;
            nextCross = cross;
            return false;
        }

        /// <summary>
        /// 在支道最後一段的盡頭接一座迴廊；放不下就把那一段往回縮，縮到最短為止。視線會多看進迴廊近端那條走廊，
        /// 所以那一段先讓出它的寬度，才不會超過 runCap（最後一段矩形本身的長度上限）。迴廊整圈要在容器裡，離支道以外的走廊 clearance 格以上。
        /// Puts a cloister on the end of the branch's last run, pulling the run back if it doesn't fit, as far as its
        /// minimum. The view carries on across the cloister's near side, so the run first gives up that width to stay
        /// within runCap (the cap on the last run's own rect). The whole ring must sit in the container and keep clearance from every corridor outside the
        /// branch.
        /// </summary>
        private static void TryAddBranchLoop(Growth g, bool horizontal, int side, int lastRun, List<int> pieces, int cross, int runCap)
        {
            int w = g.width;
            int size = LoopSize(g.ext, w);
            CellRect run = g.plan.corridors[lastRun].rect;
            int runLength = Length(run, horizontal);
            int start = horizontal ? (side > 0 ? run.minX : run.maxX) : (side > 0 ? run.minZ : run.maxZ);
            List<CellRect> others = g.plan.corridors.Where((c, i) => !pieces.Contains(i)).Select(c => c.rect).ToList();

            for (int length = Mathf.Min(runLength, runCap - (w - 1)); length >= w * 2; length -= 2)
            {
                int near = start + side * (length - 1);
                CellRect bounds = Limits(horizontal, near, near + side * (size - 1), cross - size / 2, cross - size / 2 + size - 1);
                if (!bounds.FullyContainedWithin(g.container) || others.Any(o => o.Overlaps(bounds.ExpandedBy(g.Clearance)))) continue;

                if (length != runLength) g.plan.corridors[lastRun].rect = RunRect(horizontal, start, side, length, cross, w);
                AddLoop(g, horizontal, near, side, cross, size, lastRun);
                g.loopsLeft--;
                return;
            }
        }

        private static int AddCorridor(VaultLayoutPlan plan, CellRect rect, int parent, bool horizontal, int side, bool internalJunction = false)
        {
            plan.corridors.Add(new VaultLayoutPlan.Corridor
            {
                rect = rect,
                parent = parent,
                horizontal = horizontal,
                side = side,
                internalJunction = internalJunction
            });
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

        /// <summary>
        /// 轉折的連接走廊：沿自己的軸從舊線那一側的牆線，長到新線遠側的牆線；另一個方向佔 [from, to]（舊線那段的末端）。
        /// 兩端都只跟相接的走廊共用一條牆線（T 字接法），下一段則從它的外牆線接出去。整塊疊在一起的 L 形轉角會讓原版
        /// FinalizeRooms 兩邊都不砌外牆，轉角就破了。horizontal 是連接走廊本身的軸。
        /// A bend's connector: along its own axis from the old line's wall on the side it turns to, out to the new line's far
        /// wall; across, it takes [from, to] (the end of the old run). Each end shares only one wall line with the corridor
        /// it meets (a T joint), and the next run leaves from its outer wall line. An L corner overlapping whole makes
        /// vanilla's FinalizeRooms skip the outer walls on both, leaving the corner open. horizontal is the connector's own
        /// axis.
        /// </summary>
        private static CellRect BendConnector(bool horizontal, int cross, int next, int width, int from, int to)
        {
            int half = width / 2;
            int startLine = next > cross ? cross - half + width - 1 : cross - half;
            int endLine = next > cross ? next - half + width - 1 : next - half;
            return Limits(horizontal, startLine, endLine, from, to);
        }

        /// <summary>
        /// 沿軸兩個座標、垂直方向兩個座標圍出的矩形（順序不拘）。horizontal 表示軸是 x。
        /// The rect between two axis coordinates and two cross coordinates, in any order. horizontal means the axis is x.
        /// </summary>
        private static CellRect Limits(bool horizontal, int axis0, int axis1, int cross0, int cross1)
        {
            int a0 = Mathf.Min(axis0, axis1), a1 = Mathf.Max(axis0, axis1);
            int c0 = Mathf.Min(cross0, cross1), c1 = Mathf.Max(cross0, cross1);
            return horizontal ? CellRect.FromLimits(a0, c0, a1, c1) : CellRect.FromLimits(c0, a0, c1, a1);
        }

        /// <summary>從 from 往 side 方向長 length 格、以 cross 為中心寬 width 的直線段。A straight run length cells from from towards side, width wide centred on cross.</summary>
        private static CellRect RunRect(bool horizontal, int from, int side, int length, int cross, int width)
        {
            return Limits(horizontal, from, from + side * (length - 1), cross - width / 2, cross - width / 2 + width - 1);
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
