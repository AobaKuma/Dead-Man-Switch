using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 機兵封存艙間：擺滿休眠中的 DMS 機兵搖籃。
    /// Mech vault bay: rows of dormant DMS mech cradles.
    ///
    /// 用 <see cref="RoomGenUtility.FillWithPadding"/> 而不是 PrefabDef，是因為封存艙有 1x1 / 2x2 / 3x3
    /// 三種尺寸，交給 RoomGenUtility 算間距比手寫 prefab 座標穩得多。
    /// Uses RoomGenUtility rather than hand-authored PrefabDef coordinates because the cradles come in
    /// 1x1 / 2x2 / 3x3 and letting the utility solve spacing is far less error-prone.
    /// </summary>
    public class RoomContents_VaultMechBay : RoomContentsWorker
    {
        private static readonly IntRange InfantryRange = new IntRange(3, 6);
        private static readonly IntRange AssaultRange = new IntRange(1, 3);
        private static readonly IntRange FrameRange = new IntRange(0, 1);

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            // 大的先放，免得小艙把空間切碎導致龍門架擠不進去。
            // Largest first so the small cradles don't fragment the room out from under the gantry.
            Spawn(map, room, "DMS_MechCapsule_VaultFrame", FrameRange.RandomInRange, 3);
            Spawn(map, room, "DMS_MechCapsule_VaultAssault", AssaultRange.RandomInRange, 2);
            Spawn(map, room, "DMS_MechCapsule_VaultInfantry", InfantryRange.RandomInRange, 2);

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
                Log.ErrorOnce($"[DMS] RoomContents_VaultMechBay: missing ThingDef {defName}", defName.GetHashCode());
                return;
            }

            List<Thing> spawned = new List<Thing>();
            RoomGenUtility.FillWithPadding(def, count, room, map, null, null, spawned, contractedBy);
        }
    }
}
