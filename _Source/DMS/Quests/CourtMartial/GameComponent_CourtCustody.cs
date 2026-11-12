using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DMS
{
    /// <summary>
    /// 軍事法庭被告被艦隊收押期間凍結：把世界 pawn 直接移進原版的封存（mothball）名單。
    /// 封存的世界 pawn 不跑 DoTick（健康、免疫、疾病、hediff giver、需求全停），只每 15000 tick 推進年齡與紀錄。
    /// 原版本來就會在 15000 tick 節點封存沒有活躍 hediff 的借出殖民者，這裡只是不等節點、也不讓活躍 hediff 擋下來；
    /// 歸還時空投艙收進被告會走 WorldPawns.RemovePawn，封存自動解除。不 patch 任何原版方法。
    ///
    /// 凍結只限收押期間：記下自己凍結過的 pawn，任務不再進行或借出 part 結束（例如休戰破裂、人沒被送回）就放回
    /// pawnsAlive，交還原版與脫序者追殺（<see cref="DeviantHuntUtility"/>、<see cref="GameComponent_DeviantLegacy"/>）
    /// 照原本的方式處理。艦隊收押的脫序者（<see cref="DeviantCustodyUtility"/>）不在借出名單上，這裡不碰。
    ///
    /// While the fleet holds a court-martial defendant they are moved straight into vanilla's mothballed world pawns.
    /// Mothballed world pawns skip DoTick (health, immunity, disease, hediff givers, needs all stop) and only age and
    /// record every 15000 ticks. Vanilla already mothballs lent colonists without active hediffs at that interval; this
    /// just skips the wait and the hediff check. The return drop pod takes them via WorldPawns.RemovePawn, which
    /// un-mothballs them. No vanilla method is patched.
    ///
    /// The freeze lasts only while held: pawns frozen here are remembered and put back in pawnsAlive once the quest stops
    /// or the lend ends without a return (e.g. a broken truce), leaving them to vanilla and the deviant hunt exactly as
    /// before. Deviants in fleet custody (<see cref="DeviantCustodyUtility"/>) aren't on any lend list and aren't touched.
    /// </summary>
    public class GameComponent_CourtCustody : GameComponent
    {
        private const int CheckIntervalTicks = 60;

        /// <summary>由本元件移進封存名單的 pawn。Pawns this component moved into the mothballed set.</summary>
        private List<Pawn> frozen = new List<Pawn>();

        private static readonly HashSet<Pawn> tmpHeld = new HashSet<Pawn>();

        public GameComponent_CourtCustody(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0) return;
            WorldPawns worldPawns = Find.WorldPawns;
            List<Quest> quests = Find.QuestManager?.QuestsListForReading;
            if (worldPawns == null || quests == null) return;
            if (frozen.Count == 0 && !quests.Any(IsCourtMartialOngoing)) return;

            tmpHeld.Clear();
            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];
                if (!IsCourtMartialOngoing(quest)) continue;
                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (!(part is QuestPart_LendColonistsToFaction lend) || lend.State != QuestPartState.Enabled) continue;
                    foreach (Thing t in lend.LentColonistsListForReading)
                    {
                        if (t is Pawn p && !p.Dead) tmpHeld.Add(p);
                    }
                }
            }

            // 凍結：起飛訊號早於離場，人還在離場的穿梭機裡時不是世界 pawn，下一輪再處理。
            // Freeze: the launch signal fires before the shuttle leaves; not a world pawn yet, retry next pass.
            foreach (Pawn p in tmpHeld)
            {
                if (worldPawns.pawnsAlive.Remove(p))
                {
                    worldPawns.pawnsMothballed.Add(p);
                    if (!frozen.Contains(p)) frozen.Add(p);
                }
            }

            // 解凍：不再收押卻仍在封存名單裡的，放回 pawnsAlive；已不是世界 pawn 的（已歸還、已移除）直接忘掉。
            // Thaw: no longer held but still mothballed → back to pawnsAlive; no longer a world pawn (returned, removed) → forget.
            for (int i = frozen.Count - 1; i >= 0; i--)
            {
                Pawn p = frozen[i];
                if (p != null && tmpHeld.Contains(p)) continue;
                frozen.RemoveAt(i);
                if (p != null && worldPawns.pawnsMothballed.Remove(p))
                {
                    worldPawns.pawnsAlive.Add(p);
                }
            }
            tmpHeld.Clear();
        }

        /// <summary>以 QuestPart_CourtVerdict 辨識軍事法庭任務，不綁 root def。Court-martials are identified by their verdict part.</summary>
        private static bool IsCourtMartialOngoing(Quest quest)
        {
            return quest.State == QuestState.Ongoing && quest.PartsListForReading.Any(p => p is QuestPart_CourtVerdict);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref frozen, "dms_courtCustodyFrozen", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                frozen ??= new List<Pawn>();
                frozen.RemoveAll(p => p == null);
            }
        }
    }
}
