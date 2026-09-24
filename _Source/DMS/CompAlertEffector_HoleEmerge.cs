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
    /// 做法比照原版巨坑（PitGate）：先生成在洞內、再用 PawnFlyer 拋到洞外落地。
    /// Alert response for facility floor openings: on alarm, mechs leap out of the hole one after
    /// another, the way vanilla's pit gate does it: spawned inside the hole, then thrown to a landing
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

        /// <summary>
        /// 跳出來後的行動。false（預設）＝警報回應（<see cref="LordJob_AlarmResponse"/>）：
        /// 當下走得到警報位置就前往，之後每次警報重新判定，只打看得到的敵人。
        /// true ＝舊行為：直接進攻殖民地，知道殖民者在哪。
        /// What the squad does once out. false (default) = alarm response (<see cref="LordJob_AlarmResponse"/>):
        /// head for the alarm if it can be reached right then, re-judge on every later alarm, and only fight
        /// what it can see. true = the old behaviour: assault the colony, knowing where the colonists are.
        /// </summary>
        public bool assaultOnEmerge = false;

        public CompProperties_AlertEffector_HoleEmerge()
        {
            compClass = typeof(CompAlertEffector_HoleEmerge);
        }
    }

    public class CompAlertEffector_HoleEmerge : CompAlertEffector
    {
        public new CompProperties_AlertEffector_HoleEmerge Props => (CompProperties_AlertEffector_HoleEmerge)props;

        // 同地圖過濾已由 FFF 的 CompAlertEffector 基類（SignalMapUtility）處理。
        // Same-map filtering now lives in FFF's CompAlertEffector base (SignalMapUtility).

        /// <summary>
        /// 觸發這次 DoEffect 的警報位置。基類的 DoEffect 不帶訊號，所以在這裡先記下來；
        /// 基類是在 Notify_SignalReceived 裡同步呼叫 DoEffect，呼叫完就清掉。
        /// Position of the alarm behind the current DoEffect. The base DoEffect gets no signal, so it is noted
        /// here; the base calls DoEffect synchronously inside Notify_SignalReceived, and it is cleared after.
        /// </summary>
        private IntVec3 triggeringAlarmCell = IntVec3.Invalid;

        public override void Notify_SignalReceived(Signal signal)
        {
            triggeringAlarmCell = signal.tag == Props.listenSignal && parent.Spawned
                ? AlertResponseUtility.AlarmCell(signal, parent.Map)
                : IntVec3.Invalid;
            try
            {
                base.Notify_SignalReceived(signal);
            }
            finally
            {
                triggeringAlarmCell = IntVec3.Invalid;
            }
        }

        protected override void DoEffect()
        {
            if (!parent.Spawned) return;

            Map map = parent.Map;
            Faction faction = AlertResponseUtility.ResolveFaction(this, Props.spawnFactionDef);
            List<Pawn> pawns = AlertResponseUtility.GeneratePawns(faction, map, AlertResponseUtility.AlertLevelPct(map),
                Props.pawnKinds, Props.countRange, Props.pointsRange, Props.pointsByAlertLevel);
            if (pawns.Count == 0) return;

            // 洞內生成格：洞的佔地往內縮一圈，3x3 的洞就只剩正中央。
            // Spawn cells inside the hole: the footprint contracted by one; a 3x3 hole leaves the centre.
            CellRect inside = parent.OccupiedRect().ContractedBy(1);
            if (inside.Area <= 0) inside = CellRect.SingleCell(parent.Position);

            // 落地格：洞緣往外 landingRadius 格內、可站立的格子。
            // 之前的版本要求落地格「未被戰爭迷霧覆蓋」，但洞多半是被遠處的掃描器觸發、
            // 周圍還在迷霧裡，於是一格都找不到、退回洞內生成，機體就永遠卡在洞裡。
            // 迷霧不影響落地（PawnFlyer 落地時直接 TryDrop 到 destCell），所以不再檢查；
            // 真的一格都沒有時放棄本次觸發，寧可不出兵也不要把機體塞進洞裡。
            // Landing cells: standable cells within landingRadius of the hole's edge.
            // The old check also demanded the cell be unfogged, but holes are usually set off by a
            // distant scanner while their surroundings are still fogged, so nothing qualified, the
            // fallback landed the pawn back inside the hole, and it stayed stuck there. Fog doesn't
            // affect landing (PawnFlyer simply TryDrops onto destCell), so it is no longer tested;
            // if there is genuinely no cell at all the trigger is abandoned rather than spawning
            // pawns into the hole.
            List<IntVec3> landingCells = FindLandingCells(map);
            if (landingCells.Count == 0)
            {
                Log.Warning($"[DMS] {parent.LabelCap} at {parent.Position}: no standable cell around the hole to emerge onto; alert response skipped.");
                foreach (Pawn pawn in pawns)
                {
                    Find.WorldPawns.PassToWorld(pawn, RimWorld.Planet.PawnDiscardDecideMode.Discard);
                }
                return;
            }

            ThingDef flyerDef = Props.flyerDef ?? ThingDefOf.PawnFlyer_Stun;

            List<Thing> flyers = new List<Thing>();
            List<IntVec3> cells = new List<IntVec3>();
            foreach (Pawn pawn in pawns)
            {
                IntVec3 start = inside.RandomCell;
                IntVec3 landing = landingCells.RandomElement();

                GenSpawn.Spawn(pawn, start, map);
                pawn.rotationTracker.FaceCell(landing);
                flyers.Add(PawnFlyer.MakeFlyer(flyerDef, pawn, landing, null, null, flyWithCarriedThing: false,
                    start.ToVector3() + new Vector3(0f, 0f, -1f)));
                cells.Add(start);
            }

            float interval = Props.emergeDurationTicks.TicksToSeconds() / pawns.Count;
            SpawnRequest request = new SpawnRequest(flyers, cells, 1, interval);
            if (faction != null)
            {
                // 警報回應：崗位在成員落地後才依當下的可達性決定（見 LordToil_AlarmResponse）。
                // 不是被訊號叫出來的（triggeringAlarmCell 無效）就原地待命，等下一次警報。
                // Alarm response: each member's post is judged by reachability once it lands (see
                // LordToil_AlarmResponse). Not summoned by a signal (triggeringAlarmCell invalid) → hold in place
                // until the next alarm.
                LordJob lordJob = Props.assaultOnEmerge
                    ? new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false,
                        useAvoidGridSmart: false, canSteal: false)
                    : new LordJob_AlarmResponse(triggeringAlarmCell, Props.listenSignal);
                request.lord = LordMaker.MakeNewLord(faction, lordJob, map);
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

        /// <summary>
        /// 洞緣外 landingRadius 格內所有可站立的格子；找不到就逐圈往外擴到 landingRadius + 4。
        /// All standable cells within landingRadius outside the footprint, widening ring by ring up to
        /// landingRadius + 4 when the nearest ring is fully blocked.
        /// </summary>
        private List<IntVec3> FindLandingCells(Map map)
        {
            CellRect footprint = parent.OccupiedRect();
            List<IntVec3> cells = new List<IntVec3>();
            for (int radius = Mathf.Max(Props.landingRadius, 1); radius <= Props.landingRadius + 4; radius++)
            {
                foreach (IntVec3 c in footprint.ExpandedBy(radius))
                {
                    if (!c.InBounds(map) || footprint.Contains(c)) continue;
                    if (!c.Standable(map)) continue;
                    if (c.GetEdifice(map) is Building_Door door && !door.Open) continue;
                    cells.Add(c);
                }
                if (cells.Count > 0) break;
            }
            return cells;
        }
    }
}
