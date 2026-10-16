using RimWorld;
using Verse;
using Verse.Sound;

namespace DMS
{
    /// <summary>
    /// 升降艙落地後的開艙物件:依 def 的 LifterLandingExtension 於原地生成建築
    /// (完整升降艙 → 可再次發射並歸還剩餘燃料;返回艙 → 廢棄艙體),再把貨物卸在建築周圍。
    /// </summary>
    public class ActiveTransporter_Lifter : ActiveTransporter
    {
        protected override void Tick()
        {
            if (Contents == null || !Spawned) return;
            age++;
            if (age > Contents.openDelay) Open();
        }

        private void Open()
        {
            Map map = Map;
            IntVec3 pos = Position;
            ActiveTransporterInfo contents = Contents;
            LifterLandingExtension ext = def.GetModExtension<LifterLandingExtension>();

            Thing landed = null;
            if (ext?.spawnBuilding != null)
            {
                landed = ThingMaker.MakeThing(ext.spawnBuilding);
                landed.SetFactionDirect(Faction.OfPlayer);
                CompLaunchable_Lifter launchable = landed.TryGetComp<CompLaunchable_Lifter>();
                if (launchable != null)
                    launchable.podOnly = (contents as LifterTransporterInfo)?.podOnlyOverride ?? ext.markPodOnly;
                // 油量設為起飛後的剩餘量。建築 def 有 initialFuelPercent (建造時注入的燃料),
                // 新 MakeThing 出來的升降艙油箱是滿的,必須先清空再加回,否則每次落地都會白白加滿。
                CompRefuelable refuelable = landed.TryGetComp<CompRefuelable>();
                if (refuelable != null && contents is LifterTransporterInfo info)
                {
                    refuelable.ConsumeFuel(refuelable.Fuel);
                    if (info.fuel > 0f) refuelable.Refuel(info.fuel);
                }
                // 廢棄返回艙沒有油箱,殘餘燃料記在 CompStoredFuel,拆除時退還
                CompStoredFuel stored = landed.TryGetComp<CompStoredFuel>();
                if (stored != null && contents is LifterTransporterInfo storedInfo)
                    stored.fuel = storedInfo.fuel;
            }

            // 先把自己移除,建築才能落在同一格
            DeSpawn();
            CellRect occupied = CellRect.Empty;
            if (landed != null)
            {
                GenSpawn.Spawn(landed, pos, map, WipeMode.VanishOrMoveAside);
                occupied = landed.OccupiedRect();
            }

            // 貨物卸在建築周圍 (避開建築本身的格子);殖民者在非母星地圖落地時自動徵召,同原版空投艙
            for (int i = contents.innerContainer.Count - 1; i >= 0; i--)
            {
                Thing thing = contents.innerContainer[i];
                GenPlace.TryPlaceThing(thing, pos, map, ThingPlaceMode.Near, out Thing placed, null,
                    c => !occupied.Contains(c), Rot4.North);
                if (placed is Pawn pawn)
                {
                    if (pawn.RaceProps.Humanlike) TaleRecorder.RecordTale(TaleDefOf.LandedInPod, pawn);
                    if (pawn.IsColonist && pawn.Spawned && !map.IsPlayerHome) pawn.drafter.Drafted = true;
                    if (pawn.guest != null && pawn.guest.IsPrisoner) pawn.guest.WaitInsteadOfEscapingForDefaultTicks();
                }
            }
            contents.innerContainer.ClearAndDestroyContents();
            def.soundOpen?.PlayOneShot(new TargetInfo(pos, map));
            Destroy();
        }
    }
}
