using System.Collections.Generic;
using Fortified;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace DMS
{
    /// <summary>
    /// 設施增援的警報回應。
    ///
    /// 每一次警報響起（包括叫出這批增援的那一次），當下走得到警報位置的成員就把它當成新崗位前往；
    /// 走不到的留在原本的崗位（剛落地的就是落地點）。之後聽到同一張地圖的新警報會重新判定。
    /// 交戰只看視線：勤務 <c>DMS_AlarmResponse</c> 的戰鬥節點要求看得到才會鎖定目標，
    /// 不像 AssaultColony 那樣知道殖民者在哪、一路找過去。
    ///
    /// Alarm response for facility reinforcements.
    /// Every time the alarm sounds (including the one that summoned this squad), members that can reach
    /// the alarm's position at that moment take it as their new post and head there; those that can't
    /// keep their old post (for a squad that just landed, the landing spot). Later alarms on the same map
    /// are judged again. Engagement is sight-only: the DMS_AlarmResponse duty's fight node only locks
    /// onto targets it can see, rather than knowing where the colonists are the way AssaultColony does.
    /// </summary>
    public class LordJob_AlarmResponse : LordJob
    {
        private IntVec3 alarmCell = IntVec3.Invalid;
        private string listenSignal;

        public LordJob_AlarmResponse()
        {
        }

        /// <param name="alarmCell">叫出這批增援的警報位置；Invalid 表示原地待命。The alarm that summoned the squad; Invalid holds in place.</param>
        /// <param name="listenSignal">之後要回應的警報訊號。The alarm signal to answer from now on.</param>
        public LordJob_AlarmResponse(IntVec3 alarmCell, string listenSignal)
        {
            this.alarmCell = alarmCell;
            this.listenSignal = listenSignal;
        }

        // 設施守軍不撤退，跟原本 LordJob_AssaultColony(canTimeoutOrFlee: false) 一致。
        // Facility defenders never flee, same as the old LordJob_AssaultColony(canTimeoutOrFlee: false).
        public override bool AddFleeToil => false;

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            LordToil_AlarmResponse respond = new LordToil_AlarmResponse(alarmCell);
            graph.StartingToil = respond;

            if (!listenSignal.NullOrEmpty())
            {
                // 回到同一個 toil、不重跑 Init：只是把新的警報位置交給它重新分派崗位。
                // Loops back to the same toil without re-running Init: just hands it the new alarm to re-assign posts.
                Trigger_FacilityAlarm alarm = new Trigger_FacilityAlarm(listenSignal);
                Transition retarget = new Transition(respond, respond, canMoveToSameState: true, updateDutiesIfMovedToSameState: false);
                retarget.AddTrigger(alarm);
                retarget.AddPreAction(new TransitionAction_Custom(() => respond.Notify_Alarm(alarm.lastAlarmCell)));
                graph.AddTransition(retarget);
            }
            return graph;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref alarmCell, "alarmCell", IntVec3.Invalid);
            Scribe_Values.Look(ref listenSignal, "listenSignal");
        }
    }

    public class LordToilData_AlarmResponse : LordToilData
    {
        /// <summary>最近一次警報的位置。The most recent alarm's position.</summary>
        public IntVec3 alarmCell = IntVec3.Invalid;

        /// <summary>
        /// 每名成員的崗位。還在 PawnFlyer 裡（沒生成）的成員不在表內，落地後才決定。
        /// Each member's post. Members still inside a PawnFlyer (unspawned) aren't listed until they land.
        /// </summary>
        public Dictionary<Pawn, IntVec3> posts = new Dictionary<Pawn, IntVec3>();

        private List<Pawn> tmpPawns;
        private List<IntVec3> tmpCells;

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                posts.RemoveAll(kv => kv.Key.DestroyedOrNull());
            }
            Scribe_Values.Look(ref alarmCell, "alarmCell", IntVec3.Invalid);
            Scribe_Collections.Look(ref posts, "posts", LookMode.Reference, LookMode.Value, ref tmpPawns, ref tmpCells);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                posts ??= new Dictionary<Pawn, IntVec3>();
            }
        }
    }

    public class LordToil_AlarmResponse : LordToil
    {
        private LordToilData_AlarmResponse Data => (LordToilData_AlarmResponse)data;

        public override IntVec3 FlagLoc => Data.alarmCell;

        public override bool AllowSatisfyLongNeeds => false;

        public LordToil_AlarmResponse(IntVec3 alarmCell)
        {
            data = new LordToilData_AlarmResponse { alarmCell = alarmCell };
        }

        public override void UpdateAllDuties()
        {
            // 還沒有崗位的成員也要給勤務（焦點 Invalid＝待判定），否則 ThinkNode_Duty 會報「no duty」。
            // Members without a post still get the duty (focus Invalid = pending), or ThinkNode_Duty logs "no duty".
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                SetDuty(pawn, Data.posts.TryGetValue(pawn, out IntVec3 post) ? post : IntVec3.Invalid);
            }
        }

        public override void LordToilTick()
        {
            // 從洞裡跳出來的成員加入 lord 時還在 PawnFlyer 裡，一落地就用當下的警報位置判定崗位。
            // Members leaping out of a hole join the lord while still inside their PawnFlyer; the moment one
            // lands, its post is judged against the current alarm.
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                TryAssignPost(lord.ownedPawns[i]);
            }
        }

        /// <summary>
        /// 已落地、還沒崗位的成員現在判定崗位。PawnFlyer 落地當下就 CheckForJobOverride，比下一次
        /// LordToilTick 早，所以 <see cref="JobGiver_GotoAlarmPost"/> 碰到待判定的勤務也會呼叫這裡。
        /// Judges the post of a landed member that has none yet. PawnFlyer calls CheckForJobOverride the
        /// instant it lands, ahead of the next LordToilTick, so <see cref="JobGiver_GotoAlarmPost"/> also
        /// calls this when it meets a pending duty.
        /// </summary>
        public void TryAssignPost(Pawn pawn)
        {
            if (!pawn.Spawned || Data.posts.ContainsKey(pawn)) return;
            IntVec3 post = CanReachAlarm(pawn, Data.alarmCell) ? Data.alarmCell : pawn.Position;
            Data.posts[pawn] = post;
            SetDuty(pawn, post);
        }

        /// <summary>
        /// 新的警報：當下走得到的成員改把它當崗位，走不到的維持原崗位；還沒落地的等落地再判定。
        /// A new alarm: members that can reach it right now make it their post, the rest keep theirs;
        /// those still airborne are judged when they land.
        /// </summary>
        public void Notify_Alarm(IntVec3 cell)
        {
            Data.alarmCell = cell;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (!pawn.Spawned)
                {
                    Data.posts.Remove(pawn);
                    SetDuty(pawn, IntVec3.Invalid);
                    continue;
                }
                if (CanReachAlarm(pawn, cell))
                {
                    Data.posts[pawn] = cell;
                    SetDuty(pawn, cell);
                }
            }
        }

        public override void Notify_PawnLost(Pawn victim, PawnLostCondition cond)
        {
            Data.posts.Remove(victim);
        }

        private static bool CanReachAlarm(Pawn pawn, IntVec3 cell)
        {
            // Touch：警報位置常是掃描器本身，哨塔這類 PassThroughOnly 的建築站不上去。
            // Touch: the alarm cell is usually the scanner itself, and sentry towers are PassThroughOnly.
            return cell.IsValid && pawn.CanReach(cell, PathEndMode.Touch, Danger.Deadly);
        }

        /// <param name="post">Invalid＝待判定，落地後由 <see cref="TryAssignPost"/> 補上。Invalid = pending, filled in by <see cref="TryAssignPost"/> once landed.</param>
        private static void SetDuty(Pawn pawn, IntVec3 post)
        {
            if (pawn.mindState == null) return;
            pawn.mindState.duty = new PawnDuty(DMS_DefOf.DMS_AlarmResponse, post);
        }
    }

    /// <summary>
    /// 同一張地圖上的警報訊號。只負責把警報位置交給 transition 的 preAction，自己不帶狀態。
    /// An alarm signal from the lord's own map. It only hands the alarm's position to the transition's
    /// preAction and carries no saved state.
    /// </summary>
    public class Trigger_FacilityAlarm : Trigger
    {
        private readonly string signalTag;

        /// <summary>最近一次觸發的警報位置，只在同一次 CheckSignal 內有效。Valid only within the CheckSignal that set it.</summary>
        public IntVec3 lastAlarmCell = IntVec3.Invalid;

        public Trigger_FacilityAlarm(string signalTag)
        {
            this.signalTag = signalTag;
        }

        public override bool ActivateOn(Lord lord, TriggerSignal signal)
        {
            if (signal.type != TriggerSignalType.Signal || signal.signal.tag != signalTag) return false;
            if (!signal.signal.IsFromMap(lord.Map)) return false;
            lastAlarmCell = AlertResponseUtility.AlarmCell(signal.signal, lord.Map);
            return lastAlarmCell.IsValid;
        }
    }
}
