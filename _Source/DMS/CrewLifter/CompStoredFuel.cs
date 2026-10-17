using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    public class CompProperties_StoredFuel : CompProperties
    {
        // 拆除時以此物品退還殘餘燃料
        public ThingDef fuelDef;

        public CompProperties_StoredFuel()
        {
            compClass = typeof(CompStoredFuel);
        }
    }

    /// <summary>
    /// 廢棄返回艙的殘餘燃料:落地時由 LifterTransporterInfo.fuel 寫入,拆除時整數部分以化學燃料退還。
    /// 不用 CompRefuelable 是因為不希望殖民者再往廢艙裡加油、也不需要油量目標 gizmo。
    /// </summary>
    public class CompStoredFuel : ThingComp
    {
        public float fuel;

        public CompProperties_StoredFuel Props => (CompProperties_StoredFuel)props;

        private ThingDef FuelDef => Props.fuelDef ?? ThingDefOf.Chemfuel;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref fuel, "fuel");
        }

        public override string CompInspectStringExtra()
        {
            if (fuel < 1f) return null;
            return "DMS_CrewLifter_ResidualFuel".Translate(Mathf.FloorToInt(fuel), FuelDef.label);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            // 只有拆除回收才退油;被打壞 (KillFinalize) 或 Vanish 都不退
            if (mode != DestroyMode.Deconstruct || previousMap == null) return;
            int remaining = Mathf.FloorToInt(fuel);
            fuel = 0f;
            while (remaining > 0)
            {
                Thing stack = ThingMaker.MakeThing(FuelDef);
                stack.stackCount = Mathf.Min(remaining, FuelDef.stackLimit);
                remaining -= stack.stackCount;
                GenPlace.TryPlaceThing(stack, parent.Position, previousMap, ThingPlaceMode.Near);
            }
        }
    }
}
