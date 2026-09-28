using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace DMS
{
    /// <summary>
    /// 警報反應建築共用的兵力產生邏輯（洞口跳出、戰鬥群呼叫都用這套）。
    /// Squad-generation logic shared by the alert-response buildings (hole emerge, battle-group call).
    ///
    /// 兵力兩種給法：<paramref name="pawnKinds"/> 有填就從裡面抽 countRange 隻；
    /// 沒填就依 pointsRange 用派系的 pawnGroupMakers 生一隊。
    /// 兩者都依地圖當下的警戒值（FFF MapComponent_AlertCounter）縮放：
    /// 警戒值 0% 取範圍下限，100% 取上限；也可以用 pointsByAlertLevel 自訂曲線。
    /// Either list pawnKinds and roll countRange of them, or leave it empty and let the faction's
    /// pawnGroupMakers build a squad worth pointsRange. Both scale with the map's current alert level
    /// (FFF MapComponent_AlertCounter): 0% alert takes the bottom of the range, 100% the top;
    /// pointsByAlertLevel can replace that curve.
    /// </summary>
    public static class AlertResponseUtility
    {
        /// <summary>
        /// 地圖目前的警戒值比例（0~1）。掃描器是先累加警戒值再廣播 Signal，所以這裡讀到的已經含本次觸發。
        /// The map's current alert level (0~1). Scanners bump the counter before broadcasting, so the
        /// value already includes the trigger that got us here.
        /// </summary>
        public static float AlertLevelPct(Map map)
        {
            return map?.GetComponent<MapComponent_AlertCounter>()?.AlertLevelPct ?? 0f;
        }

        /// <summary>
        /// 警報訊號指向的格子：POSITION 參數（掃描器、伺服主機都有帶），沒有就用 SUBJECT 所在位置；
        /// 都沒有或不在這張地圖上時回傳 Invalid。
        /// The cell an alarm signal points at: its POSITION arg (scanners and server hosts both send one),
        /// else where its SUBJECT is; Invalid when neither is there or it isn't on this map.
        /// </summary>
        public static IntVec3 AlarmCell(Signal signal, Map map)
        {
            if (map == null) return IntVec3.Invalid;
            if (signal.args.TryGetArg("POSITION", out IntVec3 cell) && cell.InBounds(map)) return cell;
            if (signal.args.TryGetArg("SUBJECT", out Thing subject) && subject != null && subject.MapHeld == map)
            {
                return subject.PositionHeld;
            }
            return IntVec3.Invalid;
        }

        /// <summary>
        /// 效果器收到的訊號若正是它在聽的警報，回傳警報位置；其他訊號或建築未生成時回傳 Invalid。
        /// FFF 的 DoEffect 不帶訊號，警報回應類的效果器在 Notify_SignalReceived 先用這個記下位置。
        /// The alarm's position when the signal is the one this effector listens for; Invalid for any other
        /// signal or an unspawned building. FFF's DoEffect gets no signal, so alarm-response effectors note the
        /// position with this in Notify_SignalReceived.
        /// </summary>
        public static IntVec3 AlarmCellFor(CompAlertEffector effector, Signal signal)
        {
            ThingWithComps parent = effector.parent;
            if (signal.tag != effector.Props.listenSignal || !parent.Spawned) return IntVec3.Invalid;
            return AlarmCell(signal, parent.Map);
        }

        /// <summary>
        /// 增援設施的 CanFire 共用判定：訊號指向的警報位置從出兵點走得到才觸發。
        /// 沒有訊號或訊號不帶位置時不擋（出兵後原地待命）。
        /// Shared CanFire check for reinforcement facilities: fire only when the alarm's position can be reached
        /// from the exit cells. No signal, or one without a position, isn't blocked (the squad holds in place).
        /// </summary>
        public static bool CanReachAlarmFrom(CompAlertEffector effector, Signal? signal, IEnumerable<IntVec3> exitCells, Faction faction)
        {
            if (!signal.HasValue) return true;
            IntVec3 alarmCell = AlarmCellFor(effector, signal.Value);
            return !alarmCell.IsValid || FacilityCanReachAlarm(effector.parent.Map, exitCells, alarmCell, faction);
        }

        /// <summary>
        /// 增援設施的出兵點走不走得到警報位置（Touch）。增援在觸發前還沒生成，沒有 pawn 可以問
        /// <c>pawn.CanReach</c>，所以直接走 region：門依 <paramref name="faction"/> 判定（見 <see cref="DoorPassableFor"/>），
        /// 其餘比照 <see cref="TraverseMode.PassDoors"/>。
        /// Whether a reinforcement facility's exit cells can reach the alarm (Touch). The squad doesn't exist
        /// before the trigger, so there's no pawn to ask <c>pawn.CanReach</c>; this walks regions directly,
        /// judging doors for <paramref name="faction"/> (see <see cref="DoorPassableFor"/>) and everything else
        /// like <see cref="TraverseMode.PassDoors"/>.
        /// </summary>
        public static bool FacilityCanReachAlarm(Map map, IEnumerable<IntVec3> exitCells, IntVec3 alarmCell, Faction faction)
        {
            if (map == null || !alarmCell.InBounds(map)) return false;

            TraverseParms passDoors = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly);
            bool Enterable(Region r) => r != null && r.Allows(passDoors, isDestination: false)
                && (r.door == null || DoorPassableFor(r.door, faction));

            // Touch：警報格本身或相鄰八格任一個 region 到得了就算。
            // Touch: reaching the alarm cell's region or any of its eight neighbours' counts.
            HashSet<Region> destRegions = new HashSet<Region>();
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(new TargetInfo(alarmCell, map)).Append(alarmCell))
            {
                if (!c.InBounds(map)) continue;
                Region r = c.GetRegion(map);
                if (Enterable(r)) destRegions.Add(r);
            }
            if (destRegions.Count == 0) return false;

            HashSet<Region> visited = new HashSet<Region>();
            bool found = false;
            foreach (IntVec3 c in exitCells)
            {
                if (!c.InBounds(map)) continue;
                Region root = c.GetRegion(map);
                if (!Enterable(root) || visited.Contains(root)) continue;

                RegionTraverser.BreadthFirstTraverse(root,
                    (from, to) => Enterable(to),
                    r =>
                    {
                        visited.Add(r);
                        found = destRegions.Contains(r);
                        return found;
                    });
                if (found) return true;
            }
            return false;
        }

        /// <summary>
        /// 不靠 pawn 判定門對某派系通不通，比照 <see cref="Building_Door.PawnCanOpen"/>：
        /// 開著且不會自己關的門都能過；FFF 的門禁／封鎖門未啟用時誰都打不開，啟用後不看派系；
        /// 無主的門能過；其餘要與門的派系不敵對。
        /// Whether a door lets a faction through, without a pawn, mirroring <see cref="Building_Door.PawnCanOpen"/>:
        /// an open door that stays open always does; FFF's access and sealed doors open for nobody until activated
        /// and ignore faction after; unowned doors do; anything else needs a faction not hostile to the door's.
        /// </summary>
        public static bool DoorPassableFor(Building_Door door, Faction faction)
        {
            if (door.FreePassage) return true;
            switch (door)
            {
                case Building_RollingDoor_AccessLink link:
                    return link.activated;
                case Building_RollingDoor_Access access:
                    return access.Comp?.activated ?? true;
            }
            if (door.Faction == null || faction == null) return true;
            return door.Faction == faction || !door.Faction.HostileTo(faction);
        }

        /// <summary>
        /// 把已生成的單位改交給警報回應（<see cref="LordJob_AlarmResponse"/>）：先退出原本的 lord
        /// （退完沒人了就整個移除），再開一個新的。回傳新的 lord；單位未生成或沒有派系時回傳 null。
        /// Hands a spawned pawn over to an alarm response (<see cref="LordJob_AlarmResponse"/>): it leaves its
        /// current lord (removing that lord if it is left empty) and gets a new one. Returns the new lord, or null
        /// when the pawn isn't spawned or has no faction.
        /// </summary>
        public static Lord JoinAlarmResponse(Pawn pawn, IntVec3 alarmCell, string listenSignal)
        {
            if (pawn == null || !pawn.Spawned || pawn.Faction == null) return null;

            Lord previous = pawn.GetLord();
            if (previous != null)
            {
                previous.RemovePawn(pawn);
                if (previous.ownedPawns.Count == 0)
                {
                    previous.lordManager.RemoveLord(previous);
                }
            }
            return LordMaker.MakeNewLord(pawn.Faction, new LordJob_AlarmResponse(alarmCell, listenSignal), pawn.Map,
                new List<Pawn> { pawn });
        }

        /// <summary>派系：指定的 → 建築自己的 → DMS 遺留部隊。Faction: explicit → the parent's → DMS_Legacy.</summary>
        public static Faction ResolveFaction(ThingComp comp, FactionDef spawnFactionDef)
        {
            Faction faction = null;
            if (spawnFactionDef != null)
            {
                faction = Find.FactionManager.FirstFactionOfDef(spawnFactionDef);
            }
            return faction ?? comp.parent.Faction ?? VaultRoomUtility.DefenderFaction;
        }

        public static float PointsFor(float alertPct, FloatRange pointsRange, SimpleCurve pointsByAlertLevel)
        {
            if (pointsByAlertLevel != null)
            {
                return pointsByAlertLevel.Evaluate(alertPct * MapComponent_AlertCounter.MaxAlertLevel);
            }
            return pointsRange.LerpThroughRange(alertPct);
        }

        public static List<Pawn> GeneratePawns(Faction faction, Map map, float alertPct,
            List<PawnKindDef> pawnKinds, IntRange countRange, FloatRange pointsRange, SimpleCurve pointsByAlertLevel,
            PawnGroupKindDef groupKind = null)
        {
            List<Pawn> pawns = new List<Pawn>();

            if (!pawnKinds.NullOrEmpty())
            {
                int count = Mathf.RoundToInt(Mathf.Lerp(countRange.min, countRange.max, alertPct));
                for (int i = 0; i < count; i++)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        pawnKinds.RandomElement(), faction, PawnGenerationContext.NonPlayer, map.Tile));
                    if (pawn != null) pawns.Add(pawn);
                }
                return pawns;
            }

            if (faction == null) return pawns;

            // 派系沒有指定種類的 pawnGroupMaker 時退回 Combat，免得產生失敗。
            // Fall back to Combat when the faction has no maker of the requested kind, so generation doesn't fail.
            if (groupKind == null || faction.def.pawnGroupMakers.NullOrEmpty()
                || !faction.def.pawnGroupMakers.Any(m => m.kindDef == groupKind))
            {
                groupKind = PawnGroupKindDefOf.Combat;
            }

            PawnGroupMakerParms parms = new PawnGroupMakerParms
            {
                groupKind = groupKind,
                faction = faction,
                points = PointsFor(alertPct, pointsRange, pointsByAlertLevel),
                tile = map.Tile,
            };
            pawns.AddRange(PawnGroupMakerUtility.GeneratePawns(parms));
            return pawns;
        }

        /// <summary>
        /// 建築要打的對象：與建築派系（沒有就與防守派系）敵對、還活著、已生成的 pawn。
        /// Who the building shoots at: spawned, living pawns hostile to the building's faction
        /// (or the defender faction when it has none).
        /// </summary>
        public static bool IsHostileTarget(Pawn pawn, Faction ownFaction)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned) return false;
            if (ownFaction == null) return pawn.Faction == Faction.OfPlayer;
            return pawn.HostileTo(ownFaction);
        }
    }
}
