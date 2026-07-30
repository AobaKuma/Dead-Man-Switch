using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 軍械庫房間：排滿貨架，再用房間定義上的 thingSetMakerDef 把貨架填滿。
    /// Armory room: lays out shelf rows, then fills them from the LayoutRoomDef's thingSetMakerDef.
    ///
    /// 原版 RoomContents_Stockpile 會依 AncientHatch.stockpileType 硬塞醫藥/化石油等固定貨物，
    /// 對 DMS 的軍械庫並不合用，所以這裡只走 ThingSetMaker 路線，讓內容完全由 XML 決定。
    /// Vanilla's RoomContents_Stockpile switches on AncientHatch.stockpileType and spawns fixed
    /// vanilla goods; this one is ThingSetMaker-only so the contents stay fully XML-driven.
    /// </summary>
    public class RoomContents_VaultArmory : RoomContentsWorker
    {
        /// <summary>ThingSetMaker 執行幾輪。輪數越多貨架越滿。</summary>
        private static readonly IntRange BatchCountRange = new IntRange(2, 4);

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            RoomGenUtility.GenerateRows(ThingDefOf.Shelf, room, map, ThingDefOf.Steel);

            ThingSetMakerDef setMaker = room.defs.FirstOrDefault(x => x.thingSetMakerDef != null)?.thingSetMakerDef;
            if (setMaker != null)
            {
                int batches = BatchCountRange.RandomInRange;
                for (int i = 0; i < batches; i++)
                {
                    ThingSetMakerParams parms = new ThingSetMakerParams
                    {
                        totalMarketValueRange = new FloatRange(1500f, 2500f)
                    };
                    List<Thing> items = setMaker.root.Generate(parms);
                    if (!PlaceOnShelves(map, room, items))
                    {
                        // 貨架滿了就別再產下一批，免得整堆東西掉在地上。
                        // Stop once shelves are full instead of dumping loot on the floor.
                        break;
                    }
                }
            }

            base.FillRoom(map, room, faction, threatPoints);
        }

        /// <summary>
        /// 把清單放上貨架。回傳 false 代表已經找不到空位（未放下的物品直接丟棄，不落地）。
        /// Returns false once the shelves are full; leftovers are dropped from the list, not spawned.
        /// </summary>
        private bool PlaceOnShelves(Map map, LayoutRoom room, List<Thing> items)
        {
            int safety = 999;
            while (items.Count > 0 && safety-- > 0)
            {
                Thing item = items[items.Count - 1];

                if (!room.TryGetRandomCellInRoom(map, out IntVec3 cell, 0, 0,
                        (IntVec3 c) => ShelfValidator(map, c, item.def), ignoreBuildings: true))
                {
                    return false;
                }

                items.RemoveAt(items.Count - 1);
                GenSpawn.Spawn(item, cell, map).SetForbidden(value: true);
            }

            return true;
        }

        private bool ShelfValidator(Map map, IntVec3 c, ThingDef itemDef)
        {
            if (!(c.GetFirstThing(map, ThingDefOf.Shelf) is Building_Storage storage))
            {
                return false;
            }
            return storage.SpaceRemainingFor(itemDef) > 0;
        }
    }
}
