using System.Collections.Generic;
using System.Linq;
using Fortified.Structures;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在入口檢查點的 LayoutRoomDef 上：可以蓋的 FFF 結構（變體），依各自的 baseWeight 抽一張。
    /// 每張的 z = 0 那一面朝走廊、z 最大那一面接房間；size 的 x 沿走廊、z 是深度（含牆），所有變體要一樣大。
    /// 牆上沒有牆的格子就是開口：可以放門、放別的東西（例如鐵絲網），或留空直接相通。
    /// On the entrance checkpoint's LayoutRoomDef: the FFF structures (variants) it may stamp, one picked by baseWeight.
    /// Each has its z = 0 side facing the corridor and its far z side meeting the room; size.x runs along the corridor
    /// and size.z is the depth (walls included), the same for every variant. Any wall cell without a wall is an
    /// opening: it may hold a door, something else (barbed wire, say), or nothing at all.
    /// </summary>
    public class ModExtension_VaultCheckpoint : DefModExtension
    {
        public List<FFF_StructureDef> structures = new List<FFF_StructureDef>();

        // 貨架與書櫃的內容物，交給 FFF 的 Task_FillStorage（欄位意義相同）。
        // Shelf and bookcase contents, handed to FFF's Task_FillStorage (same meanings).

        /// <summary>貨架上的補給；null = 貨架不放東西。Supplies for the shelves; null = shelves stay empty.</summary>
        public ThingSetMakerDef shelfLoot;
        public IntRange shelfBatches = IntRange.One;
        /// <summary>每座貨架與書櫃各自參與的機率。Chance each shelf and bookcase is stocked.</summary>
        public float fillChance = 1f;
        /// <summary>每座書櫃放幾本隨機的書；0 = 不放。Random books per bookcase; 0 = none.</summary>
        public IntRange booksPerBookcase = IntRange.Zero;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (structures.NullOrEmpty())
            {
                yield return "ModExtension_VaultCheckpoint needs at least one structure.";
                yield break;
            }
            IntVec2 size = structures[0].size;
            foreach (FFF_StructureDef s in structures.Where(s => s.size != size))
            {
                yield return $"checkpoint structure {s.defName} is {s.size}, but the first one is {size}; they must all match.";
            }
        }
    }

    /// <summary>
    /// 入口檢查點：版面產生時把房間推離走廊、中間夾一間檢查點（<see cref="VaultLayoutGenerator"/>）。
    /// 變體要等房型定了才能挑（獎勵房前面不能是沒門的開口），所以版面階段只在前後牆正中各開一扇佔位門，
    /// 讓原版把房間連起來；填充時再依抽到的變體把四面牆改寫成它的牆、門與開口。
    /// Entrance checkpoints: the layout generator pushes a room off its corridor with a checkpoint in between
    /// (<see cref="VaultLayoutGenerator"/>). The variant can only be picked once room types are known (a treasury can't
    /// sit behind a doorless opening), so the layout just opens a placeholder door mid front and mid back to get the
    /// rooms connected, and filling rewrites the walls to the chosen variant's walls, doors and openings.
    /// </summary>
    public static class VaultCheckpointUtility
    {
        private enum EdgeKind { Wall, Door, Opening }

        public static ModExtension_VaultCheckpoint ExtOf(LayoutRoomDef def) => def?.GetModExtension<ModExtension_VaultCheckpoint>();

        /// <summary>檢查點的尺寸（x 沿走廊、z 深度），沒設就是 null。The checkpoint size (x along the corridor, z deep); null when unset.</summary>
        public static IntVec2? SizeOf(LayoutRoomDef def)
        {
            List<FFF_StructureDef> structures = ExtOf(def)?.structures;
            return structures.NullOrEmpty() ? (IntVec2?)null : structures[0].size;
        }

        /// <summary>這間版面房間是不是入口檢查點。Whether a layout room is an entrance checkpoint.</summary>
        public static bool IsCheckpoint(LayoutRoom room)
        {
            return VaultRoomUtility.HasWorker(room, typeof(RoomContents_VaultCheckpoint))
                || room?.requiredDef?.roomContentsWorkerType == typeof(RoomContents_VaultCheckpoint);
        }

        // ── 前後牆 / Front and back ──────────────────────────────────────────

        /// <summary>朝走廊的那面牆正中的格子。The middle cell of the wall facing the corridor.</summary>
        public static IntVec3 FrontMid(CellRect rect, Rot4 rot) => MidOnEdge(rect, rot);

        /// <summary>接房間的那面牆正中的格子。The middle cell of the wall meeting the room.</summary>
        public static IntVec3 BackMid(CellRect rect, Rot4 rot) => MidOnEdge(rect, rot.Opposite);

        /// <summary>
        /// 朝 rot 長出去的檢查點，貼著走廊的那面在 rot 的反方向：北 = 從走廊往 +z 長，前牆是 minZ 那排。
        /// For a checkpoint grown towards rot, the corridor side is the opposite way: North = grown towards +z off the
        /// corridor, front wall on the minZ row.
        /// </summary>
        private static IntVec3 MidOnEdge(CellRect rect, Rot4 grownTowards)
        {
            IntVec3 c = rect.CenterCell;
            switch (grownTowards.AsInt)
            {
                case 0: return new IntVec3(c.x, 0, rect.minZ);
                case 1: return new IntVec3(rect.minX, 0, c.z);
                case 2: return new IntVec3(c.x, 0, rect.maxZ);
                default: return new IntVec3(rect.maxX, 0, c.z);
            }
        }

        private static LayoutRoom RoomAcross(StructureLayout layout, LayoutRoom from, CellRect rect, IntVec3 edge)
        {
            IntVec3 outside = edge + VaultRoomUtility.OutwardDirection(rect, edge);
            return layout.Rooms.FirstOrDefault(r => r != from && r.rects.Any(x => x.ContractedBy(1).Contains(outside)));
        }

        // ── 版面階段 / Layout stage ──────────────────────────────────────────

        /// <summary>
        /// 原版開完門之後，把每座檢查點牆上的門全部封回牆，只在前後牆正中各開一扇佔位門（一扇通走廊、一扇通房間），
        /// 並重新連好房間之間的關係；真正的牆、門與開口在填充時照變體改寫。側牆上原版開給隔壁房間的門也一併封掉，
        /// 所以從走廊進後面的房間只能穿過檢查點（房間彼此之間原本就可能有門）；因此失去唯一出口的隔壁房間，
        /// 會交給之後的 EnsureCorridorDoors 補一扇通走廊的門。
        /// Once vanilla has placed its doors, seal every checkpoint's back to wall and open just a placeholder door mid
        /// front and mid back (one onto the corridor, one into the room), rewiring the room connections; the real walls,
        /// doors and openings are rewritten from the variant at fill time. Doors vanilla cut into the side walls for
        /// neighbouring rooms are sealed too, so the room behind can only be reached through the checkpoint from the
        /// corridor (rooms may still have doors between each other, as before); a neighbour that loses its only exit
        /// is left for EnsureCorridorDoors, which runs afterwards, to give a corridor door.
        /// </summary>
        public static void FixDoors(StructureLayout layout, VaultLayoutPlan plan, LayoutRoomDef corridorDef)
        {
            if (plan == null) return;

            foreach (VaultLayoutPlan.Checkpoint cp in plan.checkpoints)
            {
                if (cp.room == null) continue;

                IntVec3 front = FrontMid(cp.rect, cp.rot);
                IntVec3 back = BackMid(cp.rect, cp.rot);
                LayoutRoom corridor = RoomAcross(layout, cp.room, cp.rect, front);
                LayoutRoom behind = RoomAcross(layout, cp.room, cp.rect, back);
                if (corridor?.requiredDef != corridorDef || behind == null)
                {
                    Log.WarningOnce($"[DMS] Checkpoint at {cp.rect} isn't between a corridor and a room; keeping vanilla's doors.", 0x4C4B02);
                    continue;
                }

                foreach (IntVec3 edge in cp.rect.EdgeCells)
                {
                    if (layout.IsDoorAt(edge)) layout.Add(edge, RoomLayoutCellType.Wall);
                }
                foreach (LayoutRoom other in cp.room.connections) other.connections.Remove(cp.room);
                cp.room.connections.Clear();

                foreach ((IntVec3 cell, LayoutRoom other) in new[] { (front, corridor), (back, behind) })
                {
                    layout.Add(cell, RoomLayoutCellType.Door);
                    cp.room.connections.Add(other);
                    if (!other.connections.Contains(cp.room)) other.connections.Add(cp.room);
                }
            }
        }

        // ── 填充階段 / Fill stage ────────────────────────────────────────────

        /// <summary>
        /// 檢查點的朝向：哪一面貼著走廊（結構的 z = 0 那面朝走廊）。跟版面產生時的算法一致：北 = 往 +z 長出去。
        /// The checkpoint's rotation: which side touches the corridor (the structure's z = 0 side faces it). Matches the
        /// layout generator: North = it grows off towards +z.
        /// </summary>
        public static bool TryGetFrontRot(LayoutRoom room, out Rot4 rot)
        {
            rot = Rot4.North;
            CellRect rect = room.rects[0];
            LayoutRoomDef corridorDef = CorridorDefOf(room);
            List<CellRect> corridors = room.sketch?.structureLayout?.Rooms
                .Where(r => r.requiredDef == corridorDef)
                .SelectMany(r => r.rects)
                .Select(r => r.ContractedBy(1))
                .ToList();
            if (corridorDef == null || corridors.NullOrEmpty()) return false;

            foreach (Rot4 r in Rot4.AllRotations)
            {
                IntVec3 front = FrontMid(rect, r);
                if (corridors.Any(x => x.Contains(front + VaultRoomUtility.OutwardDirection(rect, front))))
                {
                    rot = r;
                    return true;
                }
            }
            return false;
        }

        public static LayoutRoomDef CorridorDefOf(LayoutRoom room) => (room.sketch?.layoutDef as RimWorld.StructureLayoutDef)?.corridorDef;

        /// <summary>檢查點後面接的是哪間房。The room behind the checkpoint.</summary>
        public static LayoutRoom RoomBehind(LayoutRoom room, Rot4 rot)
        {
            StructureLayout layout = room.sketch?.structureLayout;
            return layout == null ? null : RoomAcross(layout, room, room.rects[0], BackMid(room.rects[0], rot));
        }

        /// <summary>
        /// 變體以 rot 蓋進 rect 時，牆上每一格（不含牆角）是牆、門還是開口；以及開口上擋路的東西。
        /// 結構實際範圍跟 rect 不同大小就回傳 null（定位方式與 FFF_StructureUtility.Generate 相同）。
        /// For a variant stamped into rect at rot: whether each wall cell (corners excluded) is wall, door or opening, and
        /// what blocks an opening. Null when the structure's real footprint isn't rect's size (positioned exactly as
        /// FFF_StructureUtility.Generate does).
        /// </summary>
        private static Dictionary<IntVec3, (EdgeKind kind, bool blocked)> EdgePlan(FFF_StructureDef structure, CellRect rect, Rot4 rot)
        {
            Sketch sketch = structure.GetSketch();
            if (rot != Rot4.North) sketch.Rotate(rot);
            CellRect occupied = sketch.OccupiedRect;
            if (occupied.Width != rect.Width || occupied.Height != rect.Height) return null;
            IntVec3 offset = rect.CenterCell - occupied.CenterCell;

            Dictionary<IntVec3, (EdgeKind kind, bool blocked)> plan = new Dictionary<IntVec3, (EdgeKind, bool)>();
            foreach (IntVec3 edge in rect.EdgeCells)
            {
                if (!rect.IsCorner(edge)) plan[edge] = (EdgeKind.Opening, false);
            }
            foreach (SketchThing thing in sketch.Things)
            {
                foreach (IntVec3 cell in thing.OccupiedRect.MovedBy(offset))
                {
                    if (!plan.TryGetValue(cell, out (EdgeKind kind, bool blocked) entry) || entry.kind != EdgeKind.Opening) continue;
                    if (thing.def.IsDoor) plan[cell] = (EdgeKind.Door, false);
                    else if (thing.def.IsWall) plan[cell] = (EdgeKind.Wall, false);
                    else if (thing.def.passability == Traversability.Impassable) plan[cell] = (EdgeKind.Opening, true);
                }
            }
            return plan;
        }

        /// <summary>
        /// 這個變體能不能放在這間房前面：獎勵房（含伺服機房）靠房間那面的開口全都要是門，之後才封得起來；
        /// 電梯廳前面不能有擋路的開口，不然玩家一下來就被關住。
        /// Whether this variant may stand in front of this room: a treasury (server halls included) needs every opening
        /// on the room side to be a door so it can be sealed later; the lift lobby can't have a blocked opening, or the
        /// player is shut in on arrival.
        /// </summary>
        private static bool Suits(Dictionary<IntVec3, (EdgeKind kind, bool blocked)> plan, CellRect rect, Rot4 rot, LayoutRoom behind)
        {
            List<System.Type> workers = behind?.defs?.Select(d => d.roomContentsWorkerType).ToList() ?? new List<System.Type>();
            if (workers.Any(VaultTreasuryUtility.IsTreasuryWorker))
            {
                IntVec3 back = BackMid(rect, rot);
                bool BackRow(IntVec3 c) => rot.IsHorizontal ? c.x == back.x : c.z == back.z;
                if (plan.Any(p => BackRow(p.Key) && p.Value.kind == EdgeKind.Opening)) return false;
            }
            if (workers.Contains(typeof(RoomContents_VaultEntrance)) && plan.Values.Any(v => v.blocked)) return false;
            return true;
        }

        /// <summary>
        /// 挑一個變體蓋進檢查點：先把牆上（不含牆角）版面留下的牆與佔位門拆掉，再蓋結構，讓它的牆、門、開口各就各位；
        /// 最後把走廊那排牆線下被清掉的電纜補回來，走廊電網才不會在這裡斷開。
        /// Picks a variant and stamps it: the layout's walls and placeholder doors along the walls (corners excluded) come
        /// down first so the structure's walls, doors and openings land as drawn; then the conduit cleared off the
        /// corridor's wall line is put back so the corridor grid doesn't break here.
        /// </summary>
        public static bool TryStamp(Map map, LayoutRoom room, LayoutRoomDef def, Faction faction)
        {
            ModExtension_VaultCheckpoint ext = ExtOf(def);
            CellRect rect = room.rects[0];
            if (ext == null || !TryGetFrontRot(room, out Rot4 rot)) return false;

            LayoutRoom behind = RoomBehind(room, rot);
            List<(FFF_StructureDef structure, Dictionary<IntVec3, (EdgeKind kind, bool blocked)> plan)> options = ext.structures
                .Where(s => s != null && FFF_StructureUtility.FootprintAt(s, rect.CenterCell, rot) == rect)
                .Select(s => (s, EdgePlan(s, rect, rot)))
                .Where(o => o.Item2 != null && Suits(o.Item2, rect, rot, behind))
                .ToList();
            if (!options.TryRandomElementByWeight(o => o.structure.baseWeight, out var chosen)) return false;

            foreach (IntVec3 cell in chosen.plan.Keys)
            {
                Building edifice = cell.GetEdifice(map);
                // 走廊與後面房間的壁燈掛在這排共用牆上，拆牆不能讓它們掉成地上的物品。
                // The corridor's and the next room's lamps hang on these shared walls; don't drop them as items.
                if (edifice != null && (edifice.def.IsWall || edifice.def.IsDoor)) VaultRoomUtility.RemoveEdifice(map, cell);
            }
            // 生成期間電網由遊戲在最後統一建立，不需要（也不該）重連。The game builds power nets once generation ends; no reconnect here.
            VaultRoomUtility.AllowingIndestructibleDestroy(() =>
                FFF_StructureUtility.Generate(chosen.structure, rect.CenterCell, map, faction, rot, reconnectPower: false));

            RestoreCorridorConduit(map, room, rect, rot);

            // 結構本身不帶貨架與書櫃的內容物，用 FFF 的 Task_FillStorage 補上（rect 已是地圖座標，offset 為零）。
            // The structures carry no shelf or bookcase contents; FFF's Task_FillStorage stocks them (rect is already in
            // map space, so no offset).
            new Task_FillStorage
            {
                rect = rect,
                makerDef = ext.shelfLoot,
                batches = ext.shelfBatches,
                fillChance = ext.fillChance,
                booksPerBookcase = ext.booksPerBookcase,
            }.Execute(map, IntVec3.Zero, faction);
            return true;
        }

        /// <summary>
        /// 填房間時會清掉不在版面上的東西，檢查點前牆那排（走廊的牆線）下的走廊電纜因此不見了；
        /// 沒被結構自己補上的格子，這裡補回走廊用的電纜（只補空地與牆門下，不壓到別的建築）。
        /// Filling a room clears whatever isn't in the layout, so the corridor conduit under the checkpoint's front row
        /// (the corridor's wall line) is gone. Put the corridor's conduit back wherever the structure didn't, only on bare
        /// floor or under walls and doors, never under other buildings.
        /// </summary>
        private static void RestoreCorridorConduit(Map map, LayoutRoom room, CellRect rect, Rot4 rot)
        {
            ModExtension_VaultSecurity security = CorridorDefOf(room)?.GetModExtension<ModExtension_VaultSecurity>();
            if (security?.conduitDef == null || !security.conduitAlongWalls) return;

            IntVec3 front = FrontMid(rect, rot);
            foreach (IntVec3 cell in rect.EdgeCells)
            {
                bool frontRow = rot.IsHorizontal ? cell.x == front.x : cell.z == front.z;
                if (!frontRow || !cell.InBounds(map)) continue;
                if (cell.GetThingList(map).Any(t => t.def.category == ThingCategory.Building && !t.def.IsWall && !t.def.IsDoor)) continue;
                VaultRoomUtility.TrySpawnConduit(map, security.conduitDef, cell);
            }
        }
    }

    /// <summary>
    /// 入口檢查點的房間 worker：挑一個適合後面那間房的變體整張蓋上（牆、門、開口、家具、地板、屋頂）。
    /// 不跑原版的填充，免得壁燈與雜物擠進這麼小的房間；沒有變體放得進去就退回原版填充（保留版面的佔位門）。
    /// The entrance checkpoint's room worker: stamps a whole variant suited to the room behind (walls, doors, openings,
    /// furniture, floors, roof). Vanilla filling is skipped so wall lamps and junk don't crowd so small a room; if no
    /// variant fits, it falls back to vanilla filling (keeping the layout's placeholder doors).
    /// </summary>
    public class RoomContents_VaultCheckpoint : RoomContentsWorker
    {
        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            if (VaultCheckpointUtility.TryStamp(map, room, RoomDef, faction)) return;

            Log.WarningOnce($"[DMS] Checkpoint at {room.rects[0]} couldn't stamp any variant; filling it as a plain room.", room.rects[0].GetHashCode());
            base.FillRoom(map, room, faction, threatPoints);
        }
    }
}
