using System.Collections.Generic;
using System.Linq;
using Fortified.Structures;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 地下設施房間填充的共用工具。
    /// Shared helpers for filling underground facility rooms.
    /// </summary>
    public static class VaultRoomUtility
    {
        /// <summary>
        /// 在房間中央蓋一張帶指定標籤的 FFF 預製結構。房間塞不下就回傳 false，
        /// 呼叫端應該退回程序化填充。
        /// Stamps a tagged FFF structure in the middle of the room. Returns false when nothing fits,
        /// in which case the caller should fall back to procedural filling.
        /// </summary>
        public static bool TryStampStructure(Map map, LayoutRoom room, string tag, Faction faction)
        {
            if (tag.NullOrEmpty()) return false;

            List<FFF_StructureDef> candidates = DefDatabase<FFF_StructureDef>.AllDefs
                .Where(d => d.tags != null && d.tags.Contains(tag))
                .ToList();

            if (candidates.Count == 0)
            {
                Log.WarningOnce($"[DMS] No FFF_StructureDef carries the tag '{tag}'.", tag.GetHashCode());
                return false;
            }

            // 大的先試，這樣房間夠大時會用上內容比較豐富的那張。
            // Try the biggest first so roomy rooms get the richer block.
            foreach (FFF_StructureDef def in candidates.OrderByDescending(d => d.size.x * d.size.z))
            {
                // 多留 1 格，避免預製塊直接貼到房間牆上。
                // One cell of slack so the block doesn't press against the room walls.
                if (!room.TryGetRectOfSize(def.size.x + 1, def.size.z + 1, out CellRect rect)) continue;

                FFF_StructureUtility.Generate(def, rect.CenterCell, map, faction, Rot4.North);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 用 ThingSetMaker 產物填滿房間裡的貨架。回傳 false 代表貨架已滿。
        /// Fills the room's shelves from a ThingSetMaker. Returns false once they are full.
        /// </summary>
        public static bool PlaceOnShelves(Map map, LayoutRoom room, List<Thing> items)
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

        private static bool ShelfValidator(Map map, IntVec3 c, ThingDef itemDef)
        {
            if (!(c.GetFirstThing(map, ThingDefOf.Shelf) is Building_Storage storage)) return false;
            return storage.SpaceRemainingFor(itemDef) > 0;
        }

        /// <summary>依 defName 取 ThingDef，缺了就記一次錯誤。Look up a ThingDef, warning once if absent.</summary>
        public static ThingDef ThingNamed(string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Log.ErrorOnce($"[DMS] Vault generation: missing ThingDef {defName}", defName.GetHashCode());
            }
            return def;
        }

        /// <summary>用 RoomGenUtility 在房內平均放置若干建築。Spread N buildings through the room.</summary>
        public static void FillWithPadding(Map map, LayoutRoom room, string defName, int count, int contractedBy)
        {
            if (count <= 0) return;

            ThingDef def = ThingNamed(defName);
            if (def == null) return;

            List<Thing> spawned = new List<Thing>();
            RoomGenUtility.FillWithPadding(def, count, room, map, null, null, spawned, contractedBy);
        }
    }
}
