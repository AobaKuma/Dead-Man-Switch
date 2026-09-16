using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 地下設施的電梯廳：放出口電梯、拉玩家出生點、擺幾座還在待機的 DMS 哨戒砲。
    /// The elevator lobby: exit car, player spawn spot, and a couple of still-armed DMS sentries.
    ///
    /// 出口 ThingDef 不寫死，改讀目前正在產生這張口袋地圖的傳送門 def 上的 portal.exitDef，
    /// 所以同一個 worker 可以服務任何 DMS 出入口。
    /// The exit is read from portal.exitDef of whichever portal is generating the map, so one
    /// worker serves every DMS entrance.
    /// </summary>
    public class RoomContents_VaultEntrance : RoomContentsWorker
    {
        private const string TurretDefName = "DMS_Turret_Sentry";

        private static readonly IntRange TurretsRange = new IntRange(1, 2);

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            SpawnExit(map, room);
            SpawnTurrets(map, room, faction);
            base.FillRoom(map, room, faction, threatPoints);
        }

        private void SpawnExit(Map map, LayoutRoom room)
        {
            ThingDef exitDef = PocketMapUtility.currentlyGeneratingPortal?.def?.portal?.exitDef;
            if (exitDef == null)
            {
                Log.Error("[DMS] RoomContents_VaultEntrance: the portal has no exitDef; pawns would be trapped.");
                return;
            }

            List<Thing> spawned = new List<Thing>();
            RoomGenUtility.FillWithPadding(exitDef, 1, room, map, null, null, spawned, 3);
            if (spawned.Count > 0)
            {
                MapGenerator.PlayerStartSpot = spawned[0].Position;
            }
        }

        private void SpawnTurrets(Map map, LayoutRoom room, Faction faction)
        {
            ThingDef turretDef = VaultRoomUtility.ThingNamed(TurretDefName);
            if (turretDef == null) return;

            RoomGenUtility.FillAroundEdges(turretDef, TurretsRange.RandomInRange, IntRange.One, room, map,
                null, null, 1, 0, null, avoidDoors: true, RotationDirection.Opposite, null, faction);
        }
    }
}
