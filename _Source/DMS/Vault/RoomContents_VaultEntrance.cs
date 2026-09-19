using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 地下設施的電梯廳：放出口電梯、拉玩家出生點。
    /// 這是玩家的初始房間，刻意不放任何砲塔或警戒建築——落地就被打不公平，防務從走廊才開始。
    /// The elevator lobby: exit car and player spawn spot. This is the player's starting room, so it
    /// deliberately holds no turrets or security fixtures; the defences begin in the corridor.
    ///
    /// 出口 ThingDef 不寫死，改讀目前正在產生這張口袋地圖的傳送門 def 上的 portal.exitDef，
    /// 所以同一個 worker 可以服務任何 DMS 出入口。
    /// The exit is read from portal.exitDef of whichever portal is generating the map, so one
    /// worker serves every DMS entrance.
    /// </summary>
    public class RoomContents_VaultEntrance : RoomContentsWorker
    {
        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            SpawnExit(map, room);
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

            // 出口是 5x5（原版艙口只有 3x3），padding 3 需要 11x11 的房間矩形；
            // 房間不夠大時逐步縮小 padding，否則出口不會生成、出生點也不會被設定，
            // GenStep_Fog 會報 "Accessing player start spot before setting it"。
            // The car is 5x5 (vanilla's hatch is 3x3), so padding 3 needs an 11x11 rect.
            // Shrink the padding when the room is tighter instead of silently spawning nothing.
            List<Thing> spawned = new List<Thing>();
            for (int padding = 3; padding >= 1 && spawned.Count == 0; padding--)
            {
                RoomGenUtility.FillWithPadding(exitDef, 1, room, map, null, null, spawned, padding);
            }

            if (spawned.Count > 0)
            {
                MapGenerator.PlayerStartSpot = spawned[0].Position;
                return;
            }

            // 最後手段：直接放在最大矩形中央，保證有出口與出生點。
            // Last resort: drop it in the middle of the largest rect so there is always an exit.
            CellRect largest = room.rects.MaxBy(r => r.Area);
            IntVec3 center = largest.CenterCell;
            Log.Warning($"[DMS] RoomContents_VaultEntrance: no padded spot for {exitDef.defName} in a {largest.Width}x{largest.Height} room; forcing it at {center}.");
            Thing exit = GenSpawn.Spawn(ThingMaker.MakeThing(exitDef), center, map, Rot4.North);
            MapGenerator.PlayerStartSpot = exit.Position;
        }
    }
}
