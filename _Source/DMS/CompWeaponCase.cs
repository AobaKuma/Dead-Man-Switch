using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace DMS
{
    /// <summary>
    /// 牆掛武器箱：純靜態容器，不接警報、不會自己開火。裡面的武器走 CompRefuelable 存放，
    /// 殖民者用一般的補充工作把武器裝進去，右鍵可以直接取出裝備（<see cref="JobDriver_TakeWeaponFromCase"/>）。
    /// 燃料條 gizmo 上的勾選框決定要不要自動補裝。
    /// Wall-mounted weapon case: a purely static container that neither listens to alarms nor fires. The
    /// weapon inside is stored through CompRefuelable: colonists load one with the ordinary refuel job, and
    /// the right-click option takes it out and equips it (<see cref="JobDriver_TakeWeaponFromCase"/>).
    /// The checkbox on the fuel-bar gizmo decides whether they restock it automatically.
    /// </summary>
    public class CompProperties_WeaponCase : CompProperties
    {
        /// <summary>
        /// 生成時若不是玩家的建築就直接裝滿。玩家蓋的箱子從空的開始，得自己放一件武器進去；
        /// 設施／地圖生成的箱子則生來就是裝好的。判斷用生成當下的派系：玩家施工完成的建築在 Spawn 前就已是玩家派系，
        /// 地圖生成的建築則是生成後才換成守軍派系。
        /// Fill the case on spawn when it isn't a player building. Player-built cases start empty and need a
        /// weapon loaded in; facility / mapgen ones come stocked. Decided by the faction at spawn time: a
        /// player-completed frame is already player faction before Spawn, while mapgen buildings only get
        /// their defender faction after spawning.
        /// </summary>
        public bool preloadWhenNotPlayerOwned = true;

        /// <summary>取出武器要花的時間。Ticks it takes to pull the weapon out.</summary>
        public int takeTicks = 90;

        public CompProperties_WeaponCase()
        {
            compClass = typeof(CompWeaponCase);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            CompProperties_Refuelable refuelable = parentDef.GetCompProperties<CompProperties_Refuelable>();
            if (refuelable == null)
                yield return $"{parentDef.defName}: CompProperties_WeaponCase needs a CompProperties_Refuelable to hold the weapon";
            else if (refuelable.fuelFilter?.AnyAllowedDef == null)
                yield return $"{parentDef.defName}: CompProperties_WeaponCase needs the fuelFilter to allow a weapon def";
        }
    }

    public class CompWeaponCase : ThingComp
    {
        private CompRefuelable refuelable;

        public CompProperties_WeaponCase Props => (CompProperties_WeaponCase)props;

        /// <summary>箱裡裝的武器 def（燃料過濾器唯一允許的東西）。The weapon def in the case (the fuel filter's single allowed def).</summary>
        public ThingDef WeaponDef => refuelable?.Props.fuelFilter.AnyAllowedDef;

        /// <summary>一件武器對應的燃料量。Fuel worth one weapon.</summary>
        private float FuelPerWeapon => refuelable?.Props.FuelMultiplierCurrentDifficulty ?? 1f;

        public bool HasWeaponToTake => refuelable != null && WeaponDef != null && refuelable.Fuel >= FuelPerWeapon;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            refuelable = parent.GetComp<CompRefuelable>();

            if (!respawningAfterLoad && Props.preloadWhenNotPlayerOwned && refuelable != null
                && parent.Faction != Faction.OfPlayer && !refuelable.IsFull)
            {
                refuelable.Refuel(refuelable.Props.fuelCapacity);
            }
        }

        /// <summary>
        /// 從箱子裡拿出一件武器：扣掉一件的燃料，生出一個未生成的 Thing 交給呼叫端處理（裝備或放地上）。
        /// 燃料本來就是「被吃掉」的物品，所以拿出來的是新造的普通品質武器。
        /// Take one weapon out of the case: deduct one weapon's worth of fuel and hand back an unspawned
        /// Thing for the caller to equip or drop. Fuel items are destroyed on loading, so what comes out is a
        /// freshly made normal-quality weapon.
        /// </summary>
        public Thing TakeWeaponOut()
        {
            if (!HasWeaponToTake) return null;
            refuelable.ConsumeFuel(FuelPerWeapon);
            Thing weapon = ThingMaker.MakeThing(WeaponDef);
            weapon.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
            return weapon;
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (refuelable == null || WeaponDef == null) yield break;

            string label = "DMS_LauncherCase_TakeWeapon".Translate(WeaponDef.label);
            AcceptanceReport report = CanTakeWeapon(selPawn);
            if (!report.Accepted)
            {
                yield return new FloatMenuOption(label + " (" + report.Reason.UncapitalizeFirst() + ")", null);
                yield break;
            }

            yield return RimWorld.FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, delegate
            {
                Job job = JobMaker.MakeJob(DMS_DefOf.DMS_TakeWeaponFromCase, parent);
                selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }), selPawn, parent);
        }

        private AcceptanceReport CanTakeWeapon(Pawn pawn)
        {
            if (!HasWeaponToTake) return "DMS_LauncherCase_Empty".Translate();
            if (pawn.equipment == null || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return "Incapable".Translate();
            if (WeaponDef.IsWeapon && pawn.WorkTagIsDisabled(WorkTags.Violent))
                return "IsIncapableOfViolenceLower".Translate(pawn.LabelShort, pawn);
            if (!pawn.CanReach(parent, PathEndMode.Touch, Danger.Deadly)) return "NoPath".Translate();
            if (!pawn.CanReserve(parent)) return "Reserved".Translate();
            return true;
        }
    }
}
