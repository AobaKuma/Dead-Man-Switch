using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 現實穩定錨：有電時，範圍內的每一個 pawn（不分敵我，含平臺上收容的實體與棺內的人）掛上 fieldHediff，
    /// 異象實體與變異體再加掛 entityHediff。兩者都靠 HediffComp_Disappears 自然消退，離開範圍或停電後很快解除。
    /// 實際套用由 <see cref="MapComponent_RealityAnchor"/> 統一處理，一張地圖每個週期只掃一次 pawn。
    /// 上半部的懸浮錨柱由同一建築上的 <see cref="CompFloatingGraphic"/> 繪製。
    /// Reality anchor: while powered, every pawn in range (any faction, including entities held on platforms and
    /// pawns inside caskets) gets fieldHediff, and anomalous entities and mutants also get entityHediff. Both fade
    /// through HediffComp_Disappears, so they lift shortly after the pawn leaves or the anchor loses power. The
    /// application itself happens in <see cref="MapComponent_RealityAnchor"/>, which scans a map's pawns once per
    /// cycle however many anchors there are. The floating pillar is drawn by a <see cref="CompFloatingGraphic"/>
    /// on the same building.
    /// </summary>
    public class CompProperties_RealityAnchor : CompProperties
    {
        public float radius = 14.9f;

        public HediffDef fieldHediff;

        public HediffDef entityHediff;

        public CompProperties_RealityAnchor()
        {
            compClass = typeof(CompRealityAnchor);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;
            if (radius <= 0f)
                yield return $"{parentDef.defName}: CompProperties_RealityAnchor.radius must be > 0";
            if (fieldHediff == null)
                yield return $"{parentDef.defName}: CompProperties_RealityAnchor.fieldHediff is null";
            if (fieldHediff != null && !fieldHediff.HasComp(typeof(HediffComp_Disappears)))
                yield return $"{parentDef.defName}: {fieldHediff.defName} needs HediffComp_Disappears";
            if (entityHediff != null && !entityHediff.HasComp(typeof(HediffComp_Disappears)))
                yield return $"{parentDef.defName}: {entityHediff.defName} needs HediffComp_Disappears";
        }
    }

    public class CompRealityAnchor : ThingComp
    {
        private CompPowerTrader power;

        public CompProperties_RealityAnchor Props => (CompProperties_RealityAnchor)props;

        public bool Active => parent.Spawned && (power == null || power.PowerOn);

        public bool Covers(IntVec3 cell) => cell.InHorDistOf(parent.Position, Props.radius);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            power = parent.GetComp<CompPowerTrader>();
            parent.Map.GetComponent<MapComponent_RealityAnchor>()?.Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            map.GetComponent<MapComponent_RealityAnchor>()?.Deregister(this);
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            GenDraw.DrawRadiusRing(parent.Position, Props.radius);
        }

        public override string CompInspectStringExtra()
        {
            if (!Active) return null;
            MapComponent_RealityAnchor field = parent.Map.GetComponent<MapComponent_RealityAnchor>();
            if (field == null) return null;
            return "DMS_RealityAnchor_Field".Translate(field.AnchoredCount(this), field.SuppressedCount(this));
        }
    }

    /// <summary>
    /// 每張地圖一份：登記地圖上所有現實穩定錨，每 IntervalTicks 對錨場內的 pawn 套用或續期 hediff。
    /// 沒有任何運作中的錨時完全不做事。
    /// One per map: registers every reality anchor on the map and, every IntervalTicks, applies or renews the
    /// hediffs on pawns inside an active field. Does nothing while no anchor is running.
    /// </summary>
    public class MapComponent_RealityAnchor : MapComponent
    {
        public const int IntervalTicks = 90;

        /// <summary>續期長度：兩個週期，避免剛好在續期前一刻消退造成閃爍。Two cycles, so the hediff never lapses right before renewal.</summary>
        private const int HediffTicks = IntervalTicks * 2;

        private readonly List<CompRealityAnchor> anchors = new List<CompRealityAnchor>();

        private readonly List<Pawn> buffer = new List<Pawn>();

        // 只用來顯示檢視面板，不存檔。Inspect-pane counts only; not saved.
        private readonly Dictionary<CompRealityAnchor, int> anchoredCounts = new Dictionary<CompRealityAnchor, int>();
        private readonly Dictionary<CompRealityAnchor, int> suppressedCounts = new Dictionary<CompRealityAnchor, int>();

        public MapComponent_RealityAnchor(Map map) : base(map) { }

        public void Register(CompRealityAnchor anchor)
        {
            if (!anchors.Contains(anchor)) anchors.Add(anchor);
        }

        public void Deregister(CompRealityAnchor anchor)
        {
            anchors.Remove(anchor);
            anchoredCounts.Remove(anchor);
            suppressedCounts.Remove(anchor);
        }

        public int AnchoredCount(CompRealityAnchor anchor) => anchoredCounts.TryGetValue(anchor, out int n) ? n : 0;

        public int SuppressedCount(CompRealityAnchor anchor) => suppressedCounts.TryGetValue(anchor, out int n) ? n : 0;

        /// <summary>這一格是否位在任一運作中的錨場內。Whether a cell lies inside any active field.</summary>
        public CompRealityAnchor ActiveAnchorCovering(IntVec3 cell)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                CompRealityAnchor a = anchors[i];
                if (a.Active && a.Covers(cell)) return a;
            }
            return null;
        }

        public override void MapComponentTick()
        {
            if (anchors.Count == 0 || Find.TickManager.TicksGame % IntervalTicks != 0) return;

            anchoredCounts.Clear();
            suppressedCounts.Clear();
            bool anyActive = false;
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].Active) { anyActive = true; break; }
            }
            if (!anyActive) return;

            ApplyTo(map.mapPawns.AllPawnsSpawned);
            // 平臺上收容的實體、被搬運的人、棺內的人都不算 spawned，要另外掃。
            // Entities on holding platforms, carried pawns and casket occupants are unspawned and need their own pass.
            ApplyTo(map.mapPawns.AllPawnsUnspawned);
        }

        private void ApplyTo(IReadOnlyList<Pawn> pawns)
        {
            // AllPawnsUnspawned 回傳的是共用清單，先複製再處理，避免加 hediff 的副作用改到它。
            // AllPawnsUnspawned hands back a shared list; copy it before applying hediffs that might touch it.
            buffer.Clear();
            buffer.AddRange(pawns);
            for (int i = 0; i < buffer.Count; i++)
            {
                Pawn pawn = buffer[i];
                if (pawn.Dead || pawn.health == null) continue;

                CompRealityAnchor anchor = ActiveAnchorCovering(pawn.PositionHeld);
                if (anchor == null) continue;

                CompProperties_RealityAnchor props = anchor.Props;
                GiveOrRenew(pawn, props.fieldHediff);
                Increment(anchoredCounts, anchor);

                if (props.entityHediff != null && RealityAnchorUtility.IsTranscendentEntity(pawn))
                {
                    GiveOrRenew(pawn, props.entityHediff);
                    Increment(suppressedCounts, anchor);
                }
            }
        }

        private static void Increment(Dictionary<CompRealityAnchor, int> counts, CompRealityAnchor anchor)
        {
            counts.TryGetValue(anchor, out int n);
            counts[anchor] = n + 1;
        }

        private static void GiveOrRenew(Pawn pawn, HediffDef def)
        {
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff == null)
            {
                hediff = pawn.health.AddHediff(def);
                if (hediff == null) return;
            }
            HediffComp_Disappears disappears = hediff.TryGetComp<HediffComp_Disappears>();
            if (disappears != null) disappears.ticksToDisappear = HediffTicks;
        }
    }

    public static class RealityAnchorUtility
    {
        /// <summary>
        /// 錨場要壓制的「超凡造物」：異象實體（含血肉獸）與變異體（蹣跚者、食屍鬼等）。
        /// 兩個屬性在沒有 Anomaly 時都固定為 false，不需要另外判斷 DLC。
        /// The "transcendent creations" the field suppresses: anomalous entities (fleshbeasts included) and mutants
        /// (shamblers, ghouls and the like). Both properties are always false without Anomaly, so no DLC check is needed.
        /// </summary>
        public static bool IsTranscendentEntity(Pawn pawn) => pawn.RaceProps.IsAnomalyEntity || pawn.IsMutant;

        /// <summary>pawn 目前是否在錨場內（看身上的錨場 hediff）。Whether the pawn is currently inside a field, by its field hediff.</summary>
        public static bool IsAnchored(Pawn pawn) =>
            pawn?.health?.hediffSet != null && pawn.health.hediffSet.HasHediff(DMS_DefOf.DMS_RealityAnchored);
    }
}
