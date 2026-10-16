using System;
using System.Collections.Generic;
using Verse;

namespace DMS
{
    /// <summary>
    /// 脫序者接納任務在任務結束後仍需保存的狀態：
    /// 1. 待授予的封存檔案（前置研究完成後由 <see cref="DeviantTechUtility"/> 自動解譯）。
    /// 2. 延後執行的軍事法庭傳票整理（<see cref="CourtMartialOfferUtility"/>）。
    /// State the deviant refuge quest needs after the quest itself has ended: the sealed files still waiting on their
    /// prerequisites, and the deferred court-martial summons refresh.
    /// </summary>
    public class GameComponent_DeviantLegacy : GameComponent
    {
        private const int CheckIntervalTicks = 250;

        private List<ResearchProjectDef> pendingGrants = new List<ResearchProjectDef>();

        // 傳票整理：-1 = 沒有待辦。必須晚於登記的那個 tick，確保觸發它的軍事法庭任務已經結束。
        // Summons refresh: -1 = none queued. Runs strictly after the tick it was requested, so the court-martial quest
        // that requested it has already ended.
        private int courtRefreshRequestedTick = -1;
        private bool courtRefreshEnsureOffer;

        private static Game cachedGame;
        private static GameComponent_DeviantLegacy cached;

        public GameComponent_DeviantLegacy(Game game) { }

        public static GameComponent_DeviantLegacy CompSafe
        {
            get
            {
                Game game = Current.Game;
                if (game == null)
                {
                    cachedGame = null;
                    cached = null;
                    return null;
                }
                if (!ReferenceEquals(game, cachedGame))
                {
                    cachedGame = game;
                    cached = game.GetComponent<GameComponent_DeviantLegacy>();
                }
                return cached;
            }
        }

        public List<ResearchProjectDef> PendingGrants => pendingGrants;

        public bool IsPending(ResearchProjectDef proj) => proj != null && pendingGrants.Contains(proj);

        public void AddPending(ResearchProjectDef proj)
        {
            if (proj != null && !pendingGrants.Contains(proj)) pendingGrants.Add(proj);
        }

        public bool RemovePending(ResearchProjectDef proj) => pendingGrants.Remove(proj);

        /// <summary>
        /// 登記一次傳票整理。ensureOffer：即使沒有撤銷任何傳票，也要確保手上有一份應有種類的傳票。
        /// Queues a summons refresh. ensureOffer: make sure a summons of the expected kind is on offer even when nothing
        /// was withdrawn.
        /// </summary>
        public void RequestCourtRefresh(bool ensureOffer)
        {
            courtRefreshRequestedTick = Find.TickManager.TicksGame;
            courtRefreshEnsureOffer |= ensureOffer;
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (courtRefreshRequestedTick < 0 || now <= courtRefreshRequestedTick || now % CheckIntervalTicks != 0) return;
            bool ensureOffer = courtRefreshEnsureOffer;
            courtRefreshRequestedTick = -1;
            courtRefreshEnsureOffer = false;
            try
            {
                CourtMartialOfferUtility.Refresh(ensureOffer);
            }
            catch (Exception ex)
            {
                Log.Error($"[DMS] Court-martial summons refresh failed: {ex}");
            }
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            // 讀檔期間前置可能已被其他模組或除錯動作完成。Prerequisites may have been completed while the game was away.
            DeviantTechUtility.CheckPending();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pendingGrants, "dms_deviantPendingGrants", LookMode.Def);
            Scribe_Values.Look(ref courtRefreshRequestedTick, "dms_deviantCourtRefreshTick", -1);
            Scribe_Values.Look(ref courtRefreshEnsureOffer, "dms_deviantCourtRefreshEnsureOffer");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pendingGrants ??= new List<ResearchProjectDef>();
                pendingGrants.RemoveAll(p => p == null);
            }
        }
    }
}
