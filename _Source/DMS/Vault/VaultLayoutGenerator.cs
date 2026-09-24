using System.Collections.Generic;
using System.Linq;
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
            };
            for (int i = 0; i < occ.corridors.Count; i++)
            {
                HangRooms(occ, occ.corridors[i], occ.corridorSectors[i], ext);
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
            public readonly List<int> roomSectors = new List<int>();
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
        /// 沿某軸、從 start 走 length 格、以 crossCentre 為中心線的走廊矩形。
        /// A corridor rect running along one axis from start for length cells, centred on crossCentre.
        /// </summary>
        private static CellRect MakeCorridor(bool horizontal, int start, int length, int crossCentre, int width, CellRect container)
        {
            int half = width / 2;
            CellRect rect = horizontal
                ? new CellRect(container.minX + start, container.minZ + crossCentre - half, length, width)
                : new CellRect(container.minX + crossCentre - half, container.minZ + start, width, length);
            return rect.ClipInsideRect(container);
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
                bool growsPositive = side > 0;
                if (horizontal)
                {
                    rect = growsPositive ? new CellRect(rect.minX, rect.minZ, rect.Width - 1, rect.Height)
                                         : new CellRect(rect.minX + 1, rect.minZ, rect.Width - 1, rect.Height);
                    if (rect.Width < minLength) return CellRect.Empty;
                }
                else
                {
                    rect = growsPositive ? new CellRect(rect.minX, rect.minZ, rect.Width, rect.Height - 1)
                                         : new CellRect(rect.minX, rect.minZ + 1, rect.Width, rect.Height - 1);
                    if (rect.Height < minLength) return CellRect.Empty;
                }
            }
            return rect;
        }

        private static CellRect GrowBranchRaw(CellRect container, CellRect parent, bool horizontal, int pos, int width, int side,
            out int minLength)
        {
            int half = width / 2;
            minLength = width * 2;

            if (horizontal)
            {
                // 支道水平：pos 是 z，從 parent 的左或右牆線往外長。Horizontal branch: pos is z, grows off parent's left/right wall.
                int startX = side > 0 ? parent.maxX : parent.minX;
                int room = side > 0 ? container.maxX - startX + 1 : startX - container.minX + 1;
                int length = Mathf.Clamp(room * BranchLengthPct.RandomInRange / 100, 0, room);
                if (length < minLength) return CellRect.Empty;
                int minX = side > 0 ? startX : startX - length + 1;
                return new CellRect(minX, pos - half, length, width).ClipInsideRect(container);
            }
            else
            {
                int startZ = side > 0 ? parent.maxZ : parent.minZ;
                int room = side > 0 ? container.maxZ - startZ + 1 : startZ - container.minZ + 1;
                int length = Mathf.Clamp(room * BranchLengthPct.RandomInRange / 100, 0, room);
                if (length < minLength) return CellRect.Empty;
                int minZ = side > 0 ? startZ : startZ - length + 1;
                return new CellRect(pos - half, minZ, width, length).ClipInsideRect(container);
            }
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

                    if (TryPlaceRoom(occ, corridor, sector, horizontal, side, cursor, w, ext.roomDepthRange, out CellRect placed))
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
            occ.roomSectors.Add(sector);
        }

        /// <summary>
        /// 在走廊某側、沿走廊 cursor 起 w 格寬處貼一間房，深度從 depthRange 往下試到放得下為止。
        /// Attaches a room w wide at cursor on one side of the corridor, trying depths from the range
        /// downwards until one fits.
        /// </summary>
        private static bool TryPlaceRoom(Occupancy occ, CellRect corridor, int sector, bool horizontal, int side, int cursor, int w,
            IntRange depthRange, out CellRect placed)
        {
            int depth = depthRange.RandomInRange;
            for (; depth >= depthRange.min; depth--)
            {
                CellRect rect;
                if (horizontal)
                {
                    int minZ = side > 0 ? corridor.maxZ : corridor.minZ - depth + 1;
                    rect = new CellRect(cursor, minZ, w, depth);
                }
                else
                {
                    int minX = side > 0 ? corridor.maxX : corridor.minX - depth + 1;
                    rect = new CellRect(minX, cursor, depth, w);
                }

                if (Fits(occ, rect, sector))
                {
                    placed = rect;
                    return true;
                }
            }

            placed = CellRect.Empty;
            return false;
        }

        private static void TryPlaceEndRoom(Occupancy occ, CellRect corridor, int sector, bool horizontal, int end,
            ModExtension_VaultLayout ext)
        {
            int w = ext.roomWidthRange.RandomInRange;
            int corridorWidth = horizontal ? corridor.Height : corridor.Width;
            w = Mathf.Max(w, corridorWidth);
            int crossMin = (horizontal ? corridor.minZ : corridor.minX) - (w - corridorWidth) / 2;

            int depth = ext.roomDepthRange.RandomInRange;
            for (; depth >= ext.roomDepthRange.min; depth--)
            {
                CellRect rect;
                if (horizontal)
                {
                    int minX = end > 0 ? corridor.maxX : corridor.minX - depth + 1;
                    rect = new CellRect(minX, crossMin, depth, w);
                }
                else
                {
                    int minZ = end > 0 ? corridor.maxZ : corridor.minZ - depth + 1;
                    rect = new CellRect(crossMin, minZ, w, depth);
                }

                if (Fits(occ, rect, sector))
                {
                    AddRoom(occ, rect, sector);
                    return;
                }
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
            for (int i = 0; i < occ.corridors.Count; i++)
            {
                if (Blocks(occ.corridors[i], occ.corridorSectors[i])) return false;
            }
            for (int i = 0; i < occ.rooms.Count; i++)
            {
                if (Blocks(occ.rooms[i], occ.roomSectors[i])) return false;
            }
            return true;

            bool Blocks(CellRect other, int otherSector)
            {
                if (interior.Overlaps(other) || other.ContractedBy(1).Overlaps(rect)) return true;
                return occ.gap > 0 && otherSector != sector && halo.Overlaps(other);
            }
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
