using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DMS
{
    /// <summary>
    /// 深層礦脈採盡後的一種產出，依 weight 加權抽選。
    /// One output once the deep deposits run dry, picked by weight.
    /// </summary>
    public class HydraulicFracturingYield
    {
        public ThingDef thingDef;
        public int count = 1;
        public float weight = 1f;
    }

    /// <summary>
    /// 水壓裂解採礦機：不需要人操作的自動深鑽井。有電時每 ticksPerPortion 產出一份：
    /// scanRadius 內有深層礦脈就照原版深鑽井的份量開採並扣除礦量；採盡後若地圖有岩床，改從 fallbackYields 加權抽一項。
    /// 每產出一份，就把 pollutionRadius 內最近的 cellsPollutedPerPortion 格土地汙染掉（需要 Biotech）。
    /// 產物用 ThingPlaceMode.Near 放在建築旁；放不下時保留進度，250 ticks 後再試。
    /// 運轉中會像原版蒸氣間歇泉一樣間歇噴出蒸氣（每產出一份也噴一次），但不像間歇泉那樣推送熱量。
    /// Hydraulic fracturing rig: an automatic deep drill that needs no operator. While powered it produces one portion
    /// every ticksPerPortion. If a deep deposit lies within scanRadius it mines a vanilla deep-drill portion and draws
    /// the deposit down; once the deposits are gone, and the map has bedrock, it rolls one entry of fallbackYields by
    /// weight instead. Every portion pollutes the nearest cellsPollutedPerPortion cells within pollutionRadius
    /// (needs Biotech). Output goes beside the building through ThingPlaceMode.Near; when there is no room the
    /// progress is kept and it tries again 250 ticks later. While running it sprays steam on and off like a vanilla
    /// steam geyser (and once per portion), without the geyser's heat.
    /// </summary>
    public class CompProperties_HydraulicFracturing : CompProperties
    {
        /// <summary>產出一份所需的運轉時間。Running time per portion.</summary>
        public int ticksPerPortion = 15000;

        /// <summary>搜尋深層礦脈的半徑（原版深鑽井約 2.6）。Radius searched for deep deposits (a vanilla deep drill reaches about 2.6).</summary>
        public float scanRadius = 3.9f;

        /// <summary>開採深層礦脈時的產量倍率。Yield multiplier when mining a deep deposit.</summary>
        public float yieldFactor = 1f;

        /// <summary>礦脈採盡後的產出。Output once the deposits are exhausted.</summary>
        public List<HydraulicFracturingYield> fallbackYields;

        public float pollutionRadius = 14.9f;

        public int cellsPollutedPerPortion = 6;

        /// <summary>運轉時兩次噴發的間隔（原版間歇泉 500~2000）。Ticks between sprays while running (a vanilla geyser uses 500~2000).</summary>
        public IntRange sprayIntervalTicks = new IntRange(500, 2000);

        /// <summary>每次噴發的長度（原版間歇泉 200~500）。Length of one spray (a vanilla geyser uses 200~500).</summary>
        public IntRange sprayDurationTicks = new IntRange(200, 500);

        /// <summary>噴發時每 tick 冒出一團蒸氣的機率（原版間歇泉 0.6）。Chance per tick of a puff while spraying (a vanilla geyser uses 0.6).</summary>
        public float sprayThickness = 0.6f;

        /// <summary>
        /// 蒸氣冒出的位置，相對於建築中心（部件靜止時）。建築上有 CompFloatingGraphic 時會跟著部件一起升降。
        /// Where the puffs come out, relative to the building's center with the part at rest. Follows the part up and
        /// down when the building has a CompFloatingGraphic.
        /// </summary>
        public Vector3 sprayOffset = Vector3.zero;

        public SoundDef spraySound;

        public CompProperties_HydraulicFracturing()
        {
            compClass = typeof(CompHydraulicFracturing);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;
            if (ticksPerPortion <= 0)
                yield return $"{parentDef.defName}: CompProperties_HydraulicFracturing.ticksPerPortion must be > 0";
            if (scanRadius <= 0f || scanRadius > GenRadial.MaxRadialPatternRadius)
                yield return $"{parentDef.defName}: CompProperties_HydraulicFracturing.scanRadius must be in (0, {GenRadial.MaxRadialPatternRadius}]";
            if (pollutionRadius > GenRadial.MaxRadialPatternRadius)
                yield return $"{parentDef.defName}: CompProperties_HydraulicFracturing.pollutionRadius must be <= {GenRadial.MaxRadialPatternRadius}";
            if (fallbackYields != null)
            {
                foreach (HydraulicFracturingYield y in fallbackYields)
                {
                    if (y.thingDef == null)
                        yield return $"{parentDef.defName}: a fallbackYields entry has no thingDef";
                    else if (y.count <= 0 || y.weight <= 0f)
                        yield return $"{parentDef.defName}: fallbackYields entry {y.thingDef.defName} needs count > 0 and weight > 0";
                }
            }
            if (sprayIntervalTicks.min <= 0 || sprayDurationTicks.min <= 0)
                yield return $"{parentDef.defName}: CompProperties_HydraulicFracturing spray intervals and durations must be > 0";
            if (parentDef.tickerType != TickerType.Normal)
                yield return $"{parentDef.defName}: CompHydraulicFracturing needs tickerType Normal";
        }
    }

    public class CompHydraulicFracturing : ThingComp, IFloatingGraphicDriver
    {
        private static readonly Color PollutionRingColor = new Color(0.6f, 0.75f, 0.2f);

        private int progressTicks;

        /// <summary>上一次產出因為旁邊沒空位而失敗。不存檔，只給檢視字串用。Last attempt found no room; inspect string only, not saved.</summary>
        private bool noRoom;

        /// <summary>
        /// 有電且有東西可採。每 250 ticks 更新一次，噴發每 tick 只讀這個值，不必每 tick 掃描礦脈。不存檔。
        /// Powered with something to mine. Refreshed every 250 ticks so the per-tick spray never scans for deposits. Not saved.
        /// </summary>
        private bool working;

        // 噴發狀態同原版間歇泉，不存檔。Spray state, not saved, like the vanilla geyser's.
        private int ticksUntilSpray = -1;
        private int sprayTicksLeft;
        private Sustainer spraySustainer;

        private CompPowerTrader power;
        private CompFloatingGraphic floating;

        public CompProperties_HydraulicFracturing Props => (CompProperties_HydraulicFracturing)props;

        private bool Powered => power == null || power.PowerOn;

        private bool HasFallback => parent.Map.Biome.hasBedrock && !Props.fallbackYields.NullOrEmpty();

        private bool CanFracture => TryGetDeposit(out _, out _, out _) || HasFallback;

        // 只在實際開採時浮起。The floating part rises only while it is actually mining.
        public bool FloatingGraphicActive => working;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            power = parent.GetComp<CompPowerTrader>();
            floating = parent.GetComp<CompFloatingGraphic>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref progressTicks, "hfProgressTicks", 0);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            // 同原版深鑽井：拆下搬走後進度歸零。Like the vanilla deep drill, progress resets when it is uninstalled.
            progressTicks = 0;
            noRoom = false;
            working = false;
            EndSpray();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned) return;

            if (parent.IsHashIntervalTick(GenTicks.TickRareInterval))
                TickProduction();

            TickSpray();
        }

        private void TickProduction()
        {
            working = Powered && CanFracture;
            if (!working) return;

            progressTicks = Mathf.Min(progressTicks + GenTicks.TickRareInterval, Props.ticksPerPortion);
            if (progressTicks < Props.ticksPerPortion) return;

            noRoom = !TryProducePortion();
            if (!noRoom) progressTicks = 0;
        }

        /// <summary>
        /// 同原版 IntermittentSteamSprayer 的節奏，但只在運轉中噴發；停電或停工時立刻收住。
        /// Same rhythm as vanilla's IntermittentSteamSprayer, but only while running; losing power or work stops it at once.
        /// </summary>
        private void TickSpray()
        {
            if (sprayTicksLeft > 0)
            {
                sprayTicksLeft--;
                if (Rand.Value < Props.sprayThickness)
                {
                    Vector3 nozzle = parent.TrueCenter() + Props.sprayOffset;
                    if (floating != null)
                        nozzle.z += floating.CurrentRise;
                    FleckMaker.ThrowAirPuffUp(nozzle, parent.Map);
                }
                if (sprayTicksLeft <= 0 || !working)
                    EndSpray();
                return;
            }

            if (!working) return;
            if (ticksUntilSpray < 0)
                ticksUntilSpray = Props.sprayIntervalTicks.RandomInRange;
            if (--ticksUntilSpray <= 0)
                StartSpray();
        }

        private void StartSpray()
        {
            sprayTicksLeft = Props.sprayDurationTicks.RandomInRange;
            ticksUntilSpray = -1;
            if (Props.spraySound != null && spraySustainer == null)
                spraySustainer = Props.spraySound.TrySpawnSustainer(new TargetInfo(parent.Position, parent.Map));
        }

        private void EndSpray()
        {
            sprayTicksLeft = 0;
            ticksUntilSpray = -1;
            if (spraySustainer != null)
            {
                spraySustainer.End();
                spraySustainer = null;
            }
        }

        /// <summary>
        /// 由內往外找 scanRadius 內第一個有深層礦脈的格子。
        /// Finds the nearest cell within scanRadius that holds a deep deposit.
        /// </summary>
        private bool TryGetDeposit(out ThingDef resDef, out int countPresent, out IntVec3 cell)
        {
            Map map = parent.Map;
            int num = GenRadial.NumCellsInRadius(Props.scanRadius);
            for (int i = 0; i < num; i++)
            {
                IntVec3 c = parent.Position + GenRadial.RadialPattern[i];
                if (!c.InBounds(map)) continue;
                ThingDef def = map.deepResourceGrid.ThingDefAt(c);
                if (def != null)
                {
                    resDef = def;
                    countPresent = map.deepResourceGrid.CountAt(c);
                    cell = c;
                    return true;
                }
            }
            resDef = null;
            countPresent = 0;
            cell = IntVec3.Invalid;
            return false;
        }

        private bool TryProducePortion()
        {
            Map map = parent.Map;
            bool fromDeposit = TryGetDeposit(out ThingDef resDef, out int countPresent, out IntVec3 cell);
            int taken = 0;
            Thing thing;
            if (fromDeposit)
            {
                taken = Mathf.Min(countPresent, resDef.deepCountPerPortion);
                thing = ThingMaker.MakeThing(resDef);
                thing.stackCount = Mathf.Max(1, GenMath.RoundRandom(taken * Props.yieldFactor));
            }
            else if (HasFallback)
            {
                HydraulicFracturingYield yield = Props.fallbackYields.RandomElementByWeight(y => y.weight);
                thing = ThingMaker.MakeThing(yield.thingDef);
                thing.stackCount = yield.count;
            }
            else
            {
                return false;
            }

            CellRect rect = parent.OccupiedRect();
            if (!GenPlace.TryPlaceThing(thing, parent.Position, map, ThingPlaceMode.Near, null, c => !rect.Contains(c)))
                return false;

            if (fromDeposit)
            {
                map.deepResourceGrid.SetAt(cell, resDef, countPresent - taken);
                if (!TryGetDeposit(out _, out _, out _) && parent.Faction == Faction.OfPlayer)
                {
                    string key = HasFallback ? "DMS_HydraulicFracturing_Exhausted" : "DMS_HydraulicFracturing_ExhaustedNoFallback";
                    Messages.Message(key.Translate(parent.Named("RIG")), parent, MessageTypeDefOf.TaskCompletion);
                }
            }

            Pollute();
            // 排出泥漿時噴一次。A spray goes up with every discharge of slurry.
            if (sprayTicksLeft <= 0)
                StartSpray();
            return true;
        }

        /// <summary>
        /// 由內往外汙染 pollutionRadius 內還沒被汙染的格子，作法同原版 CompToxifier。
        /// Pollutes the nearest clean cells within pollutionRadius, the same way vanilla's CompToxifier does.
        /// </summary>
        private void Pollute()
        {
            if (!ModsConfig.BiotechActive || Props.cellsPollutedPerPortion <= 0) return;

            Map map = parent.Map;
            int num = GenRadial.NumCellsInRadius(Props.pollutionRadius);
            int polluted = 0;
            for (int i = 0; i < num && polluted < Props.cellsPollutedPerPortion; i++)
            {
                IntVec3 c = parent.Position + GenRadial.RadialPattern[i];
                if (c.InBounds(map) && c.CanPollute(map))
                {
                    c.Pollute(map);
                    polluted++;
                }
            }

            if (polluted > 0)
            {
                FleckMaker.Static(parent.TrueCenter(), map, FleckDefOf.Fleck_ToxifierPollutionSource);
                SoundDefOf.Toxifier_Pollute.PlayOneShot(parent);
            }
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            // 掃描範圍由 def 的 specialDisplayRadius 畫；這裡補上汙染範圍。
            // The def's specialDisplayRadius draws the scan radius; this adds the pollution radius.
            if (ModsConfig.BiotechActive && Props.cellsPollutedPerPortion > 0)
                GenDraw.DrawRadiusRing(parent.Position, Props.pollutionRadius, PollutionRingColor);
        }

        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned) return null;

            string text;
            if (TryGetDeposit(out ThingDef resDef, out _, out _))
                text = "ResourceBelow".Translate() + ": " + resDef.LabelCap;
            else if (HasFallback)
                text = "DMS_HydraulicFracturing_Bedrock".Translate();
            else
                return "DeepDrillNoResources".Translate();

            text += "\n" + "ProgressToNextPortion".Translate() + ": " + ((float)progressTicks / Props.ticksPerPortion).ToStringPercent("F0");
            if (noRoom)
                text += "\n" + "DMS_HydraulicFracturing_NoRoom".Translate().Colorize(ColoredText.WarningColor);
            return text;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DebugSettings.ShowDevGizmos) yield break;
            yield return new Command_Action
            {
                defaultLabel = "DEV: Produce portion",
                action = () =>
                {
                    noRoom = !TryProducePortion();
                    if (!noRoom) progressTicks = 0;
                }
            };
        }
    }
}
