using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 皇權權限:向殖民艦隊申請一架全新的機動載人升降艙 (DMS_MobileCrewLifter),
    /// 以整機降落 skyfaller 空投到指定的 3x3 位置,歸玩家所有、油箱加滿、底座未脫離 (可整機起飛)。
    /// 流程仿 RoyalTitlePermitWorker_RewardShuttle。
    /// </summary>
    public class RoyalTitlePermitWorker_RewardLifter : RoyalTitlePermitWorker_Targeted
    {
        private Faction calledFaction;

        private static ThingDef LifterDef => DefDatabase<ThingDef>.GetNamed("DMS_MobileCrewLifter");

        public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            if (!CanHitTarget(target))
            {
                if (target.IsValid && showMessages)
                    Messages.Message(def.LabelCap + ": " + "AbilityCannotHitTarget".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            // 與世界地圖降落瞄準相同的判定 (整個 3x3 可見、可站立、非厚岩頂、無建築)
            bool ok = target.IsValid && TransportersArrivalAction_LifterLanding.CanLandInCell(target.Cell, map, LifterDef.Size);
            if (!ok && showMessages)
                Messages.Message("DMS_CrewLifter_CannotLandHere".Translate(), new LookTargets(target.Cell, map), MessageTypeDefOf.RejectInput, historical: false);
            return ok;
        }

        public override void DrawHighlight(LocalTargetInfo target)
        {
            GenDraw.DrawRadiusRing(caller.Position, RangeClamped, Color.white);
            if (!target.IsValid) return;
            ThingDef lifterDef = LifterDef;
            Color col = TransportersArrivalAction_LifterLanding.CanLandInCell(target.Cell, map, lifterDef.Size)
                ? Designator_Place.CanPlaceColor : Designator_Place.CannotPlaceColor;
            GhostDrawer.DrawGhostThing(target.Cell, Rot4.North, lifterDef, lifterDef.graphic, col, AltitudeLayer.Blueprint);
        }

        public override void OrderForceTarget(LocalTargetInfo target)
        {
            DeliverLifter(target.Cell);
        }

        public override void OnGUI(LocalTargetInfo target)
        {
            if (!target.IsValid || !TransportersArrivalAction_LifterLanding.CanLandInCell(target.Cell, map, LifterDef.Size))
                GenUI.DrawMouseAttachment(TexCommand.CannotShoot);
        }

        public override IEnumerable<FloatMenuOption> GetRoyalAidOptions(Map map, Pawn pawn, Faction faction)
        {
            if (map.generatorDef?.isUnderground ?? false)
            {
                yield return new FloatMenuOption(def.LabelCap + ": " + "CommandCallRoyalAidMapUnreachable".Translate(faction.Named("FACTION")), null);
                yield break;
            }
            if (faction.HostileTo(Faction.OfPlayer))
            {
                yield return new FloatMenuOption(def.LabelCap + ": " + "CommandCallRoyalAidFactionHostile".Translate(faction.Named("FACTION")), null);
                yield break;
            }
            string description = def.LabelCap + ": ";
            Action action = null;
            if (FillAidOption(pawn, faction, ref description, out bool free))
            {
                action = delegate { BeginTargetingLanding(pawn, pawn.MapHeld, faction, free); };
            }
            yield return new FloatMenuOption(description, action, faction.def.FactionIcon, faction.Color);
        }

        private void BeginTargetingLanding(Pawn caller, Map map, Faction faction, bool free)
        {
            targetingParameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetSelf = false,
                canTargetPawns = false,
                canTargetFires = false,
                canTargetBuildings = true,
                canTargetItems = true,
            };
            base.caller = caller;
            base.map = map;
            calledFaction = faction;
            base.free = free;
            float rangeActual = RangeClamped;
            targetingParameters.validator = (TargetInfo target) => !(rangeActual > 0f) || !(target.Cell.DistanceTo(caller.Position) > rangeActual);
            Find.Targeter.BeginTargeting(this);
        }

        private void DeliverLifter(IntVec3 cell)
        {
            if (!caller.Spawned) return;
            ThingDef lifterDef = LifterDef;

            // 走升降艙自己的落地流程 (dropPodFaller / dropPodActive):整機降落 skyfaller → 開艙時於原地生成升降艙建築。
            // podOnlyOverride = false:這是全新機體,落地後仍可整機起飛;油量 = 滿油箱。
            CompProperties_Refuelable refuelProps = lifterDef.GetCompProperties<CompProperties_Refuelable>();
            LifterTransporterInfo info = new LifterTransporterInfo
            {
                sentTransporterDef = lifterDef,
                fuel = refuelProps?.fuelCapacity ?? 0f,
                podOnlyOverride = false,
                openDelay = lifterDef.GetCompProperties<CompProperties_Launchable_Lifter>()?.openDelay ?? 60,
            };
            DropPodUtility.MakeDropPodAt(cell, map, info);

            caller.royalty.GetPermit(def, calledFaction).Notify_Used();
            if (!free)
                caller.royalty.TryRemoveFavor(calledFaction, def.royalAid.favorCost);
        }
    }
}
