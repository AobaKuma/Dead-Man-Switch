using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 軍械庫房間：先嘗試蓋一張 FFF 預製貨架區，塞不下就退回程序化排列貨架，
    /// 最後用房間定義上的 thingSetMakerDef 把貨架填滿。
    /// Armory: stamp a prefab shelf block if it fits, otherwise lay procedural shelf rows,
    /// then fill whatever shelves exist from the LayoutRoomDef's thingSetMakerDef.
    /// </summary>
    public class RoomContents_VaultArmory : RoomContentsWorker
    {
        public const string StructureTag = "DMS_VaultArmory";

        /// <summary>ThingSetMaker 執行幾輪。輪數越多貨架越滿。</summary>
        private static readonly IntRange BatchCountRange = new IntRange(2, 4);

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            if (!VaultRoomUtility.TryStampStructure(map, room, StructureTag, faction))
            {
                RoomGenUtility.GenerateRows(ThingDefOf.Shelf, room, map, ThingDefOf.Steel);
            }

            FillShelves(map, room);
            base.FillRoom(map, room, faction, threatPoints);
        }

        private void FillShelves(Map map, LayoutRoom room)
        {
            ThingSetMakerDef setMaker = room.defs.FirstOrDefault(x => x.thingSetMakerDef != null)?.thingSetMakerDef;
            if (setMaker == null) return;

            int batches = BatchCountRange.RandomInRange;
            for (int i = 0; i < batches; i++)
            {
                ThingSetMakerParams parms = new ThingSetMakerParams
                {
                    totalMarketValueRange = new FloatRange(1500f, 2500f)
                };
                List<Thing> items = setMaker.root.Generate(parms);

                // 貨架滿了就別再產下一批，免得整堆東西掉在地上。
                // Stop once shelves are full instead of dumping loot on the floor.
                if (!VaultRoomUtility.PlaceOnShelves(map, room, items)) break;
            }
        }
    }
}
