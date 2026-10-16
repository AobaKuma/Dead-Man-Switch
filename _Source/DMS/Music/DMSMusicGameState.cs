namespace DMS
{
    /// <summary>
    /// 一張地圖的遊戲端音樂紀錄。只在 tick 流程（同步）內修改，欄位由 <see cref="MapComponent_DMSMusicState"/> 存檔；
    /// 這裡不依賴 RimWorld，所以測試可以直接驅動它。
    /// One map's game-side music record. It is only changed inside the (synced) tick flow and its fields are saved by
    /// <see cref="MapComponent_DMSMusicState"/>; it has no RimWorld dependency so tests can drive it directly.
    ///
    /// 第一次評估（新地圖或沒有這些欄位的舊存檔）只建立基準，不把既有的警戒水位、搜索或增援次數當成「剛發生」。
    /// The first evaluation (a new map, or an old save without these fields) only sets baselines; an existing alert level,
    /// sweep or raid count is not treated as something that just happened.
    /// </summary>
    public class DMSMusicGameState
    {
        public int lastEvaluatedTick = DMSMusicTuning.Never;

        public float lastAlertLevel;
        public int lastAlertRiseTick = DMSMusicTuning.Never;

        public bool searchActive;
        public int searchStartTick = DMSMusicTuning.Never;

        public int lastRaidsSentCount;
        public int lastReinforcementArrivalTick = DMSMusicTuning.Never;

        public int lastCombatTick = DMSMusicTuning.Never;
        public int lastActiveBosses;

        public bool crisisActive;
        public int crisisCounter;
        public int lastCrisisEndTick = DMSMusicTuning.Never;

        public bool Initialized => lastEvaluatedTick != DMSMusicTuning.Never;

        /// <summary>
        /// 危機進行中或餘波窗口仍開著：即使地圖沒有情境、遭遇也已結束，仍要完整讀取來源。
        /// A crisis is running or its aftermath window is open: read every source even when the map has no context and
        /// the encounter is over.
        /// </summary>
        public bool Tracking(int now)
        {
            return crisisActive || (crisisCounter > 0 && Within(lastCrisisEndTick, now, DMSMusicTuning.AftermathWindowTicks));
        }

        /// <summary>記錄一次敵我交火（由開火 hook 在 tick 流程內呼叫）。Records hostile fire; called by the firing hook inside the tick flow.</summary>
        public void NoteCombat(int now)
        {
            lastCombatTick = now;
        }

        /// <summary>開發者工具：注入一次警戒上升。Developer tools: inject one alert rise.</summary>
        public void NoteAlertRise(int now)
        {
            lastAlertRiseTick = now;
        }

        /// <summary>
        /// 開發者工具：清掉所有事件窗口（交戰、警戒上升、搜索、增援抵達），讓地圖回到基準。危機編號與基準值保留。
        /// Developer tools: clear every event window (combat, alert rise, sweep, reinforcement arrival) so the map returns
        /// to its baseline. Crisis ids and baselines are kept.
        /// </summary>
        public void ClearWindows()
        {
            lastCombatTick = DMSMusicTuning.Never;
            lastAlertRiseTick = DMSMusicTuning.Never;
            searchStartTick = DMSMusicTuning.Never;
            lastReinforcementArrivalTick = DMSMusicTuning.Never;
        }

        public DMSMusicFacts Evaluate(in DMSMusicInputs inputs, int now)
        {
            if (Initialized)
            {
                if (inputs.alertLevel > lastAlertLevel + DMSMusicTuning.AlertRiseEpsilon)
                {
                    lastAlertRiseTick = now;
                }
                if (inputs.gravekeeperSearch && !searchActive)
                {
                    searchStartTick = now;
                }
                if (inputs.raidsSentCount > lastRaidsSentCount)
                {
                    lastReinforcementArrivalTick = now;
                }
                if (lastActiveBosses > 0 && inputs.activeBosses == 0)
                {
                    // Boss 倒下時把擊殺它的那幾發排除在交戰窗口外：沒有護衛繼續開火就直接結束危機，
                    // 不會在 Boss 曲之後再接一段一般交戰曲。護衛還在打，下一次刷新就會重新記錄。
                    // When the bosses fall, the shots that killed them leave the combat window: with no escort still
                    // firing the crisis ends at once instead of tailing the boss music with regular combat music.
                    // Escorts still fighting are picked up again on the next refresh.
                    lastCombatTick = DMSMusicTuning.Never;
                }
            }

            lastAlertLevel = inputs.alertLevel;
            searchActive = inputs.gravekeeperSearch;
            lastRaidsSentCount = inputs.raidsSentCount;
            lastActiveBosses = inputs.activeBosses;
            lastEvaluatedTick = now;

            DMSMusicReason reasons = DMSMusicReason.None;
            if (Within(lastCombatTick, now, DMSMusicTuning.CombatWindowTicks))
            {
                reasons |= DMSMusicReason.CombatShots;
            }
            if (Within(inputs.gravekeeperSightTick, now, DMSMusicTuning.GravekeeperContactTicks))
            {
                reasons |= DMSMusicReason.GravekeeperContact;
            }
            if (Within(lastAlertRiseTick, now, DMSMusicTuning.AlertRiseWindowTicks))
            {
                reasons |= DMSMusicReason.AlertRise;
            }
            if (inputs.gravekeeperChallenge)
            {
                reasons |= DMSMusicReason.GravekeeperChallenge;
            }
            if (searchActive && Within(searchStartTick, now, DMSMusicTuning.SearchAlertWindowTicks))
            {
                reasons |= DMSMusicReason.GravekeeperSearch;
            }
            if (inputs.reinforcementTicksLeft >= 0 && inputs.reinforcementTicksLeft <= DMSMusicTuning.ApproachWarningTicks)
            {
                reasons |= DMSMusicReason.ReinforcementsApproaching;
            }
            if (Within(lastReinforcementArrivalTick, now, DMSMusicTuning.ReinforcementArrivalWindowTicks))
            {
                reasons |= DMSMusicReason.ReinforcementsArrived;
            }
            if (inputs.bossTicksUntilArrival >= 0 && inputs.bossTicksUntilArrival <= DMSMusicTuning.ApproachWarningTicks)
            {
                reasons |= DMSMusicReason.BossApproaching;
            }
            if (inputs.airspaceBlocked)
            {
                reasons |= DMSMusicReason.AirspaceBlocked;
            }

            DMSMusicFacts facts = new DMSMusicFacts
            {
                context = inputs.context,
                playerPresent = inputs.playerPresent,
                hasEncounter = inputs.hasEncounter,
                lockdown = inputs.lockdown,
                activeBosses = inputs.activeBosses,
                reasons = reasons,
                songTags = inputs.songTags,
                alertLevel = inputs.alertLevel,
                reinforcementTicksLeft = inputs.reinforcementTicksLeft,
                bossTicksUntilArrival = inputs.bossTicksUntilArrival,
            };

            bool crisisNow = facts.HasCombatReason || facts.activeBosses > 0 || facts.LockdownCountingDown || facts.Trapped;
            if (crisisNow && !crisisActive)
            {
                crisisCounter++;
            }
            else if (!crisisNow && crisisActive)
            {
                lastCrisisEndTick = now;
            }
            crisisActive = crisisNow;

            facts.crisisActive = crisisNow;
            facts.crisisId = crisisCounter;
            facts.aftermathCrisisId = !crisisNow && crisisCounter > 0 && Within(lastCrisisEndTick, now, DMSMusicTuning.AftermathWindowTicks)
                ? crisisCounter
                : 0;
            return facts;
        }

        private static bool Within(int tick, int now, int window)
        {
            return tick != DMSMusicTuning.Never && now - tick <= window;
        }
    }
}
