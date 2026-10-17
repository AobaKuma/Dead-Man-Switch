using Verse;
using RimWorld;
using LudeonTK;
using System.Collections.Generic;

namespace DMS
{
    /// <summary>
    /// DMS Boss 群的抵達部件，同時是情境音樂的 Boss 遭遇紀錄：生成時登記 Boss 與護衛，由
    /// <see cref="DMSMusicEventBridge"/> 讀取接近、抵達、啟動與解除。任務結束後不再被讀取，所以不會殘留。
    ///
    /// The DMS bossgroup arrival part, which doubles as situational music's boss-encounter record: the bosses and
    /// escorts are registered at generation and <see cref="DMSMusicEventBridge"/> reads approach, arrival, activation
    /// and resolution from it. Once the quest ends it is no longer read, so nothing lingers.
    /// </summary>
    public class QuestPart_BossgroupArrivesWithMusic : QuestPart_BossgroupArrives
    {
        /// <summary>
        /// 這次遭遇的 Boss 曲變奏（召喚時選定，給情境音樂選曲用）。舊版在抵達時強制播放，現在不再強制播放；欄位保留以讀舊存檔。
        /// This encounter's boss-song variant (picked at summoning, for situational music to choose from). Older
        /// versions force-played it on arrival; it is no longer force-played, and the field stays for old saves.
        /// </summary>
        public SongDef song;

        /// <summary>情境音樂追蹤的 Boss；舊存檔為 null，第一次讀取時補建。The bosses situational music tracks; null in old saves until backfilled.</summary>
        public List<Pawn> musicBosses;

        /// <summary>情境音樂追蹤的護衛，不算 Boss。The escorts situational music tracks; never counted as bosses.</summary>
        public List<Pawn> musicEscorts;

        /// <summary>
        /// 抵達訊號已送出。原版完成時把狀態設為 Disabled；DMS_Boss 任務沒有停用這個部件的訊號，所以 Disabled 只代表已抵達。
        /// The arrival signal has gone out. Vanilla marks a completed part Disabled, and the DMS_Boss quest never disables
        /// this part otherwise, so Disabled means arrived.
        /// </summary>
        public bool Arrived => State == QuestPartState.Disabled;

        public override void QuestPartTick()
        {
            // 玩家已離開站點（站點還在）時，原版會以 null 地圖呼叫 ShouldSummonNow，每 2500 tick 拋一次 NullReferenceException。
            // 沒有地圖時只等最長延遲的強制抵達；投放會改投玩家據點。
            // With the player gone from a site that still exists, vanilla calls ShouldSummonNow with a null map and
            // throws every 2500 ticks. Without a map just wait for the forced arrival at the maximum delay; the drop
            // then retargets a player home.
            if (mapParent?.Map == null && Find.TickManager.TicksGame < enableTick + maxDelay)
            {
                return;
            }
            base.QuestPartTick();
        }

        public override void Complete(SignalArgs signalArgs)
        {
            // 舊存檔要在 QuestPart_Bossgroup.bosses 被清空前補建。Old saves must backfill before QuestPart_Bossgroup.bosses is cleared.
            EnsureMusicPawns();
            base.Complete(signalArgs);
            foreach (var q in quest.PartsListForReading)
            {
                if (q is QuestPart_Bossgroup questPart_Bossgroup)
                {
                    questPart_Bossgroup.bosses.Clear();
                }
            }
        }
        [DebugAction("DMS", "Flush quest bosses", false, false, false, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 1000)]
        public static void DebugInfo()
        {
            List<Quest> questsListForReading = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < questsListForReading.Count; i++)
            {
                if (questsListForReading[i].State != QuestState.Ongoing)
                {
                    continue;
                }
                List<QuestPart> partsListForReading = questsListForReading[i].PartsListForReading;
                for (int j = 0; j < partsListForReading.Count; j++)
                {
                    if (partsListForReading[j] is QuestPart_Bossgroup questPart_Bossgroup)
                    {
                        questPart_Bossgroup.bosses.Clear();
                    }
                }
            }
        }

        /// <summary>
        /// 舊存檔沒有 <see cref="musicBosses"/>：只從同一任務的 <see cref="QuestPart_Bossgroup"/> 補建，不會把其他同型機體當成 Boss。
        /// 抵達前用它的 <c>bosses</c>；抵達後 <c>bosses</c> 已被清空，改以 Boss 種類從它的 <c>pawns</c> 挑出。
        /// 只能在 tick 流程（或任務流程）呼叫：它會寫入存檔資料。
        ///
        /// Old saves have no <see cref="musicBosses"/>: backfill only from the same quest's
        /// <see cref="QuestPart_Bossgroup"/>, so no look-alike machine becomes a boss. Before arrival use its
        /// <c>bosses</c>; after arrival those were cleared, so pick the boss kind out of its <c>pawns</c>. Call it from
        /// the tick or quest flow only, since it writes saved data.
        /// </summary>
        public void EnsureMusicPawns()
        {
            if (musicBosses != null && musicEscorts != null)
            {
                return;
            }
            musicBosses = new List<Pawn>();
            musicEscorts = new List<Pawn>();
            QuestPart_Bossgroup group = null;
            List<QuestPart> parts = quest?.PartsListForReading;
            for (int i = 0; parts != null && i < parts.Count && group == null; i++)
            {
                group = parts[i] as QuestPart_Bossgroup;
            }
            if (group?.pawns == null)
            {
                return;
            }
            PawnKindDef bossKind = bossgroupDef?.boss?.kindDef;
            bool bossesKnown = !group.bosses.NullOrEmpty();
            foreach (Pawn pawn in group.pawns)
            {
                if (pawn == null)
                {
                    continue;
                }
                bool boss = bossesKnown ? group.bosses.Contains(pawn) : pawn.kindDef == bossKind;
                (boss ? musicBosses : musicEscorts).Add(pawn);
            }
        }

        public override void Notify_PawnDiscarded(Pawn pawn)
        {
            base.Notify_PawnDiscarded(pawn);
            musicBosses?.Remove(pawn);
            musicEscorts?.Remove(pawn);
        }

        public override void ReplacePawnReferences(Pawn replace, Pawn with)
        {
            base.ReplacePawnReferences(replace, with);
            musicBosses?.Replace(replace, with);
            musicEscorts?.Replace(replace, with);
        }

        public override void Cleanup()
        {
            base.Cleanup();
            // 任務結束後不再需要，避免歷史任務一直參照已死的 pawn。No longer needed once the quest ends; keeps history from pinning dead pawns.
            musicBosses?.Clear();
            musicEscorts?.Clear();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref song, "song");
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                musicBosses?.RemoveAll(p => p == null || p.Discarded);
                musicEscorts?.RemoveAll(p => p == null || p.Discarded);
            }
            Scribe_Collections.Look(ref musicBosses, "musicBosses", LookMode.Reference);
            Scribe_Collections.Look(ref musicEscorts, "musicEscorts", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                musicBosses?.RemoveAll(p => p == null);
                musicEscorts?.RemoveAll(p => p == null);
            }
        }
    }
}