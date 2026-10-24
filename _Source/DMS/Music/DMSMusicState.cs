using System;
using System.Collections.Generic;

namespace DMS
{
    // 本檔與 DMSMusicGameState.cs、DMSMusicClientState.cs 只用 System，不引用 Verse／RimWorld／UnityEngine，
    // 讓 _Source/DMS.MusicTests 能直接連結這三個檔案測試狀態規則。
    // This file, DMSMusicGameState.cs and DMSMusicClientState.cs use System only (no Verse/RimWorld/UnityEngine),
    // so _Source/DMS.MusicTests can link the three files and test the state rules directly.

    /// <summary>
    /// 情境音樂的八種狀態，越往下越緊急；<see cref="None"/> 代表交還原版。
    /// The eight situational-music states, more urgent further down; <see cref="None"/> hands music back to vanilla.
    /// </summary>
    public enum DMSMusicState : byte
    {
        None,
        InstallationExplore,
        FacilityExplore,
        Aftermath,
        SecurityAlert,
        FacilityTrapped,
        InstallationCombat,
        BossCombat,
        LockdownCountdown,
    }

    /// <summary>地圖的音樂情境：地表站點或地下設施。The map's music context: surface installation or underground facility.</summary>
    public enum DMSMusicContext : byte
    {
        None,
        Installation,
        Facility,
    }

    /// <summary>
    /// FFF <c>FacilityLockdownState</c> 的鏡像，讓狀態規則不必引用 Fortified.dll。
    /// A mirror of FFF's <c>FacilityLockdownState</c> so the state rules need not reference Fortified.dll.
    /// </summary>
    public enum DMSLockdownPhase : byte
    {
        Idle,
        Countdown,
        Locked,
        Sealed,
        Disarmed,
        Lost,
    }

    /// <summary>
    /// 狀態的成因。交戰類撐起 <see cref="DMSMusicState.InstallationCombat"/>，警戒類撐起 <see cref="DMSMusicState.SecurityAlert"/>。
    /// What holds a state up: combat reasons hold <see cref="DMSMusicState.InstallationCombat"/>, alert reasons hold
    /// <see cref="DMSMusicState.SecurityAlert"/>.
    /// </summary>
    [Flags]
    public enum DMSMusicReason : ushort
    {
        None = 0,

        /// <summary>交戰紀錄窗口內有敵我雙方的開火。Hostile fire between the player and an enemy inside the combat window.</summary>
        CombatShots = 1 << 0,
        /// <summary>交戰或搜索中的守墓人近期看見入侵者。An engaging or searching gravekeeper squad saw an intruder recently.</summary>
        GravekeeperContact = 1 << 1,

        /// <summary>警戒值近期上升。The alert level rose recently.</summary>
        AlertRise = 1 << 4,
        /// <summary>守墓人正在盤查。A gravekeeper squad is challenging an intruder.</summary>
        GravekeeperChallenge = 1 << 5,
        /// <summary>守墓人搜索剛開始。A gravekeeper sweep has just begun.</summary>
        GravekeeperSearch = 1 << 6,
        /// <summary>增援即將抵達。Reinforcements are about to arrive.</summary>
        ReinforcementsApproaching = 1 << 7,
        /// <summary>增援剛抵達。Reinforcements have just arrived.</summary>
        ReinforcementsArrived = 1 << 8,
        /// <summary>登記的 Boss 即將抵達。A registered boss is about to arrive.</summary>
        BossApproaching = 1 << 9,
        /// <summary>空域封鎖中。The airspace is blockaded.</summary>
        AirspaceBlocked = 1 << 10,

        Combat = CombatShots | GravekeeperContact,
        Alert = AlertRise | GravekeeperChallenge | GravekeeperSearch | ReinforcementsApproaching | ReinforcementsArrived
            | BossApproaching | AirspaceBlocked,
    }

    /// <summary>選曲用的遭遇標記，不影響狀態本身。Encounter tags used to pick songs; they never change the state itself.</summary>
    [Flags]
    public enum DMSMusicSongTag : byte
    {
        None = 0,
        Huntdown = 1 << 0,
        AlliedEngaged = 1 << 1,
        MainBattle = 1 << 2,
    }

    /// <summary>
    /// 整個 DMS 音樂讓位的原因（結果為 <see cref="DMSMusicState.None"/>）。
    /// Why DMS music stands aside entirely (the result is <see cref="DMSMusicState.None"/>).
    /// </summary>
    public enum DMSMusicYield : byte
    {
        None,
        Disabled,
        WorldView,
        NoMap,
        ForcedSong,
    }

    /// <summary>
    /// 時間與門檻的初始值（見 Plans/DMS_Music_Transitions_實作計畫.md 3.3）。tick 值在遊戲端換算，秒數是客戶端未暫停的實際時間。
    /// Initial timings and thresholds. Tick values are evaluated game-side; seconds are the client's unpaused real time.
    /// </summary>
    public static class DMSMusicTuning
    {
        /// <summary>「從未發生」的 tick；與任何遊戲 tick 相減都遠大於所有窗口。A "never happened" tick, far outside every window.</summary>
        public const int Never = -999999;

        public const int RefreshIntervalTicks = 60;

        public const int CombatWindowTicks = 600;
        public const int GravekeeperContactTicks = 600;
        public const int AlertRiseWindowTicks = 2500;
        public const int SearchAlertWindowTicks = 2500;
        public const int ReinforcementArrivalWindowTicks = 2500;
        public const int ApproachWarningTicks = 1800;

        /// <summary>
        /// 危機結束後可開始餘波的窗口。3 倍速下停戰緩衝（12 秒）加淡出（4 秒）約 2900 tick，所以不能用 2500。
        /// How long after a crisis ends the aftermath may still start. At 3x the ceasefire hold (12 s) plus the
        /// fade-out (4 s) is about 2900 ticks, so 2500 is too short.
        /// </summary>
        public const int AftermathWindowTicks = 5000;

        /// <summary>
        /// 追殺襲擊發動時，要有至少 <see cref="HuntdownSongMinHeavies"/> 架 combatPower 達到這個值（鬣蜥 DMS_Mech_Iguana 的 300）
        /// 的重型機兵，才加上追殺選曲標記；較弱的追殺仍算遭遇，播一般交戰曲。
        /// A huntdown raid gets the huntdown song tag only when it fires with at least <see cref="HuntdownSongMinHeavies"/>
        /// heavies of this combatPower or more (the Iguana, DMS_Mech_Iguana, at 300); weaker hunts still count as
        /// encounters and play the regular combat music.
        /// </summary>
        public const float HuntdownSongHeavyPower = 300f;

        public const int HuntdownSongMinHeavies = 4;

        public const float CeasefireSeconds = 12f;
        public const float AlertHoldSeconds = 15f;

        /// <summary>警戒值是 0–100；高於上次這麼多才算上升。The alert level runs 0–100; it must exceed the last reading by this much to count as a rise.</summary>
        public const float AlertRiseEpsilon = 0.001f;
    }

    /// <summary>
    /// 序列層的規則。原版只在 transition 不成立「且」過了 <c>minTimeToPlay</c> 才結束序列；
    /// 換了地圖或整個讓位時不該被最低播放時間鎖住，這裡決定何時提前結束。
    /// Sequence-level rules. Vanilla only ends a sequence when its transition fails and <c>minTimeToPlay</c> has passed;
    /// a map change or a full yield should not be held by the minimum play time, and this decides when to end early.
    /// </summary>
    public static class DMSMusicSequenceRules
    {
        /// <summary>
        /// 目前狀態已不是這個序列的狀態，而且是讓位（None）或換了地圖：立即結束。同一張地圖上的降級照原版規則淡出。
        /// 結果為 true 時 transition 必然不成立，所以淡出後不會被同一個 transition 重新觸發。
        /// The current state is no longer this sequence's and DMS yields (None) or the map changed: end now. A downgrade on
        /// the same map fades out by vanilla's rules. When this is true the transition is necessarily unsatisfied, so the
        /// same transition cannot re-trigger after the fade.
        /// </summary>
        public static bool EndsEarly(DMSMusicState mine, DMSMusicState current, bool sameMap)
        {
            return current != mine && (current == DMSMusicState.None || !sameMap);
        }
    }

    /// <summary>
    /// 選曲順序：符合遭遇標記的第一個變奏 → 遭遇指定曲（例如這次 Boss 的變奏）→ 地下設施曲庫（有可播放曲目時）→ 一般曲庫。
    /// 曲庫依輪替序號輪播；不可播放（缺檔）的曲目會被跳過，全部不可播放時回傳 null。
    /// Song order: the first variant whose encounter tag matches → the encounter's own song (such as this boss's
    /// variant) → the facility pool (when it has a playable song) → the general pool. Pools rotate by a counter; songs
    /// that cannot play (missing files) are skipped, and null means nothing can play.
    /// </summary>
    public static class DMSMusicSongChoice
    {
        public static T Pick<T>(DMSMusicSongTag tags, IReadOnlyList<KeyValuePair<DMSMusicSongTag, T>> variants, T encounterSong,
            IReadOnlyList<T> facilityPool, IReadOnlyList<T> pool, bool facility, int rotation, Predicate<T> playable) where T : class
        {
            if (variants != null)
            {
                for (int i = 0; i < variants.Count; i++)
                {
                    if ((tags & variants[i].Key) != 0 && playable(variants[i].Value))
                    {
                        return variants[i].Value;
                    }
                }
            }
            if (encounterSong != null && playable(encounterSong))
            {
                return encounterSong;
            }
            if (facility)
            {
                T fromFacility = FromPool(facilityPool, rotation, playable);
                if (fromFacility != null)
                {
                    return fromFacility;
                }
            }
            return FromPool(pool, rotation, playable);
        }

        private static T FromPool<T>(IReadOnlyList<T> pool, int rotation, Predicate<T> playable) where T : class
        {
            if (pool == null || pool.Count == 0)
            {
                return null;
            }
            int start = ((rotation % pool.Count) + pool.Count) % pool.Count;
            for (int i = 0; i < pool.Count; i++)
            {
                T song = pool[(start + i) % pool.Count];
                if (song != null && playable(song))
                {
                    return song;
                }
            }
            return null;
        }
    }

    /// <summary>不依賴遊戲的時間計算。Timing math with no game dependency.</summary>
    public static class DMSMusicTiming
    {
        /// <summary>
        /// 原版 <c>QuestPart_BossgroupArrives</c> 與 FFF <c>QuestPart_RaidWaveArrives</c> 的抵達時間：過了最短延遲後，
        /// 在第一個 hash 邊界（<c>(tick + hashOffset) % interval == 0</c>，與原版一樣以 int 溢位計算）且允許召喚時抵達，
        /// 最晚在最長延遲時強制抵達。不允許召喚時只能保證強制抵達的時間。
        ///
        /// When vanilla's <c>QuestPart_BossgroupArrives</c> and FFF's <c>QuestPart_RaidWaveArrives</c> arrive: after the
        /// minimum delay, on the first hash boundary (<c>(tick + hashOffset) % interval == 0</c>, wrapping like vanilla's
        /// int maths) when summoning is allowed, and forced at the maximum delay. When summoning is blocked only the
        /// forced time is certain.
        /// </summary>
        public static int TicksUntilArrival(int now, int enableTick, int minDelay, int maxDelay, int hashOffset, int interval, bool canSummonNow)
        {
            int forced = enableTick + maxDelay;
            int arrival = forced;
            if (canSummonNow)
            {
                int from = Math.Max(now, enableTick + minDelay);
                int phase = unchecked(from + hashOffset) % interval;
                int boundary = phase == 0 ? from : phase > 0 ? from + interval - phase : from - phase;
                arrival = Math.Min(boundary, forced);
            }
            return Math.Max(0, arrival - now);
        }
    }

    /// <summary>
    /// 遊戲端每次刷新從地圖讀到的原始輸入。讀取端負責語意（例如只回報交戰／搜索中守墓人的目擊 tick）。
    /// Raw inputs read from a map on each game-side refresh. Readers own the semantics (for example only engaging or
    /// searching gravekeeper squads report a sighting tick).
    /// </summary>
    public struct DMSMusicInputs
    {
        public DMSMusicContext context;
        /// <summary>地圖上有玩家控制的單位（含倒地者與機兵）。The map holds player-controlled units, downed ones and mechs included.</summary>
        public bool playerPresent;
        /// <summary>此地圖有已登記的 DMS 遭遇（Boss、追殺）。The map has a registered DMS encounter (boss, huntdown).</summary>
        public bool hasEncounter;
        public DMSLockdownPhase lockdown;
        public float alertLevel;
        public bool airspaceBlocked;
        public bool gravekeeperChallenge;
        public bool gravekeeperSearch;
        /// <summary>交戰或搜索中守墓人最後看見入侵者的 tick；沒有則為 <see cref="DMSMusicTuning.Never"/>。The last tick an engaging or searching squad saw an intruder.</summary>
        public int gravekeeperSightTick;
        /// <summary>最近一波增援的剩餘 tick；沒有倒數為 −1。Ticks until the next reinforcement wave; −1 when none is counting down.</summary>
        public int reinforcementTicksLeft;
        public int raidsSentCount;
        public int activeBosses;
        /// <summary>最近一個 Boss 遭遇的預計抵達剩餘 tick；不可信或沒有為 −1。Ticks until the nearest boss arrival; −1 when unknown or none.</summary>
        public int bossTicksUntilArrival;
        public DMSMusicSongTag songTags;

        public static DMSMusicInputs Empty => new DMSMusicInputs
        {
            gravekeeperSightTick = DMSMusicTuning.Never,
            reinforcementTicksLeft = -1,
            bossTicksUntilArrival = -1,
        };
    }

    /// <summary>
    /// 遊戲端刷新後的結論。所有 tick 窗口都已換算成旗標或編號，客戶端不需要（在 Multiplayer 下也不能）自己拿 TicksGame 相減。
    /// The game-side conclusion of a refresh. Every tick window is already turned into flags or ids, so the client never
    /// subtracts TicksGame itself (under Multiplayer it could not).
    /// </summary>
    public struct DMSMusicFacts
    {
        public DMSMusicContext context;
        public bool playerPresent;
        public bool hasEncounter;
        public DMSLockdownPhase lockdown;
        public int activeBosses;
        public DMSMusicReason reasons;
        public DMSMusicSongTag songTags;

        /// <summary>目前是否處於危機（交戰、Boss、封鎖倒數或受困）。Whether a crisis (combat, boss, lockdown countdown or trap) is under way.</summary>
        public bool crisisActive;
        /// <summary>進行中的危機編號；沒有危機時為最後一個。The running crisis id, or the last one when calm.</summary>
        public int crisisId;
        /// <summary>剛在餘波窗口內結束的危機編號；否則為 0。The crisis that ended inside the aftermath window, else 0.</summary>
        public int aftermathCrisisId;

        // 診斷用。For diagnostics.
        public float alertLevel;
        public int reinforcementTicksLeft;
        public int bossTicksUntilArrival;

        /// <summary>
        /// DMS 地圖、有登記遭遇的一般地圖，或遭遇引起的危機尚未結束（含餘波窗口）的地圖：Boss 倒下、任務結束後，
        /// 殖民地上的護衛交火與餘波仍由 DMS 接手。
        /// A DMS map, any map with a registered encounter, or a map whose encounter-born crisis has not run out (aftermath
        /// window included): after the boss falls and its quest ends, DMS still owns the escorts' fight and the aftermath.
        /// </summary>
        public bool Relevant => context != DMSMusicContext.None || hasEncounter || crisisActive || aftermathCrisisId > 0;

        public bool HasCombatReason => (reasons & DMSMusicReason.Combat) != 0;

        public bool HasAlertReason => (reasons & DMSMusicReason.Alert) != 0;

        public bool LockdownCountingDown => playerPresent && lockdown == DMSLockdownPhase.Countdown;

        /// <summary>
        /// 封鎖完成（Locked）或中控被毀（Sealed）且玩家仍在內。Disarmed、Lost 不算受困。
        /// Locked down (Locked) or with the controller destroyed (Sealed) while the player is still inside. Disarmed and Lost are not trapped.
        /// </summary>
        public bool Trapped => playerPresent && (lockdown == DMSLockdownPhase.Locked || lockdown == DMSLockdownPhase.Sealed);
    }

    /// <summary>
    /// 唯一的狀態選擇規則：同一份事實、同一份客戶端紀錄、同一時間點永遠得到同一個狀態，查詢不改變任何紀錄。
    /// The single state rule: the same facts, client record and time always give the same state, and asking changes nothing.
    /// </summary>
    public static class DMSMusicSelector
    {
        public static DMSMusicState Select(in DMSMusicFacts facts, DMSMusicClientMapState client, float now, DMSMusicYield yield)
        {
            if (yield != DMSMusicYield.None || !facts.playerPresent || !facts.Relevant)
            {
                return DMSMusicState.None;
            }
            if (facts.LockdownCountingDown)
            {
                return DMSMusicState.LockdownCountdown;
            }
            if (facts.activeBosses > 0)
            {
                return DMSMusicState.BossCombat;
            }
            if (client.CombatHeld(facts, now))
            {
                return DMSMusicState.InstallationCombat;
            }
            if (facts.Trapped)
            {
                return DMSMusicState.FacilityTrapped;
            }
            if (client.AlertHeld(facts, now))
            {
                return DMSMusicState.SecurityAlert;
            }
            if (client.AftermathPending(facts))
            {
                return DMSMusicState.Aftermath;
            }
            switch (facts.context)
            {
                case DMSMusicContext.Facility:
                    return DMSMusicState.FacilityExplore;
                case DMSMusicContext.Installation:
                    return DMSMusicState.InstallationExplore;
                default:
                    return DMSMusicState.None;
            }
        }
    }
}
