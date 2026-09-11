using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 升降艙落地 skyfaller:原版空投落點只保證 1 格可用,
    /// 生成時改找一塊能容納落地建築 (sentTransporterDef.Size) 的空地,免得 3x3 升降艙壓在牆或其他建築上。
    /// skyfaller def 本身維持 1x1:GenSpawn.Spawn 會先用 def.Size 檢查越界,3x3 落在地圖邊緣會直接 spawn 失敗。
    /// 起飛/著陸特效不在這裡處理:Skyfaller 是 ThingWithComps,直接用原版 CompEffecter / CompSpawnEffecterOnDestroy (見 Effects/DMS_Lifter.xml)。
    /// </summary>
    public class LifterIncoming : DropPodIncoming
    {
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            if (!respawningAfterLoad)
            {
                IntVec2 size = (innerContainer.Count > 0 ? Contents?.sentTransporterDef?.Size : null) ?? IntVec2.One;
                if ((size.x > 1 || size.z > 1) && !RectIsClear(Position, map, size)
                    && DropCellFinder.TryFindDropSpotNear(Position, map, out IntVec3 cell, allowFogged: true, canRoofPunch: false, 30,
                        allowIndoors: false, size, mustBeReachableFromCenter: false))
                {
                    Position = cell;
                }
            }
            base.SpawnSetup(map, respawningAfterLoad);
        }

        private static bool RectIsClear(IntVec3 center, Map map, IntVec2 size)
        {
            foreach (IntVec3 c in GenAdj.OccupiedRect(center, Rot4.North, size))
            {
                if (!DropCellFinder.IsGoodDropSpot(c, map, allowFogged: true, canRoofPunch: false, allowIndoors: false))
                    return false;
            }
            return true;
        }
    }
}
