using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 分區閘門：在每條支道跟母走廊的路口封一整排密封門（各種寬度拼滿走廊），旁邊放一台控制台。
    /// 控制台放在「從入口走得到」的那一側，順著走廊樹判斷：入口所在的子樹那側就是可及側。
    /// 這樣玩家永遠能從自己所在的分區一路駭出去，不會被封死。
    ///
    /// Sector gates: a full row of sealed doors (assorted widths, mixed to fill the corridor) where each
    /// branch meets its parent, with a console beside it. The console sits on whichever side the
    /// entrance can reach, decided on the corridor tree: the side whose subtree holds the entrance.
    /// The player can therefore always hack outward from wherever they start; nothing is ever sealed
    /// off for good.
    /// </summary>
    public static class VaultSectorGates
    {
        public static void SpawnGates(StructureLayout layout, VaultLayoutPlan plan, Map map, Faction faction, ModExtension_VaultLayout ext)
        {
            if (plan == null || ext == null || !ext.sectorGates) return;
            if (ext.gateConsoleDef == null || ext.gateDoorDefs.NullOrEmpty()) return;

            if (!ext.gateDoorDefs.Any(d => d.Size.x == 1))
            {
                Log.ErrorOnce("[DMS] ModExtension_VaultLayout.gateDoorDefs needs a 1-wide door or some corridor widths cannot be sealed.", 0x4A7E);
                return;
            }

            int entranceCorridor = FindEntranceCorridor(layout, plan);

            for (int i = 0; i < plan.corridors.Count; i++)
            {
                VaultLayoutPlan.Corridor child = plan.corridors[i];
                if (child.parent < 0 || !Rand.Chance(ext.gateChance)) continue;

                // 入口在這條支道底下的話，控制台要放在支道那一側。
                // If the entrance hangs somewhere under this branch, the console goes on the branch side.
                bool consoleOnChildSide = IsInSubtree(plan, entranceCorridor, i);
                TrySpawnGate(map, child, plan.corridors[child.parent], consoleOnChildSide, faction, ext);
            }
        }

        // ── 走廊樹 / Corridor tree ────────────────────────────────────────────

        /// <summary>入口房掛在哪一段走廊上（共用牆線就算）。Which corridor the entrance room hangs off (sharing a wall line counts).</summary>
        private static int FindEntranceCorridor(StructureLayout layout, VaultLayoutPlan plan)
        {
            LayoutRoom entrance = layout.Rooms.FirstOrDefault(r =>
                r.defs != null && r.defs.Any(d => d.roomContentsWorkerType == typeof(RoomContents_VaultEntrance)));
            if (entrance == null) return 0;

            int best = 0;
            int bestScore = -1;
            for (int i = 0; i < plan.corridors.Count; i++)
            {
                CellRect corridor = plan.corridors[i].rect;
                int score = entrance.rects.Sum(r => r.Overlaps(corridor) ? r.GetAdjacencyScore(corridor) : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>node 是否在 root 的子樹裡（含 root 本身）。Whether node lies in root's subtree, root included.</summary>
        private static bool IsInSubtree(VaultLayoutPlan plan, int node, int root)
        {
            for (int guard = 0; node >= 0 && guard < 32; guard++)
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

            // 路口線：支道跟母走廊共用的那條牆線，開口的部分就是要封的格子。
            // The junction line: the wall line the branch shares with its parent; the opening is what gets sealed.
            int junction = child.horizontal
                ? (child.side > 0 ? rect.minX : rect.maxX)
                : (child.side > 0 ? rect.minZ : rect.maxZ);

            List<IntVec3> cells = new List<IntVec3>();
            if (alongX)
            {
                for (int x = rect.minX + 1; x <= rect.maxX - 1; x++) cells.Add(new IntVec3(x, 0, junction));
            }
            else
            {
                for (int z = rect.minZ + 1; z <= rect.maxZ - 1; z++) cells.Add(new IntVec3(junction, 0, z));
            }

            // 路口應該是開口；有實牆擋著就不是我們要的地方。The junction should be open; solid wall means it isn't.
            foreach (IntVec3 c in cells)
            {
                if (!c.InBounds(map)) return;
                Building edifice = c.GetEdifice(map);
                if (edifice != null && !edifice.def.IsDoor && edifice.def.Fillage == FillCategory.Full) return;
            }

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

            // 各種寬度隨機拼滿整排。Fill the row with doors of assorted widths.
            Rot4 doorRot = alongX ? Rot4.North : Rot4.East;
            int index = 0;
            while (index < cells.Count)
            {
                int remaining = cells.Count - index;
                ThingDef doorDef = ext.gateDoorDefs.Where(d => d.Size.x <= remaining).RandomElement();
                int width = doorDef.Size.x;

                CellRect target = CellRect.FromLimits(cells[index], cells[index + width - 1]);
                IntVec3 pos = FindSpawnPosition(target, doorRot, doorDef.Size);
                Thing door = GenSpawn.Spawn(ThingMaker.MakeThing(doorDef), pos, map, doorRot);
                if (!comp.Link(door))
                {
                    Log.ErrorOnce($"[DMS] {doorDef.defName} is not an access-link door; the gate will never open.", 0x4A80);
                }

                index += width;
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
        /// 控制台貼牆放在閘門旁：可及側是母走廊就放在開口兩旁、母走廊牆下；是支道就放在支道兩側牆邊、路口內一格。
        /// 面朝離開牆的方向，這樣互動格才在走廊裡。
        /// The console hugs a wall next to the gate: on the parent side it goes just past either end of the
        /// opening under the parent's wall; on the branch side it goes against either side wall one cell in.
        /// It faces away from the wall so its interaction cell is in the corridor.
        /// </summary>
        private static bool TryFindConsoleSpot(Map map, List<IntVec3> gateCells, IntVec3 rowStep, IntVec3 toChild, bool childSide,
            out IntVec3 cell, out Rot4 rot)
        {
            IntVec3 first = gateCells[0];
            IntVec3 last = gateCells[gateCells.Count - 1];
            List<(IntVec3 cell, IntVec3 wallDir)> candidates = new List<(IntVec3, IntVec3)>();

            if (childSide)
            {
                for (int depth = 1; depth <= 3; depth++)
                {
                    candidates.Add((first + toChild * depth, -rowStep));
                    candidates.Add((last + toChild * depth, rowStep));
                }
            }
            else
            {
                for (int offset = 1; offset <= 3; offset++)
                {
                    candidates.Add((first - rowStep * offset - toChild, toChild));
                    candidates.Add((last + rowStep * offset - toChild, toChild));
                }
            }

            foreach ((IntVec3 c, IntVec3 wallDir) in candidates.InRandomOrder())
            {
                IntVec3 wall = c + wallDir;
                if (!c.InBounds(map) || !wall.InBounds(map)) continue;

                Building edifice = wall.GetEdifice(map);
                if (edifice == null || edifice.def.IsDoor || edifice.def.Fillage != FillCategory.Full) continue;
                if (c.GetEdifice(map) != null || c.GetThingList(map).Any(t => t.def.category == ThingCategory.Building && !t.def.building.isPowerConduit)) continue;
                if (!c.Standable(map)) continue;

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
