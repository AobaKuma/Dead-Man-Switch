using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 機兵封存艙間：優先蓋 FFF 預製搖籃區（含固定的支架排列），塞不下就改成程序化散放封存艙。
    /// Mech bay: prefer the prefab cradle block, fall back to scattering cradles procedurally.
    /// </summary>
    public class RoomContents_VaultMechBay : RoomContentsWorker
    {
        public const string StructureTag = "DMS_VaultMechBay";

        private static readonly IntRange InfantryRange = new IntRange(3, 6);
        private static readonly IntRange AssaultRange = new IntRange(1, 3);
        private static readonly IntRange FrameRange = new IntRange(0, 1);

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            if (!VaultRoomUtility.TryStampStructure(map, room, StructureTag, faction))
            {
                // 大的先放，免得小艙把空間切碎導致龍門架擠不進去。
                // Largest first so the small cradles don't fragment the room out from under the gantry.
                VaultRoomUtility.FillWithPadding(map, room, "DMS_MechCapsule_VaultFrame", FrameRange.RandomInRange, 3);
                VaultRoomUtility.FillWithPadding(map, room, "DMS_MechCapsule_VaultAssault", AssaultRange.RandomInRange, 2);
                VaultRoomUtility.FillWithPadding(map, room, "DMS_MechCapsule_VaultInfantry", InfantryRange.RandomInRange, 2);
            }

            base.FillRoom(map, room, faction, threatPoints);
        }
    }
}
