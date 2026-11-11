using System;
using System.Collections.Generic;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 任務結束後仍需保存的狀態：
    /// 1. 待授予的封存檔案（前置研究完成後由 <see cref="DeviantTechUtility"/> 自動解譯）：脫序者交付的，
    ///    以及具名封存匣解封時帶進來的，兩份分開存，因為完成信件的措辭不同。
    /// 2. 延後執行的軍事法庭傳票整理（<see cref="CourtMartialOfferUtility"/>）。
    /// 3. 脫序者追殺的「活要見人，死要收屍」：脫序者離開／回到殖民地時發信（追殺休眠與恢復由
    ///    <see cref="HuntdownWorker_Deviant"/> 自己判定），以及死後盯著屍體，被復活就重新啟動追殺。
    /// State that has to outlive the quest: sealed files still waiting on their prerequisites (deviant handovers
    /// and casket unsealings tracked separately, since the completion letters word them differently), the
    /// deferred court-martial summons refresh, and the deviant hunt's "seen alive or buried" bookkeeping: letters when
    /// the deviant leaves or returns (the worker itself decides the dormancy), and a watch on a dead deviant that
    /// restarts the hunt if they are revived.
    /// </summary>
    public class GameComponent_DeviantLegacy : GameComponent
    {
        private const int CheckIntervalTicks = 250;

        private List<ResearchProjectDef> pendingGrants = new List<ResearchProjectDef>();

        /// <summary>具名封存匣解封時前置未完成的檔案。Files held back by casket unsealings.</summary>
        private List<ResearchProjectDef> pendingCasketGrants = new List<ResearchProjectDef>();

        // 傳票整理：-1 = 沒有待辦。必須晚於登記的那個 tick，確保觸發它的軍事法庭任務已經結束。
        // Summons refresh: -1 = none queued. Runs strictly after the tick it was requested, so the court-martial quest
        // that requested it has already ended.
        private int courtRefreshRequestedTick = -1;
        private bool courtRefreshEnsureOffer;

        /// <summary>
        /// 已為脫序者離開殖民地發過「追殺暫緩」信件（人還在某張地圖上時才發）；回來時據此發「追殺恢復」。
        /// The "hunt on hold" letter went out (sent only while the deviant is still on a map); a return answers it.
        /// </summary>
        private bool deviantAway;

        /// <summary>
        /// 場外逮捕的預定 tick（-1 = 未排定）。脫序者離開所有地圖時排在 1~3 天後；期間回到殖民地或回到地圖上就取消。
        /// Tick the off-map capture is due (-1 = none). Set 1~3 days out once the deviant is on no map; cancelled if they
        /// come back to the colony or onto a map first.
        /// </summary>
        private int captureDueTick = -1;

        /// <summary>場外逮捕的延遲範圍（1~3 天）。Delay before the off-map capture: 1~3 days.</summary>
        private static readonly IntRange CaptureDelayTicks = new IntRange(GenDate.TicksPerDay, 3 * GenDate.TicksPerDay);

        /// <summary>死亡結束追殺的脫序者；被復活就重新啟動追殺，屍體消失就放下。A deviant whose death ended the hunt; revived → hunt again.</summary>
        private Pawn revivalWatch;

        /// <summary>艦隊收押中的脫序者（<see cref="DeviantCustodyUtility"/>）。Deviants held in fleet custody.</summary>
        private List<Pawn> custody = new List<Pawn>();
        private HashSet<Pawn> custodySet;

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

        public List<ResearchProjectDef> PendingCasketGrants => pendingCasketGrants;

        /// <summary>兩份清單的總和是否為空。Whether both pending lists are empty.</summary>
        public bool AnyPending => pendingGrants.Count > 0 || pendingCasketGrants.Count > 0;

        /// <summary>任一來源登記的待解譯檔案。Pending from either source.</summary>
        public bool IsPending(ResearchProjectDef proj) =>
            proj != null && (pendingGrants.Contains(proj) || pendingCasketGrants.Contains(proj));

        public void AddPending(ResearchProjectDef proj)
        {
            if (proj != null && !pendingGrants.Contains(proj)) pendingGrants.Add(proj);
        }

        public void AddPendingCasket(ResearchProjectDef proj)
        {
            if (proj != null && !pendingCasketGrants.Contains(proj)) pendingCasketGrants.Add(proj);
        }

        public bool RemovePending(ResearchProjectDef proj) =>
            pendingGrants.Remove(proj) | pendingCasketGrants.Remove(proj);

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

        /// <summary>死亡結束追殺時呼叫（<see cref="HuntdownWorker_Deviant"/>）。Called when death ends the hunt.</summary>
        public void WatchForRevival(Pawn pawn)
        {
            if (pawn != null && !pawn.Discarded && !pawn.Destroyed) revivalWatch = pawn;
        }

        public void AddToCustody(Pawn pawn)
        {
            if (pawn == null || custody.Contains(pawn)) return;
            custody.Add(pawn);
            custodySet = null;
        }

        public bool InCustody(Pawn pawn)
        {
            if (pawn == null || custody.Count == 0) return false;
            custodySet ??= new HashSet<Pawn>(custody);
            return custodySet.Contains(pawn);
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            // 脫序者的去向用 TickLong 間隔檢查就夠了：場外逮捕本來就要等 1~3 天。
            // The deviant's whereabouts only need a TickLong cadence; the off-map capture waits 1~3 days anyway.
            if (now % GenTicks.TickLongInterval == 0)
            {
                try
                {
                    CheckDeviantAway();
                    CheckRevival();
                }
                catch (Exception ex)
                {
                    Log.Error($"[DMS] Deviant hunt check failed: {ex}");
                }
            }
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

        /// <summary>
        /// 追蹤脫序者的去向（每個 TickLong）：
        /// - 回到殖民地：取消待辦的逮捕；發過「暫緩」信件的就發「恢復」。
        /// - 不在任何地圖上：排定 1~3 天後的場外逮捕，到期時人還在外面就收押。期間不發信，玩家只會收到落網通知。
        /// - 還在某張地圖上：取消待辦的逮捕，發一次「暫緩」信件。
        /// 狀態有變就整理傳票（軍事法庭的被告只能是受庇護的脫序者）。
        /// Tracks the deviant's whereabouts every TickLong: back home → any pending capture is cancelled, and a "resumed"
        /// letter answers an earlier "on hold" one; on no map → an off-map capture is scheduled 1~3 days out and carried
        /// out if they are still away when it comes due, silently until then, so the player only hears of the arrest; still
        /// on some map → no capture, one "on hold" letter. Any change refreshes the summonses (a deviant trial needs the
        /// deviant at home).
        /// </summary>
        private void CheckDeviantAway()
        {
            Pawn deviant = DeviantHuntUtility.BoundDeviant;
            if (deviant == null || DeviantHuntUtility.IsGoneForGood(deviant))
            {
                // 沒有追殺，或即將因死亡結束（結束信件由 worker 發）。No hunt, or ending on death (the worker sends that letter).
                deviantAway = false;
                captureDueTick = -1;
                return;
            }

            string fleet = DMS_DefOf.DMS_DeviantHunt.Worker.Faction?.Name ?? "?";
            if (!DeviantHuntUtility.IsAway(deviant))
            {
                bool wasPending = captureDueTick >= 0;
                captureDueTick = -1;
                if (deviantAway)
                {
                    deviantAway = false;
                    Find.LetterStack.ReceiveLetter(
                        "DMS_DeviantHunt_ReturnedLabel".Translate(deviant.Named("DEVIANT")),
                        "DMS_DeviantHunt_ReturnedText".Translate(deviant.Named("DEVIANT"), fleet.Named("FLEET")),
                        LetterDefOf.ThreatBig, deviant.Spawned ? new LookTargets(deviant) : null);
                    CourtMartialOfferUtility.RequestRefresh(ensureOffer: false);
                }
                else if (wasPending)
                {
                    CourtMartialOfferUtility.RequestRefresh(ensureOffer: false);
                }
                return;
            }

            if (!deviant.Spawned)
            {
                int now = Find.TickManager.TicksGame;
                if (captureDueTick < 0)
                {
                    captureDueTick = now + CaptureDelayTicks.RandomInRange;
                    CourtMartialOfferUtility.RequestRefresh(ensureOffer: false);
                }
                else if (now >= captureDueTick)
                {
                    Capture(deviant);
                }
                return;
            }

            // 回到某張地圖上（例如被帶著來襲）：逮捕作廢，離開地圖時重新排定。Back on a map: no capture; rescheduled on leaving.
            captureDueTick = -1;
            if (deviantAway) return;
            deviantAway = true;
            Find.LetterStack.ReceiveLetter(
                "DMS_DeviantHunt_AwayLabel".Translate(deviant.Named("DEVIANT")),
                "DMS_DeviantHunt_AwayText".Translate(deviant.Named("DEVIANT"), fleet.Named("FLEET")),
                LetterDefOf.NeutralEvent, new LookTargets(deviant));
            CourtMartialOfferUtility.RequestRefresh(ensureOffer: false);
        }

        /// <summary>
        /// 活要見人：脫序者還活著、不在殖民地手上、也不在任何地圖上（放逐後走出地圖、被綁架、被別的派系帶走），
        /// 艦隊在 1~3 天後於場外把人逮捕歸案（排程見 <see cref="CheckDeviantAway"/>）。人交給艦隊收押，追殺結束；
        /// 與艦隊的敵對不會因此解除。還在某張地圖上的（例如改屬別的派系、仍在場上）先休眠，等他離開地圖再排定。
        /// Seen alive: a deviant alive, out of the colony's hands and on no map (banished off the map, kidnapped, taken by
        /// another faction) is apprehended off-map 1~3 days later (scheduled in <see cref="CheckDeviantAway"/>). The fleet
        /// takes them into custody and the hunt ends; hostility stays. One still on some map just sleeps the hunt until
        /// they leave it.
        /// </summary>
        private void Capture(Pawn deviant)
        {
            Faction fleet = DMS_DefOf.DMS_DeviantHunt.Worker.Faction;
            // 先記下信件內容：收押後 pawn 改屬艦隊。Build the letter first: custody changes the pawn's faction.
            string fleetName = fleet?.Name ?? "?";
            TaggedString label = "DMS_DeviantHunt_CapturedLabel".Translate(deviant.Named("DEVIANT"));
            TaggedString text = "DMS_DeviantHunt_CapturedText".Translate(deviant.Named("DEVIANT"), fleetName.Named("FLEET"));

            DeviantCustodyUtility.TakeIntoCustody(deviant, fleet);
            deviantAway = false;
            captureDueTick = -1;
            // 不經 BoundPawnLost，worker 不會另發結束信件。Not through BoundPawnLost, so the worker sends no letter of its own.
            HuntdownUtility.StopAllBoundTo(deviant);
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NegativeEvent, null, fleet);
            CourtMartialOfferUtility.RequestRefresh(ensureOffer: false);
        }

        /// <summary>
        /// 死要收屍：被復活的脫序者重新被追殺（艦隊同時恢復敵對）。屍體被毀、pawn 被移除就不再盯。
        /// Buried, not forgotten: a revived deviant is hunted again and the fleet turns hostile again. A destroyed body or a
        /// discarded pawn ends the watch.
        /// </summary>
        private void CheckRevival()
        {
            Pawn pawn = revivalWatch;
            if (pawn == null) return;
            if (pawn.Discarded || pawn.Destroyed)
            {
                revivalWatch = null;
                return;
            }
            if (pawn.Dead) return;
            revivalWatch = null;

            HuntdownDef def = DMS_DefOf.DMS_DeviantHunt;
            if (def == null || HuntdownUtility.IsActive(def)) return;
            Faction fleet = def.Worker.Faction;
            if (fleet == null || fleet.defeated) return;

            Map map = pawn.MapHeld != null && pawn.MapHeld.IsPlayerHome ? pawn.MapHeld : Find.AnyPlayerHomeMap;
            HuntdownUtility.Start(def, map, source: "DeviantRevived", boundPawn: pawn);
            FleetTrackingUtility.InheritBlackout(def);
            // 人已不在殖民地手上時由 CheckDeviantAway 發「離開」信件；在殖民地就立刻恢復敵對。
            // Away from the colony: CheckDeviantAway sends the away letter. At home: hostility returns at once.
            deviantAway = false;
            captureDueTick = -1;
            if (!DeviantHuntUtility.IsAway(pawn) && !OccultechSanctionUtility.CourtMartialOngoing)
            {
                OccultechSanctionUtility.SetFleetHostile(fleet, DMS_DefOf.DMS_HarboredDeviant);
            }
            Find.LetterStack.ReceiveLetter(
                "DMS_DeviantHunt_RevivedLabel".Translate(pawn.Named("DEVIANT")),
                "DMS_DeviantHunt_RevivedText".Translate(pawn.Named("DEVIANT"), fleet.Name.Named("FLEET")),
                LetterDefOf.ThreatBig, pawn.Spawned ? new LookTargets(pawn) : null, fleet);
            CourtMartialOfferUtility.RequestRefresh(ensureOffer: false);
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
            Scribe_Collections.Look(ref pendingCasketGrants, "dms_casketPendingGrants", LookMode.Def);
            Scribe_Values.Look(ref courtRefreshRequestedTick, "dms_deviantCourtRefreshTick", -1);
            Scribe_Values.Look(ref courtRefreshEnsureOffer, "dms_deviantCourtRefreshEnsureOffer");
            Scribe_Values.Look(ref deviantAway, "dms_deviantHuntAway");
            Scribe_Values.Look(ref captureDueTick, "dms_deviantCaptureDueTick", -1);
            Scribe_References.Look(ref revivalWatch, "dms_deviantRevivalWatch");
            Scribe_Collections.Look(ref custody, "dms_deviantCustody", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pendingGrants ??= new List<ResearchProjectDef>();
                pendingGrants.RemoveAll(p => p == null);
                pendingCasketGrants ??= new List<ResearchProjectDef>();
                pendingCasketGrants.RemoveAll(p => p == null);
                custody ??= new List<Pawn>();
                custody.RemoveAll(p => p == null);
                custodySet = null;
            }
        }
    }
}
