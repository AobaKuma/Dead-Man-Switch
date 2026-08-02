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
            return RoomLayoutGenerator.GenerateRandomLayout(
                sketch: parms.sketch,
                container: rect,
                minRoomWidth: Def.minRoomWidth,
                minRoomHeight: Def.minRoomHeight,
                areaPrunePercent: 0.25f,
                canRemoveRooms: true,
                generateDoors: false,
                corridor: null,
                corridorExpansion: 2,
                maxMergeRoomsRange: new IntRange(2, 4),
                corridorShapes: CorridorShape.All,
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

        protected override void PostLayoutFlushedToSketch(LayoutStructureSketch parms)
        {
            base.PostLayoutFlushedToSketch(parms);
            ReplaceDoors(parms.layoutSketch);
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
