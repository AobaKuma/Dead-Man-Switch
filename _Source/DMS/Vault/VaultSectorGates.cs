using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 分區閘門：在每條支道跟母走廊的路口封一整排，旁邊放一台控制台。設了 gateDoorDef 就是單扇門（例如 1x3）
    /// 置中、兩側補強化牆；沒設則用各種寬度的密封門拼滿走廊。
    /// 控制台放在「從入口走得到」的那一側，順著走廊樹判斷：入口所在的子樹那側就是可及側。
    /// 這樣玩家永遠能從自己所在的分區一路駭出去，不會被封死。
    ///
    /// Sector gates: a row across the junction where each branch meets its parent, with a console beside it.
    /// With gateDoorDef set the row is one door (e.g. the 1x3) centred between reinforced walls; otherwise it's
    /// sealed doors of assorted widths mixed to fill the corridor. The console sits on whichever side the
    /// entrance can reach, decided on the corridor tree: the side whose subtree holds the entrance.
    /// The player can therefore always hack outward from wherever they start; nothing is ever sealed
    /// off for good.
    /// </summary>
    public static class VaultSectorGates
    {
        public static void SpawnGates(StructureLayout layout, VaultLayoutPlan plan, Map map, Faction faction, ModExtension_VaultLayout ext)
        {
            if (plan == null || ext == null || !ext.sectorGates || ext.gateConsoleDef == null) return;

            if (ext.gateDoorDef != null && ext.gateWallDef == null)
            {
                Log.ErrorOnce("[DMS] ModExtension_VaultLayout.gateDoorDef needs a gateWallDef to fill the rest of the junction.", 0x4A81);
                return;
            }
            if (ext.gateDoorDef == null)
            {
                if (ext.gateDoorDefs.NullOrEmpty()) return;
                if (!ext.gateDoorDefs.Any(d => d.Size.x == 1))
                {
                    Log.ErrorOnce("[DMS] ModExtension_VaultLayout.gateDoorDefs needs a 1-wide door or some corridor widths cannot be sealed.", 0x4A7E);
                    return;
                }
            }

            int entranceCorridor = FindEntranceCorridor(layout, plan);

            for (int i = 0; i < plan.corridors.Count; i++)
            {
                VaultLayoutPlan.Corridor child = plan.corridors[i];
                // 樞紐兩臂之間是內部接口，不設閘門。The joint between a hub's two bars is internal: no gate.
                if (child.parent < 0 || child.internalJunction || !Rand.Chance(ext.gateChance)) continue;

                // 入口在這條支道底下的話，控制台要放在支道那一側。
                // If the entrance hangs somewhere under this branch, the console goes on the branch side.
                bool consoleOnChildSide = IsInSubtree(plan, entranceCorridor, i);
                TrySpawnGate(map, child, plan.corridors[child.parent], consoleOnChildSide, faction, ext);
            }
        }

        // ── 走廊樹 / Corridor tree ────────────────────────────────────────────

        /// <summary>
        /// 入口房掛在哪一段走廊上（共用牆線就算）。入口前面夾了檢查點的話，入口房被推離走廊、碰不到任何走廊，
        /// 改看跟它相連的檢查點貼著哪段走廊；不然會退回主幹，支道上的入口就會被閘門關在控制台的另一邊。
        /// Which corridor the entrance room hangs off (sharing a wall line counts). With a checkpoint in front, the
        /// entrance is pushed off the corridor and touches none, so look at the checkpoint connected to it instead;
        /// otherwise this falls back to the spine and an entrance on a branch ends up behind a gate whose console is
        /// on the far side.
        /// </summary>
        private static int FindEntranceCorridor(StructureLayout layout, VaultLayoutPlan plan)
        {
            LayoutRoom entrance = layout.Rooms.FirstOrDefault(VaultRoomUtility.IsEntrance);
            if (entrance == null) return 0;

            // 檢查點的版面房間已由原版移到地圖座標（plan.checkpoints 的 rect 沒有）。
            // The checkpoints' layout rooms were moved into map space by vanilla (plan.checkpoints' rects weren't).
            List<CellRect> rects = new List<CellRect>(entrance.rects);
            if (entrance.connections != null)
            {
                rects.AddRange(entrance.connections.Where(VaultCheckpointUtility.IsCheckpoint).SelectMany(r => r.rects));
            }

            int best = 0;
            int bestScore = -1;
            for (int i = 0; i < plan.corridors.Count; i++)
            {
                CellRect corridor = plan.corridors[i].rect;
                int score = rects.Sum(r => SharedWallLength(r, corridor));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// 房間牆上有幾格正對著走廊內部，也就是兩者共用的牆長。原版 GetAdjacencyScore 在兩個矩形重疊時一律回 0，
        /// 而共用牆線的房間與走廊一定重疊一格，所以不能用它。
        /// How many of the room's wall cells face straight into the corridor, i.e. the length of wall they share.
        /// Vanilla GetAdjacencyScore returns 0 whenever the rects overlap, and a room sharing a wall line with a
        /// corridor always overlaps it by one cell, so it can't be used here.
        /// </summary>
        private static int SharedWallLength(CellRect room, CellRect corridor)
        {
            CellRect inside = corridor.ContractedBy(1);
            return room.EdgeCells.Count(c => !room.IsCorner(c) && inside.Contains(c + VaultRoomUtility.OutwardDirection(room, c)));
        }

        /// <summary>node 是否在 root 的子樹裡（含 root 本身）。Whether node lies in root's subtree, root included.</summary>
        private static bool IsInSubtree(VaultLayoutPlan plan, int node, int root)
        {
            for (int guard = 0; node >= 0 && guard < 256; guard++)
            {
                if (node == root) return true;
                node = plan.corridors[node].parent;
            }
            return false;
        }

        // ── 閘門 / Gate ───────────────────────────────────────────────────────

        private static void TrySpawnGate(Map map, VaultLayoutPlan.Corridor child, VaultLayoutPlan.Corridor parent,
            bool consoleOnChildSide, Faction faction, ModExtension_VaultLayout ext)
        {
            CellRect rect = child.rect;
            bool alongX = !child.horizontal; // 支道垂直 → 路口那排門沿 x 排。Vertical branch → the gate row runs along x.

            // 路口線：支道跟母走廊共用的那條牆線，開口的部分就是要封的格子。開口是兩者內部寬度重疊的那一段：
            // 走廊接到較寬的樞紐（或反過來）時，只有較窄那一邊的寬度是通的。
            // The junction line: the wall line the branch shares with its parent; the opening is what gets sealed.
            // The opening is where the two interiors overlap: where a corridor meets a wider hub (or the other way
            // round), only the narrower one's width is open.
            int junction = child.horizontal
                ? (child.side > 0 ? rect.minX : rect.maxX)
                : (child.side > 0 ? rect.minZ : rect.maxZ);

            CellRect parentRect = parent.rect;
            int lo = alongX ? Mathf.Max(rect.minX, parentRect.minX) + 1 : Mathf.Max(rect.minZ, parentRect.minZ) + 1;
            int hi = alongX ? Mathf.Min(rect.maxX, parentRect.maxX) - 1 : Mathf.Min(rect.maxZ, parentRect.maxZ) - 1;
            List<IntVec3> cells = new List<IntVec3>();
            for (int i = lo; i <= hi; i++) cells.Add(alongX ? new IntVec3(i, 0, junction) : new IntVec3(junction, 0, i));
            if (cells.Count == 0) return;

            // 路口應該是開口；有實牆擋著就不是我們要的地方。The junction should be open; solid wall means it isn't.
            if (cells.Any(c => !c.InBounds(map) || VaultRoomUtility.IsSolidWall(map, c))) return;

            // 單扇門模式下路口要至少跟門一樣寬。In single-door mode the junction has to be at least as wide as the door.
            if (ext.gateDoorDef != null && ext.gateDoorDef.Size.x > cells.Count) return;

            // 控制台先找位置，找不到就不封門。Find the console a home first; no console, no gate.
            IntVec3 axisStep = alongX ? IntVec3.North : IntVec3.East;      // 沿支道軸的一步 / one step along the branch axis
            IntVec3 rowStep = alongX ? IntVec3.East : IntVec3.North;       // 沿閘門排的一步 / one step along the gate row
            IntVec3 toChild = axisStep * child.side;
            if (!TryFindConsoleSpot(map, cells, rowStep, toChild, consoleOnChildSide, out IntVec3 consoleCell, out Rot4 consoleRot))
            {
                return;
            }

            Thing console = VaultRoomUtility.SpawnSecurity(ext.gateConsoleDef, consoleCell, map, consoleRot, faction);
            CompVaultConsole comp = console.TryGetComp<CompVaultConsole>();
            if (comp == null)
            {
                Log.ErrorOnce($"[DMS] {ext.gateConsoleDef.defName} has no CompVaultConsole; sector gates disabled.", 0x4A7F);
                console.Destroy();
                return;
            }

            ClearGateCells(map, cells);

            Rot4 doorRot = alongX ? Rot4.North : Rot4.East;
            if (ext.gateDoorDef != null)
            {
                // 單扇門置中，兩側補牆（寬度差為奇數時多出的那格補在後側）。
                // One door in the middle, wall either side (an odd leftover cell goes on the far side).
                int width = ext.gateDoorDef.Size.x;
                int start = (cells.Count - width) / 2;
                for (int i = 0; i < cells.Count; i++)
                {
                    if (i >= start && i < start + width) continue;
                    GenSpawn.Spawn(VaultRoomUtility.MakeThing(ext.gateWallDef), cells[i], map, WipeMode.Vanish);
                }
                SpawnGateDoor(map, ext.gateDoorDef, cells, start, doorRot, comp);
                return;
            }

            // 各種寬度隨機拼滿整排。Fill the row with doors of assorted widths.
            int index = 0;
            while (index < cells.Count)
            {
                int remaining = cells.Count - index;
                ThingDef doorDef = ext.gateDoorDefs.Where(d => d.Size.x <= remaining).RandomElement();
                SpawnGateDoor(map, doorDef, cells, index, doorRot, comp);
                index += doorDef.Size.x;
            }
        }

        /// <summary>在閘門排 index 起放一扇門並連到控制台。Spawns one gate door starting at index and links it to the console.</summary>
        private static void SpawnGateDoor(Map map, ThingDef doorDef, List<IntVec3> cells, int index, Rot4 doorRot, CompVaultConsole comp)
        {
            CellRect target = CellRect.FromLimits(cells[index], cells[index + doorDef.Size.x - 1]);
            IntVec3 pos = FindSpawnPosition(target, doorRot, doorDef.Size);
            Thing door = GenSpawn.Spawn(ThingMaker.MakeThing(doorDef), pos, map, doorRot);
            if (!comp.Link(door))
            {
                Log.ErrorOnce($"[DMS] {doorDef.defName} is not an access-link door; the gate will never open.", 0x4A80);
            }
        }

        /// <summary>閘門格上的裝飾品清掉，電纜留著。Clear dressing off the gate cells; conduits stay.</summary>
        private static void ClearGateCells(Map map, List<IntVec3> cells)
        {
            foreach (IntVec3 cell in cells)
            {
                List<Thing> things = cell.GetThingList(map).ToList();
                foreach (Thing thing in things)
                {
                    if (thing.def.category == ThingCategory.Building && !thing.def.building.isPowerConduit
                        || thing.def.category == ThingCategory.Item)
                    {
                        thing.Destroy();
                    }
                }
            }
        }

        /// <summary>
        /// 找一個能放 size 大小、朝 rot、恰好蓋住 target 的生成座標。
        /// Finds the spawn position whose occupied rect at rot equals target.
        /// </summary>
        private static IntVec3 FindSpawnPosition(CellRect target, Rot4 rot, IntVec2 size)
        {
            foreach (IntVec3 cell in target)
            {
                if (GenAdj.OccupiedRect(cell, rot, size) == target) return cell;
            }
            return target.CenterCell;
        }

        /// <summary>
        /// 控制台貼牆放在閘門的可及側，兩種位置都試：開口兩端往內幾格、貼著側牆；或開口兩旁、貼著路口那道牆。
        /// 走廊接走廊時只有其中一種有牆可貼（母走廊側是路口牆，支道側是側牆），接到較寬的樞紐時兩種都可能。
        /// 面朝離開牆的方向，這樣互動格才在走廊裡。
        /// The console hugs a wall on the reachable side of the gate, trying both kinds of spot: a few cells in from
        /// either end of the opening against the side wall, or just past either end of the opening against the
        /// junction wall. Corridor-to-corridor only one kind has a wall (the junction wall on the parent side, the
        /// side walls on the branch side); against a wider hub either may. It faces away from the wall so its
        /// interaction cell is in the corridor.
        /// </summary>
        private static bool TryFindConsoleSpot(Map map, List<IntVec3> gateCells, IntVec3 rowStep, IntVec3 toChild, bool childSide,
            out IntVec3 cell, out Rot4 rot)
        {
            IntVec3 first = gateCells[0];
            IntVec3 last = gateCells[gateCells.Count - 1];
            List<(IntVec3 cell, IntVec3 wallDir)> candidates = new List<(IntVec3, IntVec3)>();

            IntVec3 inward = childSide ? toChild : -toChild;
            for (int depth = 1; depth <= 3; depth++)
            {
                candidates.Add((first + inward * depth, -rowStep));
                candidates.Add((last + inward * depth, rowStep));
            }
            for (int offset = 1; offset <= 3; offset++)
            {
                candidates.Add((first - rowStep * offset + inward, -inward));
                candidates.Add((last + rowStep * offset + inward, -inward));
            }

            foreach ((IntVec3 c, IntVec3 wallDir) in candidates.InRandomOrder())
            {
                if (!VaultRoomUtility.IsSolidWall(map, c + wallDir) || !VaultRoomUtility.IsClearFloor(map, c)) continue;

                cell = c;
                rot = Rot4.FromIntVec3(-wallDir);
                return true;
            }

            cell = IntVec3.Invalid;
            rot = Rot4.North;
            return false;
        }
    }
}
