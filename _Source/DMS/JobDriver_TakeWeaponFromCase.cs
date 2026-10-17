using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace DMS
{
    /// <summary>
    /// 走到武器箱前，把裡面的武器取出來裝備。原本手上有主武器就先收進背包，不丟地上；
    /// 這把武器不能裝備（生化編碼、能力不足等）時就放在腳邊。
    /// Walk to the weapon case and take the weapon inside as equipment. A primary weapon already in hand
    /// goes into the inventory first rather than onto the floor; if the pawn can't equip the new weapon
    /// (biocoding, incapability, etc.) it is dropped at their feet instead.
    /// </summary>
    public class JobDriver_TakeWeaponFromCase : JobDriver
    {
        private CompWeaponCase Case => TargetThingA?.TryGetComp<CompWeaponCase>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Case == null || !Case.HasWeaponToTake);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);

            yield return Toils_General.Wait(Case?.Props.takeTicks ?? 90)
                .WithProgressBarToilDelay(TargetIndex.A)
                .FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);

            Toil take = ToilMaker.MakeToil("DMS_TakeWeaponFromCase");
            take.initAction = delegate
            {
                Thing weapon = Case?.TakeWeaponOut();
                if (weapon == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                Equip(weapon);
            };
            take.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return take;
        }

        private void Equip(Thing weapon)
        {
            ThingWithComps eq = weapon as ThingWithComps;
            if (eq != null && pawn.equipment != null && EquipmentUtility.CanEquip(eq, pawn))
            {
                ThingWithComps primary = pawn.equipment.Primary;
                if (primary != null
                    && !pawn.equipment.TryTransferEquipmentToContainer(primary, pawn.inventory.innerContainer))
                {
                    // 背包收不下就只好丟地上。No room in the inventory: it has to go on the floor.
                    pawn.equipment.TryDropEquipment(primary, out _, pawn.Position);
                }
                pawn.equipment.AddEquipment(eq);
                return;
            }

            GenPlace.TryPlaceThing(weapon, pawn.Position, pawn.Map, ThingPlaceMode.Near);
        }
    }
}
