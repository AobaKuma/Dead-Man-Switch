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

        /// <summary>支道再長出子支道的機率。Chance a branch grows its own sub-branch.</summary>
        public float subBranchChance = 0.35f;

        /// <summary>兩條支道在主幹上至少隔幾格。Minimum spacing between branches along the spine.</summary>
        public int branchSpacing = 18;

        /// <summary>相鄰兩間房合併成一間大房的機率。Chance two neighbouring rooms merge into one big room.</summary>
        public float mergeChance = 0.25f;

        /// <summary>沿走廊留空不蓋房的機率，讓輪廓不規則。Chance to leave a gap instead of a room, for a ragged outline.</summary>
        public float gapChance = 0.12f;

        /// <summary>走廊盡頭再放一間房。Put a room at each corridor dead end.</summary>
        public bool endRooms = true;

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
        }

        public List<Corridor> corridors = new List<Corridor>();
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

            VaultLayoutPlan plan = new VaultLayoutPlan();
            GrowCorridors(container, corridorWidth, ext, plan);
            List<CellRect> corridors = plan.corridors.Select(c => c.rect).ToList();

            List<CellRect> rooms = new List<CellRect>();
            foreach (CellRect corridor in corridors)
            {
                HangRooms(container, corridor, corridors, rooms, ext);
            }

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

        // ── 走廊 / Corridors ──────────────────────────────────────────────────

        private static void GrowCorridors(CellRect container, int width, ModExtension_VaultLayout ext, VaultLayoutPlan plan)
        {
            // 主幹沿較長的軸走，不一定貫穿整個容器。The spine follows the longer axis and needn't span it.
            bool horizontal = container.Width >= container.Height;
            int axisLength = horizontal ? container.Width : container.Height;
            int spineLength = Mathf.Clamp(axisLength * SpineLengthPct.RandomInRange / 100, width * 3, axisLength);
            int spineStart = Rand.RangeInclusive(0, axisLength - spineLength);
            int crossCentre = (horizontal ? container.Height : container.Width) / 2;

            CellRect spine = MakeCorridor(horizontal, spineStart, spineLength, crossCentre, width, container);
            plan.corridors.Add(new VaultLayoutPlan.Corridor { rect = spine, parent = -1, horizontal = horizontal });

            // 支道：在主幹上隔開一段距離各長一條，隨機往哪一側。
            // Branches: spaced out along the spine, each going off one side.
            int branches = ext.branchCountRange.RandomInRange;
            List<int> used = new List<int>();
            int axisMin = horizontal ? container.minX : container.minZ;
            int minPos = axisMin + spineStart + width;
            int maxPos = axisMin + spineStart + spineLength - width - 1;

            for (int i = 0; i < branches && maxPos > minPos; i++)
            {
                int pos = -1;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    int candidate = Rand.RangeInclusive(minPos, maxPos);
                    if (used.All(u => Mathf.Abs(u - candidate) >= ext.branchSpacing))
                    {
                        pos = candidate;
                        break;
                    }
                }
                if (pos < 0) break;
                used.Add(pos);

                int branchSide = Rand.Bool ? 1 : -1;
                CellRect branch = GrowBranch(container, spine, !horizontal, pos, width, branchSide);
                if (!branch.IsEmpty)
                {
                    int branchIndex = plan.corridors.Count;
                    plan.corridors.Add(new VaultLayoutPlan.Corridor { rect = branch, parent = 0, horizontal = !horizontal, side = branchSide });

                    if (Rand.Chance(ext.subBranchChance))
                    {
                        int subLength = !horizontal ? branch.Height : branch.Width;
                        int subPos = (!horizontal ? branch.minZ : branch.minX) + Rand.RangeInclusive(width, Mathf.Max(width, subLength - width - 1));
                        int subSide = Rand.Bool ? 1 : -1;
                        CellRect sub = GrowBranch(container, branch, horizontal, subPos, width, subSide);
                        if (!sub.IsEmpty)
                        {
                            plan.corridors.Add(new VaultLayoutPlan.Corridor { rect = sub, parent = branchIndex, horizontal = horizontal, side = subSide });
                        }
                    }
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
        /// Grows a branch off parent at pos towards side, sharing parent's wall line. Random length;
        /// too short to be worth it returns empty.
        /// </summary>
        private static CellRect GrowBranch(CellRect container, CellRect parent, bool horizontal, int pos, int width, int side)
        {
            int half = width / 2;
            int minLength = width * 2;

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

        private static void HangRooms(CellRect container, CellRect corridor, List<CellRect> corridors, List<CellRect> rooms,
            ModExtension_VaultLayout ext)
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

                    if (TryPlaceRoom(container, corridor, horizontal, side, cursor, w, ext.roomDepthRange, corridors, rooms, out CellRect placed))
                    {
                        rooms.Add(placed);
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
                    TryPlaceEndRoom(container, corridor, horizontal, end, ext, corridors, rooms);
                }
            }
        }

        /// <summary>
        /// 在走廊某側、沿走廊 cursor 起 w 格寬處貼一間房，深度從 depthRange 往下試到放得下為止。
        /// Attaches a room w wide at cursor on one side of the corridor, trying depths from the range
        /// downwards until one fits.
        /// </summary>
        private static bool TryPlaceRoom(CellRect container, CellRect corridor, bool horizontal, int side, int cursor, int w,
            IntRange depthRange, List<CellRect> corridors, List<CellRect> rooms, out CellRect placed)
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

                if (Fits(container, rect, corridors, rooms))
                {
                    placed = rect;
                    return true;
                }
            }

            placed = CellRect.Empty;
            return false;
        }

        private static void TryPlaceEndRoom(CellRect container, CellRect corridor, bool horizontal, int end,
            ModExtension_VaultLayout ext, List<CellRect> corridors, List<CellRect> rooms)
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

                if (Fits(container, rect, corridors, rooms))
                {
                    rooms.Add(rect);
                    return;
                }
            }
        }

        /// <summary>
        /// 房間要整個在容器裡，而且內部（去掉牆）不能碰到任何既有矩形，既有矩形的內部也不能碰到它；
        /// 牆線可以共用，房間本身不能疊。
        /// The room must sit inside the container, and its interior may not touch any existing rect,
        /// nor may any existing interior touch it; walls may be shared, rooms may not overlap.
        /// </summary>
        private static bool Fits(CellRect container, CellRect rect, List<CellRect> corridors, List<CellRect> rooms)
        {
            if (rect.minX < container.minX || rect.minZ < container.minZ || rect.maxX > container.maxX || rect.maxZ > container.maxZ)
            {
                return false;
            }

            CellRect interior = rect.ContractedBy(1);
            foreach (CellRect other in corridors.Concat(rooms))
            {
                if (interior.Overlaps(other) || other.ContractedBy(1).Overlaps(rect)) return false;
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
