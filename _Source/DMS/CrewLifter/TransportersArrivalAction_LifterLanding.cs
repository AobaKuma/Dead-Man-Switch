using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 升降艙降落在沒有地圖的地塊:抵達時直接於該地塊生成臨時地圖 (原版商隊營地 WorldObjectDefOf.Camp),
    /// 然後讓玩家在地圖上用瞄準器選擇降落點;取消瞄準則落在自動挑選的備用點。
    /// </summary>
    public class TransportersArrivalAction_LifterLanding : TransportersArrivalAction
    {
        public override bool GeneratesMap => true;

        public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport report = base.StillValid(pods, destinationTile);
            if (!report) return report;
            // 飛行途中若已有其他地圖生成在該地塊 (例如商隊紮營),就直接落在既有地圖
            if (Current.Game.FindMap(destinationTile) != null) return true;
            return CanLandAt(pods, destinationTile, requireNoMapParent: false);
        }

        public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> pods, PlanetTile tile)
        {
            return Current.Game.FindMap(tile) == null;
        }

        /// <summary>發射前的選項判定:可通行的地表地塊、尚無地圖、艙內至少一名未倒地殖民者。</summary>
        public static FloatMenuAcceptanceReport CanLandAt(IEnumerable<IThingHolder> pods, PlanetTile tile, bool requireNoMapParent = true)
        {
            if (Find.World.Impassable(tile)) return false;
            if (!tile.LayerDef.canFormCaravans) return false;
            if (requireNoMapParent && Find.WorldObjects.AnyMapParentAt(tile)) return false;
            if (!SettleInEmptyTileUtility.CanCreateMapAt(tile)) return false;
            if (!TransportersArrivalActionUtility.AnyNonDownedColonist(pods)) return false;
            return true;
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            // TravellingTransporters 在 Arrived 回傳後會清空這個 list,瞄準期間要自己抓住
            List<ActiveTransporterInfo> held = new List<ActiveTransporterInfo>(transporters);
            Thing lookTarget = TransportersArrivalActionUtility.GetLookTarget(held);

            Map map = GetOrGenerateLandingMap(tile);

            // 用起飛時的 sentTransporterDef 畫幽靈預覽 (整機或返回艙),尺寸也以它為準
            ThingDef ghostDef = held.Count > 0 ? held[0].sentTransporterDef : null;
            IntVec2 size = ghostDef?.Size ?? IntVec2.One;
            IntVec3 fallback = FindFallbackCell(map, size);

            Current.Game.CurrentMap = map;
            CameraJumper.TryHideWorld();
            CameraJumper.TryJump(fallback, map);
            Find.TickManager.Pause();

            bool landed = false;
            void Land(IntVec3 cell)
            {
                if (landed) return;
                landed = true;
                // 理論上瞄準期間強制暫停,地圖不會被移除;萬一被移除就重新生成並落在備用點
                if (map == null || !Find.Maps.Contains(map))
                {
                    Log.Warning("[DMS] Lifter landing map was removed while choosing a landing spot; regenerating.");
                    map = GetOrGenerateLandingMap(tile);
                    cell = FindFallbackCell(map, size);
                }
                TransportersArrivalActionUtility.RemovePawnsFromWorldPawns(held);
                for (int i = 0; i < held.Count; i++)
                    DropPodUtility.MakeDropPodAt(cell, map, held[i]);
                Messages.Message("MessageTransportPodsArrived".Translate(), lookTarget, MessageTypeDefOf.TaskCompletion);
            }

            Find.Targeter.BeginTargeting(TargetingParameters.ForCell(),
                x => Land(x.Cell),
                x =>
                {
                    if (ghostDef == null) return;
                    Color col = CanLandInCell(x.Cell, map, size) ? Designator_Place.CanPlaceColor : Designator_Place.CannotPlaceColor;
                    GhostDrawer.DrawGhostThing(x.Cell, Rot4.North, ghostDef, ghostDef.graphic, col, AltitudeLayer.Blueprint);
                },
                x =>
                {
                    bool ok = CanLandInCell(x.Cell, map, size);
                    if (!ok) Messages.Message("DMS_CrewLifter_CannotLandHere".Translate(), MessageTypeDefOf.RejectInput, false);
                    return ok;
                },
                null,
                // 取消瞄準 (Esc/右鍵) 也要把人放下來,否則艙內乘員會隨閉包消失
                () => Land(fallback),
                CompLaunchable.TargeterMouseAttachment,
                playSoundOnAction: true,
                null,
                // 瞄準期間強制暫停:臨時地圖在沒有玩家 pawn 時會被 tick 移除,也避免自動存檔漏掉艙內乘員
                x =>
                {
                    if (!Find.TickManager.Paused) Find.TickManager.Pause();
                });
            Messages.Message("DMS_CrewLifter_ChooseLanding".Translate(), MessageTypeDefOf.NeutralEvent, false);
        }

        private static Map GetOrGenerateLandingMap(PlanetTile tile)
        {
            Map map = Current.Game.FindMap(tile);
            if (map != null) return map;
            WorldObjectDef siteDef = WorldObjectDefOf.Camp;
            map = GetOrGenerateMapUtility.GetOrGenerateMap(tile, siteDef.overrideMapSize ?? Find.World.info.initialMapSize, siteDef);
            map.Parent.SetFaction(Faction.OfPlayer);
            return map;
        }

        /// <summary>整個佔地範圍都必須是可見、可站立、非厚岩頂且沒有建築/skyfaller 的格子。</summary>
        public static bool CanLandInCell(IntVec3 center, Map map, IntVec2 size)
        {
            foreach (IntVec3 c in GenAdj.OccupiedRect(center, Rot4.North, size))
            {
                if (!c.InBounds(map) || c.Fogged(map)) return false;
                if (!DropCellFinder.IsGoodDropSpot(c, map, allowFogged: false, canRoofPunch: true, allowIndoors: true)) return false;
            }
            return true;
        }

        private static IntVec3 FindFallbackCell(Map map, IntVec2 size)
        {
            if (DropCellFinder.TryFindDropSpotNear(map.Center, map, out IntVec3 cell, allowFogged: false, canRoofPunch: true, 60,
                    allowIndoors: false, size, mustBeReachableFromCenter: false))
                return cell;
            return DropCellFinder.RandomDropSpot(map);
        }
    }
}
