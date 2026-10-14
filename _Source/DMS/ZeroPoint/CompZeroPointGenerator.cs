using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 零點能發生器核心：以 CompRefuelable 裝填的觸媒（反粒子彈頭）激發，有電時每 ticksPerUnit 產出一個 thingToSpawn。
    /// 觸媒以「整枚」點燃：一枚彈頭點燃後轉成 yieldPerCatalyst 單位的內部充能，每產出一個扣一單位。
    /// 這樣 CompRefuelable 的燃料永遠是整數，原版補充邏輯（ceil 補滿）不會多塞一枚、白白浪費一整枚彈頭。
    /// 產物放在建築外圍一圈，優先疊到既有的同種堆上；四周都放不下時保留進度、每 250 ticks 再試。
    /// Zero-point generator core: excited by a catalyst loaded through CompRefuelable (antigrain warheads), it
    /// produces one thingToSpawn every ticksPerUnit while powered. Catalyst is ignited one whole warhead at a
    /// time: an ignited warhead becomes yieldPerCatalyst units of internal charge, and each unit produced spends
    /// one. The refuelable's fuel therefore always stays a whole number, so vanilla's round-up refuelling never
    /// overfills and wastes an entire warhead. Output goes on the ring of cells around the building, stacking
    /// onto an existing pile first; when there is no room the progress is kept and retried every 250 ticks.
    /// </summary>
    public class CompProperties_ZeroPointGenerator : CompProperties
    {
        public ThingDef thingToSpawn;

        /// <summary>一枚觸媒總共能產出的數量。Units produced per catalyst.</summary>
        public int yieldPerCatalyst = 25;

        /// <summary>產出一個所需的運轉時間。Running time per unit produced.</summary>
        public int ticksPerUnit = 144000;

        public CompProperties_ZeroPointGenerator()
        {
            compClass = typeof(CompZeroPointGenerator);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;
            if (thingToSpawn == null)
                yield return $"{parentDef.defName}: CompProperties_ZeroPointGenerator.thingToSpawn is null";
            if (yieldPerCatalyst <= 0)
                yield return $"{parentDef.defName}: CompProperties_ZeroPointGenerator.yieldPerCatalyst must be > 0";
            if (ticksPerUnit <= 0)
                yield return $"{parentDef.defName}: CompProperties_ZeroPointGenerator.ticksPerUnit must be > 0";
            if (parentDef.tickerType != TickerType.Normal)
                yield return $"{parentDef.defName}: CompZeroPointGenerator needs tickerType Normal";
            if (!parentDef.HasComp(typeof(CompRefuelable)))
                yield return $"{parentDef.defName}: CompZeroPointGenerator needs a CompRefuelable for its catalyst";
        }
    }

    public class CompZeroPointGenerator : ThingComp, ISingularityDriver
    {
        private const int RetryTicks = 250;

        /// <summary>已點燃的觸媒還能產出幾個。Units left in the ignited catalyst.</summary>
        private int charge;

        private int progressTicks;

        private CompRefuelable refuelable;
        private CompPowerTrader power;

        public CompProperties_ZeroPointGenerator Props => (CompProperties_ZeroPointGenerator)props;

        private bool Powered => power == null || power.PowerOn;

        private bool InVacuum => refuelable != null && !refuelable.Props.functionsInVacuum
            && parent.Spawned && parent.Position.GetVacuum(parent.Map) > 0f;

        /// <summary>還沒點燃、留在彈匣裡的整枚觸媒。Whole catalysts still in the magazine.</summary>
        private int StoredCatalyst => refuelable == null ? 0 : (int)refuelable.Fuel;

        /// <summary>充能加上彈匣裡的觸媒，總共還能產出幾個。Charge plus magazine, in units.</summary>
        public int UnitsRemaining => charge + StoredCatalyst * Props.yieldPerCatalyst;

        /// <summary>
        /// 正在產出：有電、不在真空、還有觸媒可燒，且沒有卡在產物放不下。奇點效果（CompSingularityGraphic）只在這時張開。
        /// Producing: powered, not in vacuum, catalyst left to burn, and not stalled for lack of room. The singularity
        /// effect (CompSingularityGraphic) only opens while this holds.
        /// </summary>
        public bool SingularityActive => parent.Spawned && Powered && !InVacuum && UnitsRemaining > 0
            && progressTicks <= Props.ticksPerUnit;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            refuelable = parent.GetComp<CompRefuelable>();
            power = parent.GetComp<CompPowerTrader>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref charge, "zpgCharge", 0);
            Scribe_Values.Look(ref progressTicks, "zpgProgressTicks", 0);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || !Powered || InVacuum) return;

            if (charge <= 0 && !TryIgniteCatalyst()) return;

            if (progressTicks < Props.ticksPerUnit)
            {
                progressTicks++;
                return;
            }

            // 進度已滿但上一次放不下：每 RetryTicks 再試，不必每 tick 掃一圈格子。
            // Full progress but the last attempt had no room: retry every RetryTicks rather than every tick.
            if (progressTicks > Props.ticksPerUnit && !parent.IsHashIntervalTick(RetryTicks)) return;

            if (TryProduce())
            {
                charge--;
                progressTicks = 0;
                if (charge <= 0 && StoredCatalyst <= 0 && parent.Faction == Faction.OfPlayer)
                {
                    Messages.Message("DMS_ZeroPointGenerator_CatalystSpent".Translate(parent.Named("GENERATOR")),
                        parent, MessageTypeDefOf.NeutralEvent);
                }
            }
            else
            {
                progressTicks = Props.ticksPerUnit + 1;
            }
        }

        private bool TryIgniteCatalyst()
        {
            if (refuelable == null || refuelable.Fuel < 1f) return false;
            refuelable.ConsumeFuel(1f);
            charge = Props.yieldPerCatalyst;
            return true;
        }

        private bool TryProduce()
        {
            if (!TryFindOutputCell(out IntVec3 cell)) return false;
            Thing thing = ThingMaker.MakeThing(Props.thingToSpawn);
            thing.stackCount = 1;
            return GenPlace.TryPlaceThing(thing, cell, parent.Map, ThingPlaceMode.Direct);
        }

        /// <summary>
        /// 外圍一圈中，先找能疊上去的同種堆，再找空地。格子不能有建築、也不能在牆或關著的門後面。
        /// On the outer ring, look for a pile of the same item with room first, then an empty cell. A cell must hold
        /// no building and must not sit behind a wall or a closed door.
        /// </summary>
        private bool TryFindOutputCell(out IntVec3 result)
        {
            Map map = parent.Map;
            ThingDef def = Props.thingToSpawn;
            IntVec3 empty = IntVec3.Invalid;

            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(parent))
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.GetEdifice(map) != null) continue;

                bool blocked = false;
                bool stackable = false;
                List<Thing> things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t.def.category != ThingCategory.Item) continue;
                    if (t.def == def && t.stackCount < def.stackLimit) stackable = true;
                    else blocked = true;
                }

                if (stackable && !blocked)
                {
                    result = c;
                    return true;
                }
                if (!blocked && !empty.IsValid) empty = c;
            }

            result = empty;
            return result.IsValid;
        }

        public override void PostDraw()
        {
            base.PostDraw();
            // 彈匣打空但還有充能時機器仍在運轉；原版的缺燃料圖示只看彈匣，所以由這裡改畫。
            // The machine keeps running on charge after the magazine empties; vanilla's out-of-fuel overlay only
            // looks at the magazine, so the def turns it off and this draws it instead.
            if (UnitsRemaining <= 0 && (refuelable == null || refuelable.allowAutoRefuel))
                parent.Map.overlayDrawer.DrawOverlay(parent, OverlayTypes.OutOfFuel);
        }

        public override string CompInspectStringExtra()
        {
            string text = "DMS_ZeroPointGenerator_Charge".Translate(charge, Props.yieldPerCatalyst, UnitsRemaining);
            if (UnitsRemaining <= 0)
                return text + "\n" + "DMS_ZeroPointGenerator_NoCatalyst".Translate().Colorize(ColoredText.WarningColor);
            if (!Powered || InVacuum)
                return text;
            if (progressTicks > Props.ticksPerUnit)
                return text + "\n" + "DMS_ZeroPointGenerator_NoRoom".Translate().Colorize(ColoredText.WarningColor);
            return text + "\n" + "NextSpawnedItemIn".Translate(GenLabel.ThingLabel(Props.thingToSpawn, null, 1)).Resolve() + ": "
                + (Props.ticksPerUnit - progressTicks).ToStringTicksToPeriod().Colorize(ColoredText.DateTimeColor);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DebugSettings.ShowDevGizmos) yield break;
            yield return new Command_Action
            {
                defaultLabel = "DEV: Finish current unit",
                action = () => progressTicks = Props.ticksPerUnit
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Clear charge",
                action = () => charge = 0
            };
        }
    }
}
