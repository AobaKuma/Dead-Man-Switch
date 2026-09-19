using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

namespace DMS
{
    /// <summary>
    /// 設施地板開口的警報反應：收到警報後，機械體從洞裡一隻隻跳出來，
    /// 做法比照原版巨坑（PitGate）——先生成在洞內、再用 PawnFlyer 拋到洞外落地。
    /// Alert response for facility floor openings: on alarm, mechs leap out of the hole one after
    /// another, the way vanilla's pit gate does it — spawned inside the hole, then thrown to a landing
    /// cell outside with a PawnFlyer.
    ///
    /// 兵力兩種給法：<see cref="pawnKinds"/> 有填就從裡面抽 <see cref="countRange"/> 隻；
    /// 沒填就依 <see cref="pointsRange"/> 用派系的 pawnGroupMakers 生一隊。
    /// 兩者都依地圖當下的警戒值（FFF MapComponent_AlertCounter）縮放：
    /// 警戒值 0% 取範圍下限，100% 取上限；也可以用 <see cref="pointsByAlertLevel"/> 自訂曲線。
    /// Either list <see cref="pawnKinds"/> and roll <see cref="countRange"/> of them, or leave it
    /// empty and let the faction's pawnGroupMakers build a squad worth <see cref="pointsRange"/>.
    /// Both scale with the map's current alert level (FFF MapComponent_AlertCounter): 0% alert takes
    /// the bottom of the range, 100% the top; <see cref="pointsByAlertLevel"/> can replace that curve.
    /// </summary>
    public class CompProperties_AlertEffector_HoleEmerge : CompProperties_AlertEffector
    {
        /// <summary>指定機種；空的就走點數。Explicit kinds; empty means points-based.</summary>
        public List<PawnKindDef> pawnKinds = new List<PawnKindDef>();

        public IntRange countRange = new IntRange(2, 4);

        /// <summary>警戒值 0% → min，100% → max。Alert 0% → min, 100% → max.</summary>
        public FloatRange pointsRange = new FloatRange(200f, 500f);

        /// <summary>
        /// 自訂曲線：x = 警戒值（0~100），y = 點數。有設就蓋過 <see cref="pointsRange"/>。
        /// Custom curve: x = alert level (0~100), y = points. Overrides <see cref="pointsRange"/> when set.
        /// </summary>
        public SimpleCurve pointsByAlertLevel;

        /// <summary>派系；null 用建築自己的，再沒有就用 DMS 遺留部隊。Faction; falls back to the parent's, then DMS_Legacy.</summary>
        public FactionDef spawnFactionDef;

        /// <summary>拋出用的 flyer，預設落地會短暫暈眩。Flyer used for the leap; the default stuns briefly on landing.</summary>
        public ThingDef flyerDef;

        /// <summary>整批跳完要花的時間。Time the whole batch takes to emerge.</summary>
        public int emergeDurationTicks = 300;

        /// <summary>落點距洞緣最多幾格。How far past the hole's edge a landing cell may be.</summary>
        public int landingRadius = 2;

        public SoundDef emergeSound;
        public EffecterDef emergeEffecter;

        /// <summary>跳出來後直接攻擊玩家。Assault the player once out.</summary>
        public bool assaultOnEmerge = true;

        public CompProperties_AlertEffector_HoleEmerge()
        {
            compClass = typeof(CompAlertEffector_HoleEmerge);
        }
    }

    public class CompAlertEffector_HoleEmerge : CompAlertEffector
    {
        public new CompProperties_AlertEffector_HoleEmerge Props => (CompProperties_AlertEffector_HoleEmerge)props;

        /// <summary>
        /// Signal 是全域廣播，只理會同一張地圖的警報，免得地表的感測器把口袋地圖裡的洞也觸發了。
        /// Signals are global; only honour alarms from this map so a surface sensor can't set off a
        /// hole down in a pocket map.
        /// </summary>
        public override void Notify_SignalReceived(Signal signal)
        {
            if (signal.args.TryGetArg("MAP", out Map signalMap) && signalMap != null && signalMap != parent.Map)
            {
                return;
            }
            base.Notify_SignalReceived(signal);
        }

        protected override void DoEffect()
        {
            if (!parent.Spawned) return;

            Map map = parent.Map;
            Faction faction = ResolveFaction();
            List<Pawn> pawns = GeneratePawns(faction, map, AlertLevelPct(map));
            if (pawns.Count == 0) return;

            // 洞內生成格：洞的佔地往內縮一圈，3x3 的洞就只剩正中央。
            // Spawn cells inside the hole: the footprint contracted by one; a 3x3 hole leaves the centre.
            CellRect inside = parent.OccupiedRect().ContractedBy(1);
            if (inside.Area <= 0) inside = CellRect.SingleCell(parent.Position);

            int landingRange = Mathf.Max(parent.def.Size.x, parent.def.Size.z) / 2 + Props.landingRadius;
            ThingDef flyerDef = Props.flyerDef ?? ThingDefOf.PawnFlyer_Stun;

            List<Thing> flyers = new List<Thing>();
            List<IntVec3> cells = new List<IntVec3>();
            foreach (Pawn pawn in pawns)
            {
                IntVec3 start = inside.RandomCell;
                if (!CellFinder.TryFindRandomCellNear(parent.Position, map, landingRange,
                        c => !c.Fogged(map) && c.Walkable(map) && !c.Impassable(map) && !parent.OccupiedRect().Contains(c),
                        out IntVec3 landing))
                {
                    landing = start;
                }

                GenSpawn.Spawn(pawn, start, map);
                pawn.rotationTracker.FaceCell(landing);
                flyers.Add(PawnFlyer.MakeFlyer(flyerDef, pawn, landing, null, null, flyWithCarriedThing: false,
                    start.ToVector3() + new Vector3(0f, 0f, -1f)));
                cells.Add(start);
            }

            float interval = Props.emergeDurationTicks.TicksToSeconds() / pawns.Count;
            SpawnRequest request = new SpawnRequest(flyers, cells, 1, interval);
            if (Props.assaultOnEmerge && faction != null)
            {
                request.lord = LordMaker.MakeNewLord(faction,
                    new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false,
                        useAvoidGridSmart: false, canSteal: false), map);
            }
            map.deferredSpawner.AddRequest(request);

            (Props.emergeSound ?? SoundDefOf.DroneTrapSpring).PlayOneShot(new TargetInfo(parent.Position, map));
            Props.emergeEffecter?.Spawn(parent.Position, map).Cleanup();

            if (faction != Faction.OfPlayer)
            {
                Messages.Message("DMS_HoleEmerge_Triggered".Translate(pawns.Count, parent.LabelCap),
                    new LookTargets(parent), MessageTypeDefOf.ThreatBig);
            }
        }

        private Faction ResolveFaction()
        {
            Faction faction = null;
            if (Props.spawnFactionDef != null)
            {
                faction = Find.FactionManager.FirstFactionOfDef(Props.spawnFactionDef);
            }
            return faction ?? parent.Faction ?? VaultRoomUtility.DefenderFaction;
        }

        /// <summary>
        /// 地圖目前的警戒值比例（0~1）。掃描器是先累加警戒值再廣播 Signal，所以這裡讀到的已經含本次觸發。
        /// The map's current alert level (0~1). Scanners bump the counter before broadcasting, so the
        /// value already includes the trigger that got us here.
        /// </summary>
        private static float AlertLevelPct(Map map)
        {
            return map.GetComponent<MapComponent_AlertCounter>()?.AlertLevelPct ?? 0f;
        }

        private float PointsFor(float alertPct)
        {
            if (Props.pointsByAlertLevel != null)
            {
                return Props.pointsByAlertLevel.Evaluate(alertPct * MapComponent_AlertCounter.MaxAlertLevel);
            }
            return Props.pointsRange.LerpThroughRange(alertPct);
        }

        private List<Pawn> GeneratePawns(Faction faction, Map map, float alertPct)
        {
            List<Pawn> pawns = new List<Pawn>();

            if (!Props.pawnKinds.NullOrEmpty())
            {
                int count = Mathf.RoundToInt(Mathf.Lerp(Props.countRange.min, Props.countRange.max, alertPct));
                for (int i = 0; i < count; i++)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        Props.pawnKinds.RandomElement(), faction, PawnGenerationContext.NonPlayer, map.Tile));
                    if (pawn != null) pawns.Add(pawn);
                }
                return pawns;
            }

            if (faction == null) return pawns;

            PawnGroupMakerParms parms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                faction = faction,
                points = PointsFor(alertPct),
                tile = map.Tile,
            };
            pawns.AddRange(PawnGroupMakerUtility.GeneratePawns(parms));
            return pawns;
        }
    }
}
