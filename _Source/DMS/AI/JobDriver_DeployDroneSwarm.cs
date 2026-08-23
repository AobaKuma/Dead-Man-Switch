using System.Collections.Generic;
using Fortified;
using RimWorld;
using Verse;
using Verse.AI;

namespace DMS
{
    /// <summary>
    /// 開艙、放一批無人機。動作本身只是一段短暫的懸停，
    /// 讓玩家看得出母艦正在投放，而不是憑空冒出一群機。
    /// </summary>
    public class JobDriver_DeployDroneSwarm : JobDriver
    {
        private const int DeployWarmupTicks = 90;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.TryGetComp<CompMechPlatform>() == null);

            Toil hover = Toils_General.Wait(DeployWarmupTicks, TargetIndex.A);
            hover.handlingFacing = true;
            hover.WithProgressBarToilDelay(TargetIndex.A);
            yield return hover;

            Toil release = ToilMaker.MakeToil("ReleaseDroneSwarm");
            release.defaultCompleteMode = ToilCompleteMode.Instant;
            release.initAction = delegate
            {
                CompMechPlatform platform = pawn.TryGetComp<CompMechPlatform>();
                if (platform != null && platform.CanSpawn.Accepted)
                {
                    platform.TrySpawnPawns();
                }
            };
            yield return release;
        }
    }
}
