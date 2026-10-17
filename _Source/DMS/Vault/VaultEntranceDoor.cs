using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 電梯廳一定直接貼著走廊，並用捲門接到走廊。
    /// 房型要等版面（含入口檢查點）排完才由原版指派，所以電梯廳可能被派到檢查點後面、或根本沒貼著走廊的房間；
    /// 這時把它跟一間直接貼著走廊的房間互換房型。之後在兩者共用的牆上開一扇捲門（寬的放不下就換窄的），
    /// 同一面牆上原本的門封回牆。都在版面寫進 sketch 之後、房間內容解析之前做，所以原版填房時會把捲門當成門避開。
    ///
    /// The lift lobby always sits right on a corridor and opens onto it through a rolling door.
    /// Vanilla assigns room types only after the layout (checkpoints included) is laid out, so the lobby may land
    /// behind a checkpoint or in a room that doesn't touch a corridor at all; if so, it swaps room types with one
    /// that does. A rolling door then goes in the wall they share (narrower ones if the wide one won't fit), and any
    /// other door on that wall is walled up. All of this runs after the layout is flushed to the sketch and before
    /// room contents are resolved, so vanilla filling treats the rolling door as a door and keeps clear of it.
    /// </summary>
    public static class VaultEntranceDoor
    {
        /// <summary>牆上一段可以開門的連續格子，以及門的朝向。A run of wall cells a door can go in, and the door's rotation.</summary>
        private class Span
        {
            public List<IntVec3> cells;
            public Rot4 rot;
            public LayoutRoom corridor;
        }

        public static void Apply(LayoutStructureSketch sketch, LayoutRoomDef corridorDef, ModExtension_VaultLayout ext)
        {
            StructureLayout layout = sketch.structureLayout;
            LayoutRoom entrance = layout.Rooms.FirstOrDefault(VaultRoomUtility.IsEntrance);
            List<LayoutRoom> corridors = layout.Rooms.Where(r => r.requiredDef == corridorDef).ToList();
            if (entrance == null || corridors.Count == 0) return;

            List<ThingDef> doorDefs = ext?.entranceDoorDefs?.Where(d => d != null).OrderByDescending(d => d.Size.x).ToList()
                ?? new List<ThingDef>();
            int minWidth = doorDefs.Count > 0 ? doorDefs.Min(d => d.Size.x) : 1;

            if (FindSpan(layout, entrance, corridors, minWidth) == null)
            {
                LayoutRoom swap = FindSwapRoom(layout, entrance, corridors, minWidth);
                if (swap == null)
                {
                    Log.Warning("[DMS] No room on a corridor can take the lift lobby; it stays where it is.");
                    return;
                }
                (entrance.defs, swap.defs) = (swap.defs, entrance.defs);
                entrance = swap;
            }

            foreach (ThingDef doorDef in doorDefs)
            {
                Span span = FindSpan(layout, entrance, corridors, doorDef.Size.x);
                if (span == null) continue;
                PlaceDoor(sketch, entrance, span, doorDef);
                return;
            }
        }

        /// <summary>
        /// 換一間直接貼著走廊的房間當電梯廳：不動走廊、檢查點與指定房型的房間；電梯廳的房型要放得進去，
        /// 對方的房型也放得進原本的電梯廳最好，放不進就退而求其次。
        /// Picks a room right on a corridor to become the lobby: never a corridor, a checkpoint or a room with a required
        /// type; the lobby's types must fit it, and it's best if its own types fit the old lobby too (else second best).
        /// </summary>
        private static LayoutRoom FindSwapRoom(StructureLayout layout, LayoutRoom entrance, List<LayoutRoom> corridors, int minWidth)
        {
            List<LayoutRoom> candidates = layout.Rooms
                .Where(r => r != entrance && r.requiredDef == null && !r.defs.NullOrEmpty() && !VaultCheckpointUtility.IsCheckpoint(r))
                .Where(r => entrance.defs.All(d => d.CanResolve(r)))
                .Where(r => FindSpan(layout, r, corridors, minWidth) != null)
                .ToList();
            if (candidates.Count == 0) return null;

            List<LayoutRoom> both = candidates.Where(r => r.defs.All(d => d.CanResolve(entrance))).ToList();
            return (both.Count > 0 ? both : candidates).RandomElement();
        }

        /// <summary>
        /// 房間與走廊共用的牆上，找 width 格連續、內外兩側都是地板、兩端外面是牆的一段。
        /// 有現成的門就優先挑蓋住它的那段，否則挑最靠牆中央的。
        /// On a wall the room shares with a corridor, finds width consecutive cells with floor either side and wall just
        /// past each end. Prefers a run covering an existing door, else the one nearest the middle of the wall.
        /// </summary>
        private static Span FindSpan(StructureLayout layout, LayoutRoom room, List<LayoutRoom> corridors, int width)
        {
            Span best = null;
            float bestScore = float.MaxValue;

            foreach (CellRect rect in room.rects)
            {
                CellRect inside = rect.ContractedBy(1);
                foreach (LayoutRoom corridor in corridors)
                {
                    foreach (CellRect corridorRect in corridor.rects)
                    {
                        CellRect corridorInside = corridorRect.ContractedBy(1);

                        // 每一面牆分開看：同一面牆上的格子共線。Each side separately: cells on one side are collinear.
                        foreach (IGrouping<IntVec3, IntVec3> side in rect.EdgeCells
                                     .Where(c => !rect.IsCorner(c))
                                     .GroupBy(c => VaultRoomUtility.OutwardDirection(rect, c)))
                        {
                            IntVec3 dir = side.Key;
                            bool alongX = dir.z != 0;
                            List<IntVec3> line = side
                                .Where(c => corridorInside.Contains(c + dir) && inside.Contains(c - dir))
                                .OrderBy(c => alongX ? c.x : c.z)
                                .ToList();
                            if (line.Count < width) continue;

                            IntVec3 step = alongX ? IntVec3.East : IntVec3.North;
                            float middle = alongX ? (line[0].x + line[line.Count - 1].x) / 2f : (line[0].z + line[line.Count - 1].z) / 2f;

                            for (int i = 0; i + width <= line.Count; i++)
                            {
                                List<IntVec3> cells = line.GetRange(i, width);
                                if (!IsDoorRun(layout, cells, dir, step)) continue;

                                float centre = alongX ? cells[0].x + (width - 1) / 2f : cells[0].z + (width - 1) / 2f;
                                float score = System.Math.Abs(centre - middle) - (cells.Any(layout.IsDoorAt) ? 1000f : 0f);
                                if (score >= bestScore) continue;

                                bestScore = score;
                                best = new Span { cells = cells, rot = alongX ? Rot4.North : Rot4.East, corridor = corridor };
                            }
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>整段都在牆線上（牆或門）、前後是地板、兩端外面是牆。All wall or door, floor front and back, wall past both ends.</summary>
        private static bool IsDoorRun(StructureLayout layout, List<IntVec3> cells, IntVec3 dir, IntVec3 step)
        {
            foreach (IntVec3 c in cells)
            {
                if (!layout.IsWallAt(c) && !layout.IsDoorAt(c)) return false;
                if (IsSolid(layout, c + dir) || IsSolid(layout, c - dir)) return false;
            }
            return layout.IsWallAt(cells[0] - step) && layout.IsWallAt(cells[cells.Count - 1] + step);
        }

        private static bool IsSolid(StructureLayout layout, IntVec3 c) => layout.IsWallAt(c) || layout.IsDoorAt(c);

        /// <summary>
        /// 蓋上捲門（sketch 會清掉它蓋住的牆與門），同一面牆上其餘的門封回牆，再把電梯廳與走廊接起來。
        /// Lays the rolling door (the sketch wipes the wall and doors under it), walls up any other door on the same wall
        /// and links the lobby to the corridor.
        /// </summary>
        private static void PlaceDoor(LayoutStructureSketch structureSketch, LayoutRoom entrance, Span span, ThingDef doorDef)
        {
            LayoutSketch sketch = structureSketch.layoutSketch;
            StructureLayout layout = structureSketch.structureLayout;
            CellRect target = CellRect.FromLimits(span.cells[0], span.cells[span.cells.Count - 1]);

            IntVec3 pos = span.cells.FirstOrDefault(c => GenAdj.OccupiedRect(c, span.rot, doorDef.Size) == target);
            if (GenAdj.OccupiedRect(pos, span.rot, doorDef.Size) != target)
            {
                Log.Warning($"[DMS] Couldn't line {doorDef.defName} up with the lift lobby's wall at {target}.");
                return;
            }

            // 同一面牆上、捲門以外的門封回牆。Wall up the other doors on that wall.
            IntVec3 dir = VaultRoomUtility.OutwardDirection(entrance.rects.First(r => r.EdgeCells.Contains(span.cells[0])), span.cells[0]);
            // 捲門兩端外面一定是牆（見 IsDoorRun），照它的材質補。The cell past either end is always wall (see IsDoorRun); copy it.
            IntVec3 step = dir.z != 0 ? IntVec3.East : IntVec3.North;
            SketchThing wall = sketch.ThingsAt(span.cells[0] - step).FirstOrDefault(t => t.def.IsWall);
            foreach (SketchThing door in sketch.Things
                         .Where(t => t.def.IsDoor && !target.Overlaps(t.OccupiedRect) && OnSameWall(t.OccupiedRect, span, dir, entrance))
                         .ToList())
            {
                sketch.Remove(door);
                foreach (IntVec3 c in door.OccupiedRect)
                {
                    layout.Add(c, RoomLayoutCellType.Wall);
                    if (wall != null) sketch.AddThing(wall.def, c, Rot4.North, wall.stuff);
                }
            }

            // 捲門 canPlaceOverWall，sketch 不會自己清掉底下的牆，要先拿掉。
            // Rolling doors canPlaceOverWall, so the sketch won't wipe the wall under them by itself; clear it first.
            foreach (SketchThing under in span.cells.SelectMany(sketch.ThingsAt).Where(t => t.def.IsWall || t.def.IsDoor).Distinct().ToList())
            {
                sketch.Remove(under);
            }
            sketch.AddThing(doorDef, pos, span.rot, doorDef.MadeFromStuff ? GenStuff.DefaultStuffFor(doorDef) : null);
            foreach (IntVec3 c in span.cells) layout.Add(c, RoomLayoutCellType.Door);

            if (!entrance.connections.Contains(span.corridor)) entrance.connections.Add(span.corridor);
            if (!span.corridor.connections.Contains(entrance)) span.corridor.connections.Add(entrance);
        }

        /// <summary>這扇門是否在電梯廳與同一段走廊共用的那面牆上。Whether a door is on the wall the lobby shares with that corridor.</summary>
        private static bool OnSameWall(CellRect door, Span span, IntVec3 dir, LayoutRoom entrance)
        {
            IntVec3 first = span.cells[0];
            bool alongX = dir.z != 0;
            foreach (IntVec3 c in door)
            {
                bool sameLine = alongX ? c.z == first.z : c.x == first.x;
                if (!sameLine) return false;
                if (!entrance.rects.Any(r => r.EdgeCells.Contains(c))) return false;
                if (!span.corridor.rects.Any(r => r.ContractedBy(1).Contains(c + dir))) return false;
            }
            return true;
        }
    }
}
