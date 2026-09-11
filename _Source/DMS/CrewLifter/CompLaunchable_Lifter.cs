using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DMS
{
    public class CompProperties_Launchable_Lifter : CompProperties_Launchable
    {
        // 第二次發射 (脫離底座) 時使用的離場 skyfaller / 世界物件 (貼圖只有返回艙)
        public ThingDef podSkyfallerLeaving;
        public WorldObjectDef podWorldObjectDef;
        // 第二次發射時作為 sentTransporterDef 的 def,決定落地用的 dropPodFaller / dropPodActive
        public ThingDef podSentDef;
        // 第二次發射後留在原地的無陣營底座
        public ThingDef platformDef;
        // 落地後開艙的延遲 tick
        public int openDelay = 60;

        public CompProperties_Launchable_Lifter()
        {
            compClass = typeof(CompLaunchable_Lifter);
        }
    }

    /// <summary>
    /// 可二次發射的升降艙:
    /// 第一次發射整機起飛,落地後於原地重建為升降艙建築 (podOnly = true);
    /// 第二次發射只有返回艙升空,底座以無陣營建築留在原地。
    /// 自帶燃料槽,不需要發射台。
    /// </summary>
    public class CompLaunchable_Lifter : CompLaunchable
    {
        // 已完成第一次飛行、底座可脫離:下次發射只發射返回艙
        public bool podOnly;

        // 世界瞄準時的半徑環快取 (同原版 CompLaunchable 的私有快取)
        private PlanetTile cachedClosest;
        private PlanetTile cachedOrigin;
        private PlanetLayer cachedLayer;

        public new CompProperties_Launchable_Lifter Props => (CompProperties_Launchable_Lifter)props;

        public override bool RequiresFuelingPort => false;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref podOnly, "podOnly");
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 不呼叫 base:原版的發射 gizmo 會走 CompLaunchable.TryLaunch,這裡改用自訂發射流程
            CompTransporter transporter = Transporter;
            if (transporter != null && !transporter.Groupable)
            {
                int selected = 0;
                foreach (object obj in Find.Selector.SelectedObjects)
                {
                    if (obj is ThingWithComps twc && twc.HasComp<CompTransporter>()) selected++;
                }
                if (selected > 1) yield break;
            }

            Command_Action launch = new Command_Action
            {
                defaultLabel = "CommandLaunchGroup".Translate(),
                defaultDesc = "CommandLaunchGroupDesc".Translate(),
                icon = LaunchCommandTex,
                alsoClickIfOtherInGroupClicked = false,
                action = delegate
                {
                    if (transporter.AnyInGroupHasAnythingLeftToLoad)
                    {
                        TaggedString text = "ConfirmSendNotCompletelyLoadedLaunchable".Translate() + ":\n";
                        text += transporter.leftToLoad.Select((TransferableOneWay x) => x.AnyThing.LabelCap).ToLineList(" -");
                        text += "\n\n" + "ConfirmSendLaunchAnyway".Translate();
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, delegate
                        {
                            StartChoosingDestinationLifter();
                        }));
                    }
                    else
                    {
                        StartChoosingDestinationLifter();
                    }
                },
            };
            AcceptanceReport report = CanLaunch();
            if (!report.Accepted) launch.Disable(report.Reason);
            yield return launch;

            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: End cooldown",
                    action = delegate { lastLaunchTick = Find.TickManager.TicksGame - Props.cooldownTicks; },
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Toggle pod-only",
                    action = delegate { podOnly = !podOnly; },
                };
            }
        }

        public override string CompInspectStringExtra()
        {
            string baseStr = base.CompInspectStringExtra();
            string stage = (podOnly ? "DMS_CrewLifter_StagePodOnly" : "DMS_CrewLifter_StageFull").Translate();
            return baseStr.NullOrEmpty() ? stage : baseStr + "\n" + stage;
        }

        /// <summary>
        /// 仿 CompLaunchable.StartChoosingDestination,但目的地選項改由 GetLifterOptionsAt 提供:
        /// 沒有地圖的地塊直接「降落」(生成地圖後選降落點),不再組成商隊而遺失升降艙。
        /// </summary>
        private void StartChoosingDestinationLifter()
        {
            PlanetTile origin = parent.Tile;
            CameraJumper.TryJump(CameraJumper.GetWorldTarget(new GlobalTargetInfo(origin)));
            Find.WorldSelector.ClearSelection();
            Find.WorldTargeter.BeginTargeting(ChoseWorldTargetLifter, canTargetTiles: true, TargeterMouseAttachment, closeWorldTabWhenFinished: true,
                delegate
                {
                    PlanetTile closest;
                    if (cachedLayer != Find.WorldSelector.SelectedLayer || cachedOrigin != origin)
                    {
                        cachedLayer = Find.WorldSelector.SelectedLayer;
                        cachedOrigin = origin;
                        closest = cachedClosest = cachedLayer.GetClosestTile_NewTemp(origin);
                    }
                    else closest = cachedClosest;
                    int maxEver = MaxLaunchDistanceEver(closest.Layer);
                    GenDraw.DrawWorldRadiusRing(closest, maxEver, CompPilotConsole.GetThrusterRadiusMat(closest));
                    int maxFuel = MaxLaunchDistanceAtFuelLevel(FuelLevel, PlanetLayer.Selected);
                    if (maxFuel < maxEver)
                        GenDraw.DrawWorldRadiusRing(closest, maxFuel, CompPilotConsole.GetFuelRadiusMat(closest));
                },
                LifterTargetingLabel, null, origin, showCancelButton: true);
        }

        private bool ChoseWorldTargetLifter(GlobalTargetInfo target)
        {
            if (!Transporter.LoadingInProgressOrReadyToLaunch) return true;
            cachedClosest = cachedOrigin = PlanetTile.Invalid;
            cachedLayer = null;
            if (!target.IsValid)
            {
                Messages.Message("MessageTransportPodsDestinationIsInvalid".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (target.HasWorldObject && !target.WorldObject.def.validLaunchTarget)
            {
                Messages.Message("MessageWorldObjectIsInvalid".Translate(target.WorldObject.Named("OBJECT")), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (ModsConfig.OdysseyActive && target.HasWorldObject && target.WorldObject.RequiresSignalJammerToReach)
            {
                Messages.Message("TransportPodDestinationRequiresSignalJammer".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            PlanetTile origin = parent.Tile;
            int dist = Find.WorldGrid.TraversalDistanceBetween(origin, target.Tile, passImpassable: true, int.MaxValue, canTraverseLayers: true);
            int maxEver = MaxLaunchDistanceEver(target.Tile.Layer);
            if (maxEver >= 0 && dist > maxEver)
            {
                Messages.Message("TransportPodDestinationBeyondMaximumRange".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (dist > MaxLaunchDistanceAtFuelLevel(FuelLevel, target.Tile.Layer))
            {
                Messages.Message("TransportPodNotEnoughFuel".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            List<FloatMenuOption> options = GetLifterOptionsAt(target.Tile).ToList();
            if (!options.Any())
            {
                Messages.Message("MessageTransportPodsDestinationIsInvalid".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (options.Count == 1)
            {
                if (options[0].Disabled) return false;
                options[0].action();
                return true;
            }
            Find.WindowStack.Add(new FloatMenu(options));
            return false;
        }

        /// <summary>
        /// 目的地選項:
        /// 1. 沒有地圖的地塊 → 直接降落 (生成地圖 + 選降落點)
        /// 2. 該地塊上的世界物件提供的原版選項 (既有地圖選格降落、拜訪/攻擊聚落…)
        /// 3. 都沒有 → 原版「貨物將遺失」
        /// </summary>
        private IEnumerable<FloatMenuOption> GetLifterOptionsAt(PlanetTile tile)
        {
            bool any = false;
            List<CompTransporter> pods = TransportersInGroup;
            if (TransportersArrivalAction_LifterLanding.CanLandAt(pods, tile))
            {
                any = true;
                yield return new FloatMenuOption("DMS_CrewLifter_LandHere".Translate(), delegate
                {
                    TryLaunchLifter(tile, new TransportersArrivalAction_LifterLanding());
                });
            }
            List<WorldObject> worldObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i].Tile != tile) continue;
                foreach (FloatMenuOption option in worldObjects[i].GetTransportersFloatMenuOptions(pods, TryLaunchLifter))
                {
                    any = true;
                    yield return option;
                }
            }
            if (!any && !Find.World.Impassable(tile))
            {
                yield return new FloatMenuOption("TransportPodsContentsWillBeLost".Translate(), delegate
                {
                    TryLaunchLifter(tile, null);
                });
            }
        }

        private TaggedString LifterTargetingLabel(GlobalTargetInfo target)
        {
            if (!target.IsValid) return null;
            PlanetTile origin = parent.Tile;
            if (target.Tile.Layer != origin.Layer && !origin.Layer.HasConnectionPathTo(target.Tile.Layer))
            {
                GUI.color = ColorLibrary.RedReadable;
                return "TransportPodDestinationNoPath".Translate(target.Tile.Layer.Def.Named("LAYER"));
            }
            if (ModsConfig.OdysseyActive)
            {
                WorldObject wo = Find.World.worldObjects.WorldObjectAt<WorldObject>(target.Tile);
                if (wo != null && wo.RequiresSignalJammerToReach)
                {
                    GUI.color = ColorLibrary.RedReadable;
                    return "TransportPodDestinationRequiresSignalJammer".Translate();
                }
            }
            int maxEver = MaxLaunchDistanceEver(target.Tile.Layer);
            int dist = Find.WorldGrid.TraversalDistanceBetween(origin, target.Tile, passImpassable: true, maxEver, canTraverseLayers: true);
            if (maxEver > 0 && dist > maxEver)
            {
                GUI.color = ColorLibrary.RedReadable;
                return "TransportPodDestinationBeyondMaximumRange".Translate();
            }
            if (target.HasWorldObject && !target.WorldObject.def.validLaunchTarget) return string.Empty;
            List<FloatMenuOption> options = GetLifterOptionsAt(target.Tile).ToList();
            if (!options.Any()) return string.Empty;

            float fuelNeeded = FuelNeededToLaunchAtDist(dist, target.Tile.Layer);
            string cost = string.Format("{0}: {1}", "Cost".Translate().CapitalizeFirst(), "FuelAmount".Translate(fuelNeeded, ThingDefOf.Chemfuel));
            if (fuelNeeded > FuelLevel)
                cost = (cost + string.Format(" ({0})", "TransportPodNotEnoughFuel".Translate())).Colorize(ColorLibrary.RedReadable);

            if (options.Count == 1)
            {
                if (options[0].Disabled) GUI.color = ColorLibrary.RedReadable;
                return options[0].Label + "\n" + cost;
            }
            if (target.WorldObject is MapParent mapParent)
                return "ClickToSeeAvailableOrders_WorldObject".Translate(mapParent.LabelCap) + "\n" + cost;
            return "ClickToSeeAvailableOrders_Empty".Translate() + "\n" + cost;
        }

        /// <summary>
        /// 仿 CompLaunchable.TryLaunch,差異:
        /// 1. 貨物資訊改用 LifterTransporterInfo 攜帶剩餘燃料
        /// 2. 依 podOnly 切換離場 skyfaller / sentTransporterDef / 世界物件
        /// 3. podOnly 時於原地留下底座
        /// (CompTransporter 設 max1PerGroup,群組只有自己,不處理多艙群組)
        /// </summary>
        private void TryLaunchLifter(PlanetTile destinationTile, TransportersArrivalAction arrivalAction)
        {
            if (!parent.Spawned)
            {
                Log.Error($"Tried to launch {parent}, but it's unspawned.");
                return;
            }
            CompTransporter transporter = Transporter;
            if (transporter == null || !CanLaunch()) return;

            Map map = parent.Map;
            int dist = Find.WorldGrid.TraversalDistanceBetween(map.Tile, destinationTile, passImpassable: true, int.MaxValue, canTraverseLayers: true);
            Current.Game.CurrentMap = map;
            if (dist > MaxLaunchDistanceAtFuelLevel(FuelLevel, destinationTile.Layer)) return;

            transporter.TryRemoveLord(map);
            int groupID = transporter.groupID;
            float fuelCost = Mathf.Max(FuelNeededToLaunchAtDist(dist, destinationTile.Layer), 1f);
            lastLaunchTick = Find.TickManager.TicksGame;
            CompRefuelable refuelable = Refuelable;
            refuelable?.ConsumeFuel(fuelCost);

            bool sendPodOnly = podOnly;
            ActiveTransporter active = (ActiveTransporter)ThingMaker.MakeThing(Props.activeTransporterDef ?? ThingDefOf.ActiveDropPod);
            LifterTransporterInfo info = new LifterTransporterInfo
            {
                fuel = refuelable?.Fuel ?? 0f,
                openDelay = Props.openDelay,
                sentTransporterDef = sendPodOnly && Props.podSentDef != null ? Props.podSentDef : parent.def,
            };
            active.Contents = info;
            info.innerContainer.TryAddRangeOrTransfer(transporter.GetDirectlyHeldThings(), canMergeWithExistingStacks: true, destroyLeftover: true);
            active.Rotation = parent.Rotation;

            ThingDef leavingDef = sendPodOnly && Props.podSkyfallerLeaving != null ? Props.podSkyfallerLeaving : (Props.skyfallerLeaving ?? ThingDefOf.DropPodLeaving);
            FlyShipLeaving leaving = (FlyShipLeaving)SkyfallerMaker.MakeSkyfaller(leavingDef, active);
            leaving.groupID = groupID;
            leaving.destinationTile = destinationTile;
            leaving.arrivalAction = arrivalAction;
            leaving.worldObjectDef = (sendPodOnly ? Props.podWorldObjectDef : null) ?? Props.worldObjectDef ?? WorldObjectDefOf.TravellingTransporters;

            IntVec3 pos = parent.Position;
            transporter.CleanUpLoadingVars(map);
            parent.Destroy();

            // 返回艙脫離底座升空:底座以無陣營建築留在原地,玩家可自行拆除回收
            if (sendPodOnly && Props.platformDef != null)
            {
                Thing platform = ThingMaker.MakeThing(Props.platformDef);
                platform.SetFactionDirect(null);
                GenSpawn.Spawn(platform, pos, map);
            }
            GenSpawn.Spawn(leaving, pos, map);
            CameraJumper.TryHideWorld();
        }
    }
}
