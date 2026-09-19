using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 StructureLayoutDef 上，指定要把一部分普通門換成哪種加固門。
    /// Tells <see cref="DMS_LayoutWorker_Vault"/> which reinforced door to swap a share of the
    /// ordinary doors for. Without it the layout just keeps its normal doors.
    /// </summary>
    public class ModExtension_VaultDoors : DefModExtension
    {
        public ThingDef reinforcedDoorDef;
        public ThingDef reinforcedDoorStuff;

        /// <summary>要替換的比例（0~1）。Share of doors to replace.</summary>
        public float reinforcedDoorRatio = 0.5f;
    }

    /// <summary>
    /// 地下設施的版面產生器：走廊串起隨機房間，再把一部分門換成加固門。
    /// Corridor-and-rooms layout for underground facilities, with a share of the doors upgraded.
    ///
    /// 等同原版 LayoutWorker_AncientStockpile，差別在於加固門的種類改由
    /// <see cref="ModExtension_VaultDoors"/> 指定，而不是寫死 Odyssey 的 AncientBlastDoor。
    /// Mirrors vanilla LayoutWorker_AncientStockpile; the only change is that the reinforced door
    /// comes from a mod extension instead of the hardcoded Odyssey AncientBlastDoor.
    /// </summary>
    public class DMS_LayoutWorker_Vault : LayoutWorker_Structure
    {
        public DMS_LayoutWorker_Vault(LayoutDef def)
            : base(def)
        {
        }

        protected override StructureLayout GetStructureLayout(StructureGenParams parms, CellRect rect)
        {
            // 有走廊房型就走「先開通道、再掛房間」的自家產生器（輪廓不是方形）；
            // 沒設 corridorDef 才退回原版的矩形切割。
            // With a corridor def, use our corridor-first generator (non-rectangular footprint);
            // without one, fall back to vanilla's rectangle splitting.
            LayoutRoomDef corridor = Def.corridorDef;
            int expansion = corridor?.GetModExtension<ModExtension_VaultSecurity>()?.corridorExpansion ?? 2;

            if (corridor != null)
            {
                return VaultLayoutGenerator.Generate(parms, rect, corridor, expansion, Def.GetModExtension<ModExtension_VaultLayout>());
            }

            return RoomLayoutGenerator.GenerateRandomLayout(
                sketch: parms.sketch,
                container: rect,
                minRoomWidth: Def.minRoomWidth,
                minRoomHeight: Def.minRoomHeight,
                areaPrunePercent: 0.25f,
                canRemoveRooms: true,
                generateDoors: false,
                corridor: corridor,
                corridorExpansion: expansion,
                maxMergeRoomsRange: new IntRange(2, 4),
                corridorShapes: Def.corridorShapes,
                canDisconnectRooms: false);
        }

        protected override void PostGraphsGenerated(StructureLayout layout, StructureGenParams parms)
        {
            // 沒有外門的話，房間不該往結構外開口。
            // With no exterior door defined, rooms must not open outwards.
            foreach (LayoutRoom room in layout.Rooms)
            {
                room.noExteriorDoors = Def.exteriorDoorDef == null;
            }
        }

        protected override StructureLayout GenerateStructure(StructureGenParams parms)
        {
            StructureLayout layout = base.GenerateStructure(parms);
            if (Def.corridorDef != null)
            {
                EnsureCorridorDoors(layout);
            }
            return layout;
        }

        /// <summary>
        /// 原版的門是照鄰接圖開的，偶爾會漏掉某間房；每間房至少要有一扇門通到走廊，
        /// 漏掉的就在它跟走廊共用的牆上直接開一扇。
        /// Vanilla places doors from the neighbourhood graph and occasionally misses a room; every
        /// room needs at least one door onto the corridor, so cut one in the shared wall when it does.
        /// </summary>
        private void EnsureCorridorDoors(StructureLayout layout)
        {
            List<LayoutRoom> corridors = layout.Rooms.Where(r => r.requiredDef == Def.corridorDef).ToList();
            if (corridors.Count == 0) return;

            foreach (LayoutRoom room in layout.Rooms)
            {
                if (room.requiredDef == Def.corridorDef || room.connections.Count > 0) continue;

                bool done = false;
                foreach (LayoutRoom corridor in corridors)
                {
                    foreach (CellRect rect in room.rects)
                    {
                        foreach (CellRect corridorRect in corridor.rects)
                        {
                            if (!TryCutDoor(layout, rect, corridorRect, out IntVec3 cell)) continue;

                            layout.Add(cell, RoomLayoutCellType.Door);
                            room.connections.Add(corridor);
                            corridor.connections.Add(room);
                            done = true;
                            break;
                        }
                        if (done) break;
                    }
                    if (done) break;
                }

                if (!done)
                {
                    Log.Warning($"[DMS] Vault room {room.id} shares no wall with a corridor; it may be unreachable.");
                }
            }
        }

        /// <summary>在兩個矩形共用的牆線上挑最靠中間、適合開門的一格。Pick the most central door-worthy cell on the shared wall.</summary>
        private static bool TryCutDoor(StructureLayout layout, CellRect room, CellRect corridor, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            float best = float.MaxValue;
            IntVec3 centre = corridor.CenterCell;

            foreach (IntVec3 edge in room.EdgeCells)
            {
                if (room.IsCorner(edge) || !corridor.Contains(edge) || !layout.IsGoodForDoor(edge)) continue;

                float dist = centre.DistanceTo(edge);
                if (dist < best)
                {
                    best = dist;
                    cell = edge;
                }
            }

            return cell.IsValid;
        }

        protected override void PostLayoutFlushedToSketch(LayoutStructureSketch parms)
        {
            base.PostLayoutFlushedToSketch(parms);
            ReplaceDoors(parms.layoutSketch);
        }

        public override void Spawn(LayoutStructureSketch layoutStructureSketch, Map map, IntVec3 pos,
            float? threatPoints = null, List<Thing> allSpawnedThings = null, bool roofs = true,
            bool canReuseSketch = false, Faction faction = null)
        {
            base.Spawn(layoutStructureSketch, map, pos, threatPoints, allSpawnedThings, roofs, canReuseSketch, faction);

            // 獎勵房要等所有門都生成完才封得起來。Treasuries can only be sealed once every door exists.
            VaultTreasuryUtility.SealTreasuries(layoutStructureSketch, map, faction);

            // 分區閘門：入口房已經填好、出生點已定，才知道控制台該放哪一側。
            // Sector gates: only now, with the entrance filled and the start spot set, do we know
            // which side of each gate the console belongs on.
            VaultLayoutPlan plan = VaultLayoutGenerator.TakePlan(layoutStructureSketch.structureLayout);
            VaultSectorGates.SpawnGates(layoutStructureSketch.structureLayout, plan, map, faction,
                Def.GetModExtension<ModExtension_VaultLayout>());

            ChargeInternalBatteries(layoutStructureSketch, map);
        }

        /// <summary>
        /// 結構裡所有帶內建電池的東西（牆上的應急燈、哨戒砲……）生成時電量都是 0，
        /// 整片掃一遍充滿，讓設施一進來就是亮的、砲塔就是醒的。
        /// Everything with an internal battery (wall emergency lamps, sentries, …) spawns at zero
        /// charge; sweep the structure once so the place is lit and armed on arrival.
        /// </summary>
        private static void ChargeInternalBatteries(LayoutStructureSketch sketch, Map map)
        {
            CellRect container = sketch.structureLayout.container.ClipInsideMap(map);
            foreach (IntVec3 cell in container)
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    // 多格建築會被掃到好幾次；充電是冪等的，無所謂。
                    // Multi-cell things get visited more than once; charging is idempotent.
                    VaultRoomUtility.ChargeInternalBattery(things[i], 1f);
                }
            }
        }

        private void ReplaceDoors(LayoutSketch sketch)
        {
            ModExtension_VaultDoors ext = Def.GetModExtension<ModExtension_VaultDoors>();
            if (ext?.reinforcedDoorDef == null) return;

            int remaining = Mathf.CeilToInt(sketch.Things.Count(t => t.def.IsDoor) * ext.reinforcedDoorRatio);
            if (remaining <= 0) return;

            foreach (SketchThing thing in sketch.Things.InRandomOrder())
            {
                if (!thing.def.IsDoor) continue;

                thing.def = ext.reinforcedDoorDef;
                thing.stuff = ext.reinforcedDoorDef.MadeFromStuff ? ext.reinforcedDoorStuff : null;

                if (--remaining <= 0) break;
            }
        }
    }
}
