using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 封存庫指揮室：中央一座還連著地下貨運線的後勤終端，周圍散幾個上鎖貨櫃。
    /// Vault command room: one logistics terminal still wired to the cargo line below,
    /// with a few locked containers around it.
    ///
    /// 機櫃、殘骸之類的裝飾交給 LayoutRoomDef 的 prefabs 處理（base.FillRoom）。
    /// Racks and wreckage come from the LayoutRoomDef's prefabs via base.FillRoom.
    /// </summary>
    public class RoomContents_VaultCommand : RoomContentsWorker
    {
        private static readonly IntRange ContainerRange = new IntRange(1, 3);

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            VaultRoomUtility.FillWithPadding(map, room, "DMS_LogisticTerminal", 1, 3);
            VaultRoomUtility.FillWithPadding(map, room, "DMS_LogisticContainer", ContainerRange.RandomInRange, 2);

            base.FillRoom(map, room, faction, threatPoints);
        }
    }
}
