using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace DMS
{
    /// <summary>守墓人分隊的狀態。A gravekeeper squad's state.</summary>
    public enum GravekeeperMode : byte
    {
        /// <summary>沿走廊樹巡邏，在獎勵房門前停留。Walking the corridor tree, pausing at the treasury door.</summary>
        Patrol,
        /// <summary>看到入侵者：停下盤查、不開火。An intruder in sight: halted and challenging, holding fire.</summary>
        Challenge,
        /// <summary>交戰：只打看得到的目標，一段時間沒看到就回去巡邏。Engaged: fights what it sees, resumes patrol after a quiet spell.</summary>
        Engage,
        /// <summary>獎勵房被打開：全設施搜索，不再回去巡邏。The treasury is open: sweeping the facility for good.</summary>
        Search,
    }

    /// <summary>
    /// 守墓人巡邏隊。設定上它們要在無人值守的設施裡巡邏數百年，交戰規則比一般機兵保守，也不會離開設施迎擊敵人：
    /// <list type="bullet">
    /// <item>巡邏：領隊沿 <see cref="route"/>（走廊樹）走，其他成員跟著；殿後的種類離得較遠。獎勵房門前會停一下。
    /// 分區閘門對它們開放（見 <see cref="Patch_AccessLinkDoor_GravekeeperPass"/>），通過後照原版的門邏輯自己關上。</item>
    /// <item>盤查：看到入侵者先停下、面向對方、冒「？」，不開火。</item>
    /// <item>交戰：被打、入侵者進入管制區（獎勵房、指揮室）、或設施警戒值達到門檻時開火。</item>
    /// <item>搜索：獎勵房門一被打開就全設施搜索，不再回頭巡邏。</item>
    /// <item>不出設施：電梯廳（<see cref="lobby"/>）是界線，絕不走進去，人跑進去也不追（看得到仍然會開火）。</item>
    /// </list>
    ///
    /// The gravekeeper patrol. They are meant to have walked unattended installations for centuries, with rules of
    /// engagement more conservative than a regular mech's, and never leave the facility to meet an enemy:
    /// <list type="bullet">
    /// <item>Patrol: the leader walks <see cref="route"/> (the corridor tree) with the rest following, rear-guard kinds
    /// further back, pausing at the treasury door. Sector gates open for them (see
    /// <see cref="Patch_AccessLinkDoor_GravekeeperPass"/>) and close again behind them by the vanilla door logic.</item>
    /// <item>Challenge: an intruder in sight halts them; they face it and show a "?" without firing.</item>
    /// <item>Engage: being shot, an intruder in a restricted room (treasury, command room) or the facility alert reaching
    /// the threshold opens fire.</item>
    /// <item>Search: once a treasury door is opened they sweep the whole facility and never go back to patrolling.</item>
    /// <item>They stay inside: the lift lobby (<see cref="lobby"/>) is the line; they never step into it and never chase
    /// anyone in there (they still shoot at what they can see).</item>
    /// </list>
    /// </summary>
    public class LordJob_GravekeeperPatrol : LordJob
    {
        /// <summary>巡邏路線（地圖座標），首尾相接。The patrol route in map space; it loops.</summary>
        public List<IntVec3> route = new List<IntVec3>();

        /// <summary>路線上哪些點是獎勵房門前的崗位。Which route points are treasury-door posts.</summary>
        public List<int> tombPosts = new List<int>();

        /// <summary>電梯廳（含牆線）。The lift lobby, wall line included.</summary>
        public List<CellRect> lobby = new List<CellRect>();

        /// <summary>管制區（房間內部）。Restricted rooms, interiors only.</summary>
        public List<CellRect> restricted = new List<CellRect>();

        /// <summary>這座設施的分區閘門。This facility's sector gates.</summary>
        public List<Thing> gates = new List<Thing>();

        /// <summary>獎勵房的密封門。The treasury's sealed doors.</summary>
        public List<Thing> tombDoors = new List<Thing>();

        public List<PawnKindDef> overwatchKinds = new List<PawnKindDef>();

        public int startIndex;
        public float followRadius = 3f;
        public float overwatchFollowRadius = 8f;
        public float sightRange = 26f;
        public float engageAlertLevel = 0.6f;
        public int calmDownTicks = 2500;
        public int tombPauseTicks = 900;

        private HashSet<Thing> gateSet;

        public LordJob_GravekeeperPatrol()
        {
        }

        public LordJob_GravekeeperPatrol(ModExtension_GravekeeperPatrol ext, int startIndex)
        {
            this.startIndex = startIndex;
            overwatchKinds = new List<PawnKindDef>(ext.overwatchKinds ?? new List<PawnKindDef>());
            followRadius = ext.followRadius;
            overwatchFollowRadius = ext.overwatchFollowRadius;
            sightRange = ext.sightRange;
            engageAlertLevel = ext.engageAlertLevel;
            calmDownTicks = ext.calmDownTicks;
            tombPauseTicks = ext.tombPauseTicks;
        }

        // 設施守軍不撤退。Facility defenders never flee.
        public override bool AddFleeToil => false;

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            graph.StartingToil = new LordToil_GravekeeperPatrol(startIndex);
            return graph;
        }

        public bool CanOpenGate(Thing door)
        {
            gateSet ??= new HashSet<Thing>(gates.Where(g => g != null));
            return gateSet.Contains(door);
        }

        public bool InLobby(IntVec3 cell) => Contains(lobby, cell);

        public bool InRestricted(IntVec3 cell) => Contains(restricted, cell);

        /// <summary>任一扇獎勵房密封門已被控制台放行。Any treasury door has been released by its console.</summary>
        public bool TombOpened
        {
            get
            {
                for (int i = 0; i < tombDoors.Count; i++)
                {
                    if (tombDoors[i] is Building_RollingDoor_AccessLink door && door.activated) return true;
                }
                return false;
            }
        }

        public bool IsOverwatch(Pawn pawn) => overwatchKinds.Contains(pawn.kindDef);

        public float FollowRadiusFor(Pawn pawn) => IsOverwatch(pawn) ? overwatchFollowRadius : followRadius;

        /// <summary>離某格最近的路線點，沒有路線時回傳 -1。The route point nearest a cell, or -1 without a route.</summary>
        public int NearestRouteIndex(IntVec3 from)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < route.Count; i++)
            {
                float dist = route[i].DistanceToSquared(from);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
            return best;
        }

        private static bool Contains(List<CellRect> rects, IntVec3 cell)
        {
            for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i].Contains(cell)) return true;
            }
            return false;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref route, "route", LookMode.Value);
            Scribe_Collections.Look(ref tombPosts, "tombPosts", LookMode.Value);
            Scribe_Collections.Look(ref lobby, "lobby", LookMode.Value);
            Scribe_Collections.Look(ref restricted, "restricted", LookMode.Value);
            Scribe_Collections.Look(ref gates, "gates", LookMode.Reference);
            Scribe_Collections.Look(ref tombDoors, "tombDoors", LookMode.Reference);
            Scribe_Collections.Look(ref overwatchKinds, "overwatchKinds", LookMode.Def);
            Scribe_Values.Look(ref startIndex, "startIndex");
            Scribe_Values.Look(ref followRadius, "followRadius", 3f);
            Scribe_Values.Look(ref overwatchFollowRadius, "overwatchFollowRadius", 8f);
            Scribe_Values.Look(ref sightRange, "sightRange", 26f);
            Scribe_Values.Look(ref engageAlertLevel, "engageAlertLevel", 0.6f);
            Scribe_Values.Look(ref calmDownTicks, "calmDownTicks", 2500);
            Scribe_Values.Look(ref tombPauseTicks, "tombPauseTicks", 900);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                route ??= new List<IntVec3>();
                tombPosts ??= new List<int>();
                lobby ??= new List<CellRect>();
                restricted ??= new List<CellRect>();
                gates ??= new List<Thing>();
                tombDoors ??= new List<Thing>();
                overwatchKinds ??= new List<PawnKindDef>();
                gates.RemoveAll(t => t == null);
                tombDoors.RemoveAll(t => t == null);
                overwatchKinds.RemoveAll(k => k == null);
                gateSet = null;
            }
        }
    }

    public class LordToilData_GravekeeperPatrol : LordToilData
    {
        public GravekeeperMode mode = GravekeeperMode.Patrol;

        /// <summary>領隊正前往的路線點。The route point the leader is heading for.</summary>
        public int routeIndex;

        public bool pausedAtPost;
        public int pauseUntilTick = -1;

        /// <summary>有人落後太遠，領隊停下等。Someone fell too far behind; the leader is waiting.</summary>
        public bool regrouping;

        public Pawn leader;
        public Pawn challengeTarget;

        public int lastSeenTick = -99999;
        public IntVec3 lastSeenCell = IntVec3.Invalid;

        public IntVec3 searchPoint = IntVec3.Invalid;
        public int nextSearchTick;

        public bool challengeAnnounced;
        public int lastQueryMoteTick = -99999;

        /// <summary>卡住判定：最近一次離目標更近的時間與距離。Stuck check: when and how close the leader last got.</summary>
        public int progressTick;
        public float bestDistance = float.MaxValue;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", GravekeeperMode.Patrol);
            Scribe_Values.Look(ref routeIndex, "routeIndex");
            Scribe_Values.Look(ref pausedAtPost, "pausedAtPost");
            Scribe_Values.Look(ref pauseUntilTick, "pauseUntilTick", -1);
            Scribe_Values.Look(ref regrouping, "regrouping");
            Scribe_References.Look(ref leader, "leader");
            Scribe_References.Look(ref challengeTarget, "challengeTarget");
            Scribe_Values.Look(ref lastSeenTick, "lastSeenTick", -99999);
            Scribe_Values.Look(ref lastSeenCell, "lastSeenCell", IntVec3.Invalid);
            Scribe_Values.Look(ref searchPoint, "searchPoint", IntVec3.Invalid);
            Scribe_Values.Look(ref nextSearchTick, "nextSearchTick");
            Scribe_Values.Look(ref challengeAnnounced, "challengeAnnounced");
            Scribe_Values.Look(ref lastQueryMoteTick, "lastQueryMoteTick", -99999);
            Scribe_Values.Look(ref progressTick, "progressTick");
            Scribe_Values.Look(ref bestDistance, "bestDistance", float.MaxValue);
        }
    }

    public class LordToil_GravekeeperPatrol : LordToil
    {
        private const int CheckInterval = 30;
        private const float ArriveRadius = 3f;
        /// <summary>入侵者離開視線後，盤查還維持多久。How long a challenge holds after the intruder leaves sight.</summary>
        private const int ChallengeLingerTicks = 300;
        /// <summary>領隊這麼久沒有更接近路線點，就跳到下一點。Skip to the next point when the leader makes no headway for this long.</summary>
        private const int StuckTicks = 2500;
        /// <summary>有成員離領隊超過這個距離，領隊停下等。The leader waits when a member is further than this.</summary>
        private const float RegroupDistance = 14f;
        private const int QueryMoteInterval = 150;
        private const int SearchRetargetTicks = 1500;

        private static readonly Color QueryColor = new Color(1f, 0.85f, 0.3f);

        private LordToilData_GravekeeperPatrol Data => (LordToilData_GravekeeperPatrol)data;

        private LordJob_GravekeeperPatrol Patrol => (LordJob_GravekeeperPatrol)lord.LordJob;

        public GravekeeperMode Mode => Data.mode;

        public override bool AllowSatisfyLongNeeds => false;

        public LordToil_GravekeeperPatrol(int startIndex)
        {
            data = new LordToilData_GravekeeperPatrol { routeIndex = startIndex };
        }

        private static int Now => Find.TickManager.TicksGame;

        // ── 勤務 / Duties ─────────────────────────────────────────────────────

        public override void UpdateAllDuties()
        {
            EnsureLeader();
            bool fireAtWill = Data.mode == GravekeeperMode.Engage || Data.mode == GravekeeperMode.Search;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                SetDuty(pawn);
                SetTurrets(pawn, fireAtWill);
            }
        }

        private void SetDuty(Pawn pawn)
        {
            if (pawn.mindState == null) return;

            LordToilData_GravekeeperPatrol d = Data;
            LordJob_GravekeeperPatrol patrol = Patrol;
            Pawn leader = d.leader;

            switch (d.mode)
            {
                case GravekeeperMode.Patrol:
                    if (pawn == leader || leader == null)
                    {
                        LocalTargetInfo waypoint = d.regrouping || patrol.route.Count == 0
                            ? (LocalTargetInfo)pawn.Position
                            : patrol.route[Mathf.Clamp(d.routeIndex, 0, patrol.route.Count - 1)];
                        pawn.mindState.duty = new PawnDuty(DMS_DefOf.DMS_GravekeeperPatrol, waypoint);
                    }
                    else
                    {
                        pawn.mindState.duty = new PawnDuty(DMS_DefOf.DMS_GravekeeperPatrol, leader, patrol.FollowRadiusFor(pawn));
                    }
                    break;

                case GravekeeperMode.Challenge:
                    pawn.mindState.duty = new PawnDuty(DMS_DefOf.DMS_GravekeeperChallenge,
                        d.challengeTarget != null && d.challengeTarget.Spawned ? (LocalTargetInfo)d.challengeTarget : pawn.Position);
                    break;

                default:
                    // 交戰與搜索：有最近的接觸就全員往那裡壓；搜索時領隊沒有接觸就去搜索點，其他人跟著領隊。
                    // Engage and search: with a recent contact everyone closes on it; searching without one, the
                    // leader heads for the search point and the rest follow the leader.
                    bool contact = d.lastSeenCell.IsValid && Now - d.lastSeenTick < patrol.calmDownTicks;
                    if (contact)
                    {
                        pawn.mindState.duty = new PawnDuty(DMS_DefOf.DMS_GravekeeperEngage, d.lastSeenCell);
                    }
                    else if (pawn != leader && leader != null)
                    {
                        pawn.mindState.duty = new PawnDuty(DMS_DefOf.DMS_GravekeeperEngage, leader, patrol.FollowRadiusFor(pawn));
                    }
                    else if (d.mode == GravekeeperMode.Search && d.searchPoint.IsValid)
                    {
                        pawn.mindState.duty = new PawnDuty(DMS_DefOf.DMS_GravekeeperEngage, d.searchPoint);
                    }
                    else
                    {
                        pawn.mindState.duty = new PawnDuty(DMS_DefOf.DMS_GravekeeperEngage, pawn.Position);
                    }
                    break;
            }
        }

        /// <summary>
        /// 肩上的副炮預設見敵即射，所以巡邏與盤查時要收起來，交戰時才放開。開關存在副炮自己身上。
        /// Shoulder sub-turrets fire on sight by default, so they are held while patrolling or challenging and
        /// released when engaged. The switch is saved on the sub-turret itself.
        /// </summary>
        private static void SetTurrets(Pawn pawn, bool fireAtWill)
        {
            CompMultipleTurretGun comp = pawn.TryGetComp<CompMultipleTurretGun>();
            if (comp?.turrets == null) return;
            for (int i = 0; i < comp.turrets.Count; i++)
            {
                comp.turrets[i]?.SwitchAutoFire(fireAtWill);
            }
        }

        /// <summary>領隊不在了就換人；換了回傳 true。Picks a new leader when the old one is gone; true when it changed.</summary>
        private bool EnsureLeader()
        {
            LordToilData_GravekeeperPatrol d = Data;
            if (CanLead(d.leader)) return false;

            Pawn next = lord.ownedPawns.FirstOrDefault(p => CanLead(p) && !Patrol.IsOverwatch(p))
                ?? lord.ownedPawns.FirstOrDefault(CanLead);
            bool changed = next != d.leader;
            d.leader = next;
            return changed;
        }

        private bool CanLead(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && pawn.Spawned && !pawn.Downed && lord.ownedPawns.Contains(pawn);
        }

        // ── 每回合 / Tick ─────────────────────────────────────────────────────

        public override void LordToilTick()
        {
            if ((Now + lord.loadID) % CheckInterval != 0) return;
            if (lord.ownedPawns.Count == 0 || Patrol.route.Count == 0) return;

            LordToilData_GravekeeperPatrol d = Data;
            LordJob_GravekeeperPatrol patrol = Patrol;

            if (EnsureLeader()) UpdateAllDuties();

            if (d.mode != GravekeeperMode.Search && patrol.TombOpened)
            {
                StartSearch();
                return;
            }

            Pawn seen = FindVisibleIntruder(out Pawn spotter, out bool intruderInRestricted);
            if (seen != null)
            {
                d.lastSeenTick = Now;
                d.lastSeenCell = seen.Position;
            }
            float alert = AlertResponseUtility.AlertLevelPct(lord.Map);

            switch (d.mode)
            {
                case GravekeeperMode.Patrol:
                case GravekeeperMode.Challenge:
                    if (seen != null && (intruderInRestricted || alert >= patrol.engageAlertLevel))
                    {
                        Engage(spotter);
                    }
                    else if (seen != null)
                    {
                        Challenge(seen, spotter);
                    }
                    else if (d.mode == GravekeeperMode.Challenge)
                    {
                        if (Now - d.lastSeenTick > ChallengeLingerTicks) SetMode(GravekeeperMode.Patrol);
                    }
                    else
                    {
                        AdvanceRoute();
                    }
                    break;

                case GravekeeperMode.Engage:
                    if (seen == null && Now - d.lastSeenTick > patrol.calmDownTicks && alert < patrol.engageAlertLevel)
                    {
                        SetMode(GravekeeperMode.Patrol);
                    }
                    else
                    {
                        UpdateAllDuties();
                    }
                    break;

                case GravekeeperMode.Search:
                    TickSearch();
                    break;
            }
        }

        private void SetMode(GravekeeperMode mode)
        {
            LordToilData_GravekeeperPatrol d = Data;
            if (d.mode == mode) return;
            d.mode = mode;

            if (mode != GravekeeperMode.Challenge) d.challengeTarget = null;
            if (mode == GravekeeperMode.Patrol)
            {
                // 從離領隊最近的路線點接著巡。Resume from the route point nearest the leader.
                EnsureLeader();
                int index = d.leader != null ? Patrol.NearestRouteIndex(d.leader.Position) : d.routeIndex;
                d.routeIndex = Mathf.Max(0, index);
                d.pausedAtPost = false;
                d.regrouping = false;
                ResetProgress();
            }

            UpdateAllDuties();

            // 立刻重想：換了勤務的成員不必等手上的工作到期。Think again now rather than waiting for the current job to expire.
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn.Spawned && !pawn.Downed && pawn.CurJob != null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
        }

        // ── 巡邏 / Patrol ─────────────────────────────────────────────────────

        private void AdvanceRoute()
        {
            LordToilData_GravekeeperPatrol d = Data;
            LordJob_GravekeeperPatrol patrol = Patrol;
            Pawn leader = d.leader;
            if (leader == null) return;

            // 有人落後太遠就原地等。Wait up when somebody has fallen too far behind.
            bool lagging = lord.ownedPawns.Any(p => p != leader && p.Spawned && !p.Downed
                && !p.Position.InHorDistOf(leader.Position, RegroupDistance + (patrol.IsOverwatch(p) ? patrol.overwatchFollowRadius : 0f)));
            if (lagging != d.regrouping)
            {
                d.regrouping = lagging;
                SetDuty(leader);
                ResetProgress();
            }
            if (lagging) return;

            d.routeIndex = Mathf.Clamp(d.routeIndex, 0, patrol.route.Count - 1);
            IntVec3 waypoint = patrol.route[d.routeIndex];

            if (leader.Position.InHorDistOf(waypoint, ArriveRadius))
            {
                // 獎勵房門前停一下再走。Stand a while at the treasury door before moving on.
                if (patrol.tombPosts.Contains(d.routeIndex))
                {
                    if (!d.pausedAtPost)
                    {
                        d.pausedAtPost = true;
                        d.pauseUntilTick = Now + patrol.tombPauseTicks;
                        return;
                    }
                    if (Now < d.pauseUntilTick) return;
                }
                NextWaypoint();
                return;
            }

            // 根本走不到（閘門被玩家放行後又被擋住之類）就直接跳過。Plainly unreachable: skip it right away.
            if (!leader.CanReach(waypoint, PathEndMode.Touch, Danger.Deadly))
            {
                NextWaypoint();
                return;
            }

            float dist = leader.Position.DistanceTo(waypoint);
            if (dist < d.bestDistance - 0.9f)
            {
                d.bestDistance = dist;
                d.progressTick = Now;
            }
            else if (Now - d.progressTick > StuckTicks)
            {
                // 走不到（被擋住、或門被玩家永久打開又關不回去之類）就跳過這一點。Can't get there; skip the point.
                NextWaypoint();
            }
        }

        private void NextWaypoint()
        {
            LordToilData_GravekeeperPatrol d = Data;
            d.routeIndex = (d.routeIndex + 1) % Patrol.route.Count;
            d.pausedAtPost = false;
            ResetProgress();
            if (d.leader != null) SetDuty(d.leader);
        }

        private void ResetProgress()
        {
            Data.progressTick = Now;
            Data.bestDistance = float.MaxValue;
        }

        // ── 盤查與交戰 / Challenge and engagement ─────────────────────────────

        private void Challenge(Pawn intruder, Pawn spotter)
        {
            LordToilData_GravekeeperPatrol d = Data;
            Map map = lord.Map;

            if (d.mode != GravekeeperMode.Challenge || d.challengeTarget != intruder)
            {
                d.challengeTarget = intruder;
                if (d.mode == GravekeeperMode.Challenge) UpdateAllDuties();
                else SetMode(GravekeeperMode.Challenge);
            }

            // 只在看得到盤查者的時候給提示，不從戰爭迷霧裡洩漏它們的位置。
            // Only cue the player when the challenger is in view; nothing leaks out of the fog.
            if (spotter == null || spotter.Position.Fogged(map)) return;

            if (Now - d.lastQueryMoteTick >= QueryMoteInterval)
            {
                d.lastQueryMoteTick = Now;
                MoteMaker.ThrowText(spotter.DrawPos + new Vector3(0f, 0f, 1.6f), map, "?", QueryColor, 2.5f);
            }
            if (!d.challengeAnnounced)
            {
                d.challengeAnnounced = true;
                Messages.Message("DMS_Gravekeeper_Challenge".Translate(intruder.LabelShort), new LookTargets(spotter), MessageTypeDefOf.CautionInput);
            }
        }

        private void Engage(Pawn spotter)
        {
            if (Data.mode == GravekeeperMode.Engage || Data.mode == GravekeeperMode.Search) return;
            SetMode(GravekeeperMode.Engage);

            Pawn cue = spotter ?? Data.leader;
            if (cue != null && cue.Spawned && !cue.Position.Fogged(lord.Map))
            {
                Messages.Message("DMS_Gravekeeper_Engage".Translate(), new LookTargets(cue), MessageTypeDefOf.ThreatBig);
            }
        }

        public override void Notify_PawnDamaged(Pawn victim, DamageInfo dinfo)
        {
            // 被敵人打到就開火。Fire back once an enemy lands a hit.
            Thing instigator = dinfo.Instigator;
            if (instigator == null || !instigator.HostileTo(lord.faction)) return;

            Data.lastSeenTick = Now;
            if (instigator.Spawned && instigator.Map == lord.Map && !Patrol.InLobby(instigator.Position))
            {
                Data.lastSeenCell = instigator.Position;
            }
            Engage(victim);
        }

        public override void Notify_PawnLost(Pawn victim, PawnLostCondition cond)
        {
            if (victim == Data.leader) Data.leader = null;

            // 一擊就倒的成員來不及回報受傷；被擊倒、擊毀或駭走都算遭到攻擊。
            // A member dropped in one blow never reports the hit; being downed, destroyed or hacked away counts as an attack.
            if (cond == PawnLostCondition.Incapped || cond == PawnLostCondition.Killed || cond == PawnLostCondition.ChangedFaction)
            {
                // 被擊毀時已經換成殘骸，所以用 Held 取位置。Destroyed members are already a corpse, hence the Held lookups.
                IntVec3 cell = victim.PositionHeld;
                if (victim.MapHeld == lord.Map && cell.IsValid && !Patrol.InLobby(cell))
                {
                    Data.lastSeenTick = Now;
                    Data.lastSeenCell = cell;
                }
                Engage(null);
            }
        }

        /// <summary>
        /// 任一成員視距內、有視線的敵對 pawn 中最近的一個。電梯廳裡的不算：那裡已經在它們的守備範圍外。
        /// The nearest hostile pawn any member can see within sight range. Anyone in the lift lobby is ignored:
        /// that is already outside what they guard.
        /// </summary>
        private Pawn FindVisibleIntruder(out Pawn spotter, out bool anyInRestricted)
        {
            spotter = null;
            anyInRestricted = false;
            if (lord.faction == null) return null;

            Map map = lord.Map;
            LordJob_GravekeeperPatrol patrol = Patrol;
            Pawn best = null;
            float bestDist = float.MaxValue;

            foreach (IAttackTarget target in map.attackTargetsCache.TargetsHostileToFaction(lord.faction))
            {
                if (!(target.Thing is Pawn intruder) || !intruder.Spawned || intruder.Downed || intruder.Dead) continue;
                if (intruder.IsPsychologicallyInvisible() || patrol.InLobby(intruder.Position)) continue;

                for (int i = 0; i < lord.ownedPawns.Count; i++)
                {
                    Pawn member = lord.ownedPawns[i];
                    if (!member.Spawned || member.Downed) continue;

                    float dist = member.Position.DistanceTo(intruder.Position);
                    if (dist > patrol.sightRange) continue;
                    if (!GenSight.LineOfSight(member.Position, intruder.Position, map, skipFirstCell: true)) continue;

                    if (patrol.InRestricted(intruder.Position)) anyInRestricted = true;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = intruder;
                        spotter = member;
                    }
                    break;
                }
            }
            return best;
        }

        // ── 搜索 / Search ─────────────────────────────────────────────────────

        private void StartSearch()
        {
            // 同一張地圖有好幾隊時只提示一次。Several squads on one map only announce it once.
            bool announced = lord.Map.lordManager.lords.Any(l => l != lord
                && l.CurLordToil is LordToil_GravekeeperPatrol other && other.Mode == GravekeeperMode.Search);

            Data.searchPoint = IntVec3.Invalid;
            Data.nextSearchTick = 0;
            SetMode(GravekeeperMode.Search);
            TickSearch();

            if (!announced)
            {
                Messages.Message("DMS_Gravekeeper_TombOpened".Translate(), MessageTypeDefOf.ThreatBig);
            }
        }

        private void TickSearch()
        {
            LordToilData_GravekeeperPatrol d = Data;
            Pawn leader = d.leader;
            bool contact = d.lastSeenCell.IsValid && Now - d.lastSeenTick < Patrol.calmDownTicks;

            if (!contact && leader != null
                && (!d.searchPoint.IsValid || leader.Position.InHorDistOf(d.searchPoint, ArriveRadius) || Now >= d.nextSearchTick))
            {
                d.searchPoint = PickSearchPoint(leader);
                d.nextSearchTick = Now + SearchRetargetTicks;
            }
            UpdateAllDuties();
        }

        /// <summary>搜索點：路線點與管制區中心，避開領隊附近。Search points: route points and restricted-room centres, away from the leader.</summary>
        private IntVec3 PickSearchPoint(Pawn leader)
        {
            LordJob_GravekeeperPatrol patrol = Patrol;
            IEnumerable<IntVec3> candidates = patrol.route.Concat(patrol.restricted.Select(r => r.CenterCell));
            List<IntVec3> far = candidates.Where(c => !c.InHorDistOf(leader.Position, 8f)).ToList();
            if (far.Count > 0) return far.RandomElement();
            return patrol.route.Count > 0 ? patrol.route.RandomElement() : IntVec3.Invalid;
        }
    }
}
