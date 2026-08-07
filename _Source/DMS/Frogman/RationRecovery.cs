using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DMS
{
    [DefOf]
    public static class DMS_FrogmanDefOf
    {
        public static JobDef DMS_LoadRation;

        static DMS_FrogmanDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DMS_FrogmanDefOf));
        }
    }

    // ================================================================
    // 口糧彈艙：掛在機兵本體上，存放已裝填的 C 口糧。
    // ================================================================

    public class CompProperties_RationMagazine : CompProperties
    {
        /// <summary>可裝填的物品。</summary>
        public ThingDef rationDef;

        /// <summary>最大裝填數。</summary>
        public int maxRations = 3;

        /// <summary>生成時預裝的數量。</summary>
        public int startingRations = 1;

        /// <summary>每次裝填動作耗時。</summary>
        public int loadTicks = 240;

        public CompProperties_RationMagazine()
        {
            compClass = typeof(CompRationMagazine);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (rationDef == null)
            {
                yield return "CompProperties_RationMagazine 沒有指定 rationDef。";
            }
        }
    }

    /// <summary>
    /// 機兵體內的口糧艙。
    ///
    /// 沒有沿用原版 CompApparelReloadable：那套 IReloadableComp 只認裝備與服裝
    /// （JobDriver_Reload 內部寫死 CompApparelReloadable / CompEquippableAbilityReloadable），
    /// 而且 Fortified 的機兵服裝生成器只對非玩家派系生效，玩家自己培育出來的蛙人
    /// 不會自帶任何服裝。要讓能力是「機兵內建」的，彈艙就得掛在機兵本體上。
    /// </summary>
    public class CompRationMagazine : ThingComp
    {
        private int loadedRations = -1;

        public CompProperties_RationMagazine Props => (CompProperties_RationMagazine)props;

        private Pawn Pawn => parent as Pawn;

        public int LoadedRations => Mathf.Max(0, loadedRations);

        public int MaxRations => Props.maxRations;

        public bool IsFull => LoadedRations >= MaxRations;

        public int RationsNeeded => Mathf.Max(0, MaxRations - LoadedRations);

        public override void PostPostMake()
        {
            base.PostPostMake();
            if (loadedRations < 0)
            {
                loadedRations = Mathf.Clamp(Props.startingRations, 0, Props.maxRations);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref loadedRations, "DMS_loadedRations", -1);
        }

        public bool TryConsume(int count)
        {
            if (LoadedRations < count)
            {
                return false;
            }
            loadedRations = LoadedRations - count;
            return true;
        }

        /// <summary>從搬來的堆疊裡吃掉需要的份數，剩下的留在原堆疊。</summary>
        public void LoadFrom(Thing ration)
        {
            if (ration == null || ration.Destroyed || ration.def != Props.rationDef)
            {
                return;
            }
            int taken = Mathf.Min(RationsNeeded, ration.stackCount);
            if (taken <= 0)
            {
                return;
            }
            loadedRations = LoadedRations + taken;
            ration.SplitOff(taken).Destroy();
        }

        public override string CompInspectStringExtra()
        {
            if (Props.rationDef == null)
            {
                return null;
            }
            return "DMS.RationMagazineContents".Translate(
                Props.rationDef.LabelCap.Named("RATION"),
                LoadedRations.Named("COUNT"),
                MaxRations.Named("MAX"));
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn pawn = Pawn;
            if (pawn == null || !pawn.IsColonyMechPlayerControlled || Props.rationDef == null)
            {
                yield break;
            }

            Command_Action load = new Command_Action
            {
                defaultLabel = "DMS.LoadRationLabel".Translate(Props.rationDef.label),
                defaultDesc = "DMS.LoadRationDesc".Translate(
                    Props.rationDef.label.Named("RATION"),
                    MaxRations.Named("MAX")),
                icon = Props.rationDef.uiIcon,
                action = delegate { TryStartLoadJob(pawn); }
            };

            if (IsFull)
            {
                load.Disable("DMS.LoadRationFull".Translate());
            }
            else if (FindRation(pawn) == null)
            {
                load.Disable("DMS.LoadRationNoneAvailable".Translate(Props.rationDef.label));
            }

            yield return load;

            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Fill ration magazine",
                    action = delegate { loadedRations = MaxRations; }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Empty ration magazine",
                    action = delegate { loadedRations = 0; }
                };
            }
        }

        private void TryStartLoadJob(Pawn pawn)
        {
            Thing ration = FindRation(pawn);
            if (ration == null)
            {
                Messages.Message("DMS.LoadRationNoneAvailable".Translate(Props.rationDef.label), pawn,
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Job job = JobMaker.MakeJob(DMS_FrogmanDefOf.DMS_LoadRation, ration);
            job.count = Mathf.Min(RationsNeeded, ration.stackCount);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public Thing FindRation(Pawn pawn)
        {
            if (pawn?.Map == null || Props.rationDef == null)
            {
                return null;
            }
            return GenClosest.ClosestThingReachable(
                pawn.Position, pawn.Map,
                ThingRequest.ForDef(Props.rationDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                (Thing t) => !t.IsForbidden(pawn) && pawn.CanReserve(t));
        }
    }

    // ================================================================
    // 裝填工作
    // ================================================================

    public class JobDriver_LoadRation : JobDriver
    {
        private CompRationMagazine Magazine => pawn.TryGetComp<CompRationMagazine>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, job.count, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Magazine == null || Magazine.IsFull);
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);

            yield return Toils_Haul.StartCarryThing(TargetIndex.A, putRemainderInQueue: false,
                    subtractNumTakenFromJobCount: true)
                .FailOnDestroyedNullOrForbidden(TargetIndex.A);

            yield return Toils_General.Wait(Magazine?.Props.loadTicks ?? 240)
                .WithProgressBarToilDelay(TargetIndex.A);

            Toil load = ToilMaker.MakeToil("DMS_LoadRation");
            load.initAction = delegate
            {
                Magazine?.LoadFrom(pawn.carryTracker.CarriedThing);
                if (pawn.carryTracker.CarriedThing != null)
                {
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
                }
            };
            load.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return load;
        }
    }

    // ================================================================
    // 能力：消耗口糧，回滿電力並再生受損（非缺失）部位
    // ================================================================

    public class CompProperties_AbilityRationRecovery : CompProperties_AbilityEffect
    {
        /// <summary>每次施放消耗的口糧數。</summary>
        public int rationCost = 1;

        /// <summary>是否把機兵電力補滿。</summary>
        public bool refillEnergy = true;

        /// <summary>是否清除傷勢。缺失部位一律不處理。</summary>
        public bool healInjuries = true;

        /// <summary>是否連永久性傷痕一併再生。</summary>
        public bool healPermanentInjuries = true;

        public EffecterDef effecter;

        public CompProperties_AbilityRationRecovery()
        {
            compClass = typeof(CompAbilityEffect_RationRecovery);
        }
    }

    /// <summary>
    /// 蛙人的維生循環：把一份 C 口糧丟進體內的生化反應堆，
    /// 一次補滿電力並讓合成肌束把受損部位重新長回來。
    /// 缺失的部位長不回來——那要靠自我修復模式重建結構。
    /// </summary>
    public class CompAbilityEffect_RationRecovery : CompAbilityEffect
    {
        public new CompProperties_AbilityRationRecovery Props => (CompProperties_AbilityRationRecovery)props;

        private CompRationMagazine Magazine => parent?.pawn?.TryGetComp<CompRationMagazine>();

        public override bool CanCast => base.CanCast && (Magazine?.LoadedRations ?? 0) >= Props.rationCost;

        public override bool GizmoDisabled(out string reason)
        {
            CompRationMagazine magazine = Magazine;
            if (magazine == null)
            {
                reason = "DMS.RationRecoveryNoMagazine".Translate();
                return true;
            }
            if (magazine.LoadedRations < Props.rationCost)
            {
                reason = "DMS.RationRecoveryNoRations".Translate(magazine.Props.rationDef.label);
                return true;
            }
            return base.GizmoDisabled(out reason);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn pawn = parent.pawn;
            CompRationMagazine magazine = Magazine;
            if (pawn == null || magazine == null || !magazine.TryConsume(Props.rationCost))
            {
                return;
            }

            if (Props.refillEnergy && pawn.needs?.energy != null)
            {
                pawn.needs.energy.CurLevel = pawn.needs.energy.MaxLevel;
            }

            int healed = 0;
            if (Props.healInjuries && pawn.health?.hediffSet != null)
            {
                // 先收集再移除：不要在迭代 hediffs 的過程中改動它。
                List<Hediff> toRemove = new List<Hediff>();
                List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    // Hediff_MissingPart 不是 Hediff_Injury，所以缺失部位天然被排除在外。
                    if (hediffs[i] is Hediff_Injury injury &&
                        (Props.healPermanentInjuries || !injury.IsPermanent()))
                    {
                        toRemove.Add(injury);
                    }
                }
                for (int i = 0; i < toRemove.Count; i++)
                {
                    pawn.health.RemoveHediff(toRemove[i]);
                    healed++;
                }
            }

            if (Props.effecter != null && pawn.Spawned)
            {
                Effecter effecter = new Effecter(Props.effecter);
                effecter.Trigger(pawn, TargetInfo.Invalid);
                effecter.Cleanup();
            }

            Messages.Message(
                "DMS.RationRecoveryDone".Translate(pawn.LabelShortCap.Named("PAWN"), healed.Named("COUNT")),
                pawn, MessageTypeDefOf.PositiveEvent, historical: false);
        }
    }
}
