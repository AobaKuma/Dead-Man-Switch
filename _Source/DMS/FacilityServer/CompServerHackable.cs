using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DMS
{
    public class CompProperties_ServerHackable : CompProperties_Hackable
    {
        /// <summary>可駭入的次數，生成時擲一次。Number of hack stages, rolled once on spawn.</summary>
        public IntRange stageCount = IntRange.One;

        /// <summary>每完成一階段增加的防禦值。Defence added per completed stage.</summary>
        public float defencePerStage;

        // ── 警報 / Alarm ──
        public float alertIncrement = 35f;
        [NoTranslate] public string alarmSignal = FacilityAlarmUtility.DefaultSignal;

        /// <summary>地圖上沒有警報反應建築時直接叫的援軍；null = 不保底。Called when nothing on the map answers; null = none.</summary>
        public RaidWaveDef fallbackRaidWave;
        public SoundDef alarmSound;
        public EffecterDef alarmEffecter;

        // ── 獎勵 / Rewards ──
        public List<ServerHackReward> rewards = new List<ServerHackReward>();
        public ThingDef fallbackThing;

        public CompProperties_ServerHackable()
        {
            compClass = typeof(CompServerHackable);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef)) yield return e;
            if (stageCount.min < 1) yield return $"{parentDef.defName}: stageCount must be >= 1";
            if (rewards.NullOrEmpty() && fallbackThing == null)
                yield return $"{parentDef.defName}: CompProperties_ServerHackable has no rewards and no fallbackThing";
            if (parentDef.tickerType != TickerType.Normal)
                yield return $"{parentDef.defName}: CompServerHackable needs tickerType Normal to detect hacking sessions";
        }
    }

    /// <summary>
    /// 多階段駭入的伺服主機。沿用原版 CompHackable 的工作、鎖定、自動駭入與 UI，多做兩件事：
    /// 每一次新的駭入作業都拉設施警報；完成一階段後保持「已駭入」讓工作自然結束，等沒人佔用才重置進入下一階段，
    /// 因此下一階段一定要重新開工，也就一定會再拉一次警報。
    ///
    /// A multi-stage hackable server. Reuses vanilla CompHackable's job, lockout, autohack and UI, and adds two
    /// things: every new hacking session trips the facility alarm, and a finished stage stays "hacked" so the
    /// job ends naturally, only resetting into the next stage once nobody holds the server. The next stage
    /// therefore always needs a fresh job, and so always trips the alarm again.
    /// </summary>
    public class CompServerHackable : CompHackable
    {
        /// <summary>
        /// 兩次 Hack() 之間隔超過這麼多 tick 就算中斷；容忍建築與 pawn 的 tick 先後。暫停時 TicksGame 不動，不會誤判。
        /// A gap longer than this between Hack() calls counts as an interruption; tolerates building/pawn tick
        /// order. TicksGame doesn't move while paused, so pausing never counts.
        /// </summary>
        private const int SessionGapTicks = 60;

        private int stagesTotal = -1;
        private int stagesDone;
        private bool pendingNextStage;
        private List<int> payoutCounts = new List<int>();

        private int lastSeenHackTick = -99999;
        private Pawn lastSessionPawn;

        public new CompProperties_ServerHackable Props => (CompProperties_ServerHackable)props;

        public int StagesTotal => stagesTotal;
        public int StagesDone => stagesDone;
        public bool FullyHacked => stagesTotal > 0 && stagesDone >= stagesTotal;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (stagesTotal < 0)
            {
                stagesTotal = Mathf.Max(1, Props.stageCount.RandomInRange);
            }
            // 生成／讀檔時把現有的 lastHackTick 視為已見過（原版預設 0，讀檔則是存檔前最後一次），免得一生成就拉警報；
            // 存檔時進行中的作業接著跑也不重拉。
            // On spawn or load, treat the current lastHackTick as already seen (vanilla defaults it to 0; on load it's
            // the last pre-save hack) so spawning doesn't trip the alarm, and a session running at save carries on.
            lastSeenHackTick = lastHackTick;
            lastSessionPawn = lastUser;
        }

        // ── 偵測駭入作業 / Session detection ─────────────────────────────

        public override void CompTick()
        {
            base.CompTick();   // 原版的鎖定解除 / vanilla lockout expiry
            if (!parent.Spawned) return;

            // 不能用 lastHackTick == TicksGame：建築比殖民者早生成、同一 tick 內先跑，永遠只看得到上一 tick 的 Hack()。
            // 改成「上次看到之後又有新的 Hack()」，不論誰先 tick 都抓得到。
            // Can't test lastHackTick == TicksGame: the building spawned before the colonists and ticks first, so it
            // only ever sees the previous tick's Hack(). Test for "a Hack() since we last looked" instead, which
            // works whichever ticks first.
            if (lastHackTick >= 0 && lastHackTick > lastSeenHackTick)
            {
                bool newSession = lastHackTick - lastSeenHackTick > SessionGapTicks || lastUser != lastSessionPawn;
                lastSeenHackTick = lastHackTick;
                lastSessionPawn = lastUser;
                if (newSession) TripAlarm(lastUser);
            }

            if (pendingNextStage && !parent.Map.reservationManager.IsReservedByAnyoneOf(parent, Faction.OfPlayer))
            {
                AdvanceStage();
            }
        }

        private void TripAlarm(Pawn hacker)
        {
            FacilityAlarmUtility.Trip(parent, Props.alertIncrement, Props.alarmSignal, Props.fallbackRaidWave);
            Props.alarmSound?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            Props.alarmEffecter?.Spawn(parent.Position, parent.Map).Cleanup();

            string text = hacker != null
                ? "DMS_ServerHack_AlarmTripped".Translate(parent.Named("SERVER"), hacker.Named("HACKER"))
                : "DMS_ServerHack_AlarmTrippedNoHacker".Translate(parent.Named("SERVER"));
            Messages.Message(text, parent, MessageTypeDefOf.ThreatSmall);
        }

        // ── 階段 / Stages ────────────────────────────────────────────────

        protected override void OnHacked(Pawn hacker = null, bool suppressMessages = false)
        {
            base.OnHacked(hacker, suppressMessages);
            stagesDone++;
            GiveReward();
            // hacked 維持 true，讓原版工作的 FailOn(IsHacked) 自然結束這份工作。
            // Leave hacked true so the vanilla job's FailOn(IsHacked) ends this job.
            if (!FullyHacked) pendingNextStage = true;
        }

        private void AdvanceStage()
        {
            pendingNextStage = false;
            hacked = false;
            progress = 0f;
            progressLastLockout = 0f;
            lastHackTick = -1;   // 讓 HackingStarted 任務訊號每階段都送一次 / HackingStarted quest signal fires each stage
            lastSeenHackTick = -99999;   // 下一階段的第一次 Hack() 一定算新作業 / the next stage's first Hack() is always a new session
            sentLetter = false;
            defence = Props.defence + Props.defencePerStage * stagesDone;
            parent.DirtyMapMesh(parent.Map);
        }

        private void GiveReward()
        {
            Thing reward = RollReward();
            if (reward == null || !parent.Spawned) return;

            IntVec3 cell = parent.def.hasInteractionCell ? parent.InteractionCell : parent.Position;
            if (!GenPlace.TryPlaceThing(reward, cell, parent.Map, ThingPlaceMode.Near, out Thing placed)) return;

            Find.LetterStack.ReceiveLetter(
                "DMS_ServerHack_StageLetterLabel".Translate(parent.Named("SERVER")),
                "DMS_ServerHack_StageLetterText".Translate(parent.Named("SERVER"), placed.Named("REWARD"),
                    stagesDone.Named("DONE"), stagesTotal.Named("TOTAL")),
                LetterDefOf.PositiveEvent, placed);
        }

        private Thing RollReward()
        {
            List<ServerHackReward> list = Props.rewards ?? new List<ServerHackReward>();
            while (payoutCounts.Count < list.Count) payoutCounts.Add(0);

            List<int> candidates = Enumerable.Range(0, list.Count)
                .Where(i => list[i].weight > 0f && (list[i].maxPerBuilding <= 0 || payoutCounts[i] < list[i].maxPerBuilding))
                .ToList();

            while (candidates.TryRandomElementByWeight(i => list[i].weight, out int pick))
            {
                Thing t = ServerHackRewardUtility.Make(list[pick]);
                if (t != null)
                {
                    payoutCounts[pick]++;
                    return t;
                }
                candidates.Remove(pick);   // 例：無 Royalty 時的 Techprint → 換下一項 / e.g. Techprint without Royalty
            }
            return Props.fallbackThing != null ? ServerHackRewardUtility.MakeStack(Props.fallbackThing, IntRange.One) : null;
        }

        // ── UI ───────────────────────────────────────────────────────────

        public override string CompInspectStringExtra()
        {
            string s = base.CompInspectStringExtra();
            string stage;
            if (FullyHacked)
            {
                stage = "DMS_ServerHack_Depleted".Translate();
            }
            else
            {
                stage = "DMS_ServerHack_Stage".Translate(stagesDone, stagesTotal) + "\n"
                    + ((string)"DMS_ServerHack_AlarmWarning".Translate()).Colorize(ColorLibrary.RedReadable);
            }
            return s.NullOrEmpty() ? stage : s + "\n" + stage;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Trip alarm",
                    action = () => TripAlarm(null)
                };
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref stagesTotal, "dms_stagesTotal", -1);
            Scribe_Values.Look(ref stagesDone, "dms_stagesDone", 0);
            Scribe_Values.Look(ref pendingNextStage, "dms_pendingNextStage", false);
            Scribe_Collections.Look(ref payoutCounts, "dms_payoutCounts", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                payoutCounts ??= new List<int>();
            }
        }
    }
}
