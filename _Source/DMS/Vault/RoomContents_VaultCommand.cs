using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 封存庫指揮室：中央一座還連著地下貨運線的後勤終端，周圍散幾個上鎖貨櫃。
    /// Vault command room: one logistics terminal still wired to the cargo line below,
    /// with a few locked containers around it.
    ///
    /// 主機櫃、破損主控台之類的裝飾交給 LayoutRoomDef 的 prefabs 處理（base.FillRoom）。
    /// Racks and broken consoles come from the LayoutRoomDef's prefabs via base.FillRoom.
    /// </summary>
    public class RoomContents_VaultCommand : RoomContentsWorker
    {
        private const string TerminalDefName = "DMS_LogisticTerminal";
        private const string ContainerDefName = "DMS_LogisticContainer";

        private static readonly IntRange ContainerRange = new IntRange(1, 3);

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            Spawn(map, room, TerminalDefName, 1, 3);
            Spawn(map, room, ContainerDefName, ContainerRange.RandomInRange, 2);

            base.FillRoom(map, room, faction, threatPoints);
        }

        private static void Spawn(Map map, LayoutRoom room, string defName, int count, int contractedBy)
        {
            if (count <= 0)
            {
                return;
            }

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Log.ErrorOnce($"[DMS] RoomContents_VaultCommand: missing ThingDef {defName}", defName.GetHashCode());
                return;
            }

            List<Thing> spawned = new List<Thing>();
            RoomGenUtility.FillWithPadding(def, count, room, map, null, null, spawned, contractedBy);
        }
    }
}
