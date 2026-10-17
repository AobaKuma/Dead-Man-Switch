using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 情境音樂的客戶端：每幀取觀看地圖的 <see cref="DMSMusicFacts"/>，累計未暫停的實際時間來推進聽感緩衝，
    /// 選出目前狀態供 transition worker 讀取。不存檔，也不寫回遊戲端，所以各客戶端可以各自觀看不同地圖。
    ///
    /// The client side of situational music: every frame it takes the viewed map's <see cref="DMSMusicFacts"/>, advances
    /// the listening holds on unpaused real time, and picks the current state for the transition workers to read.
    /// Nothing is saved or written back to the game side, so each client may watch a different map.
    ///
    /// <see cref="GameComponentUpdate"/> 在同一幀內早於 <c>MusicManagerPlay.MusicUpdate()</c> 執行，worker 讀到的總是本幀結果。
    /// 狀態或觀看地圖一改變、DMS 序列淡出完成、外部強制曲結束時，就主動呼叫 <c>CheckTransitions()</c>，不等原版每 60 tick
    /// 的檢查（暫停中也一樣）。只在幀流程呼叫：原版開始序列會用 <c>Verse.Rand</c> 洗牌，在 tick 內呼叫會讓 Multiplayer 不同步。
    ///
    /// <see cref="GameComponentUpdate"/> runs before <c>MusicManagerPlay.MusicUpdate()</c> in the same frame, so workers
    /// always read this frame's result. When the state or viewed map changes, a DMS sequence finishes fading, or an
    /// outside forced song ends, it calls <c>CheckTransitions()</c> right away instead of waiting for vanilla's 60-tick
    /// check (paused or not). Only ever from the frame flow: starting a sequence shuffles with <c>Verse.Rand</c>, and
    /// doing that inside a tick would desync Multiplayer.
    /// </summary>
    public class GameComponent_DMSMusic : GameComponent
    {
        // 載入地圖等長幀不應一次吃掉緩衝。A long frame such as a map load should not eat a hold in one go.
        private const float MaxFrameSeconds = 0.25f;

        // 原版以私有旗標記錄「目前這首是強制播放」；讀不到時退回比對 ForcePlaySong 記下的曲目。
        // Vanilla keeps "this song was forced" in a private flag; without it, compare with the song ForcePlaySong recorded.
        private static readonly AccessTools.FieldRef<MusicManagerPlay, bool> songWasForced = SongWasForcedRef();

        private readonly Dictionary<int, DMSMusicClientMapState> mapStates = new Dictionary<int, DMSMusicClientMapState>();

        private float unpausedTime;

        private SongDef forcedSong;
        private bool forcedLastFrame;

        private Map viewedMap;
        private MapComponent_DMSMusicState viewedComp;

        private DMSMusicState lastState;
        private Map lastStateMap;
        private bool fadingLastFrame;
        private bool checkPending;

        private static Game cachedGame;
        private static GameComponent_DMSMusic cached;

        public GameComponent_DMSMusic(Game game)
        {
            MapComponent_DMSMusicState.ClearCache();
        }

        /// <summary>目前遊戲的組件；不在遊戲中時為 null。This game's component; null outside a game.</summary>
        public static GameComponent_DMSMusic Instance
        {
            get
            {
                Game game = Current.Game;
                if (game != cachedGame)
                {
                    cachedGame = game;
                    cached = game?.GetComponent<GameComponent_DMSMusic>();
                }
                return cached;
            }
        }

        public DMSMusicState State { get; private set; }

        public DMSMusicYield Yield { get; private set; }

        /// <summary><see cref="State"/> 所屬的地圖；讓位時為 null。The map <see cref="State"/> belongs to; null while yielding.</summary>
        public Map StateMap { get; private set; }

        /// <summary><see cref="StateMap"/> 的結論；讓位時為預設值。<see cref="StateMap"/>'s facts; default while yielding.</summary>
        public DMSMusicFacts Facts { get; private set; }

        /// <summary><see cref="StateMap"/> 上啟動中 Boss 遭遇指定的曲目，沒有則為 null。The song of the active boss encounter on <see cref="StateMap"/>, or null.</summary>
        public SongDef EncounterSong { get; private set; }

        public float UnpausedTime => unpausedTime;

        /// <summary>
        /// 遊戲場景的音樂管理器；主選單場景（Root_Entry）也可能有 Game 在跑組件更新，那時為 null。
        /// <c>Find.MusicManagerPlay</c> 直接強制轉型 Root_Play，在那裡會丟例外。
        ///
        /// The play scene's music manager; null in the entry scene (Root_Entry), which can also hold a Game and run
        /// component updates. <c>Find.MusicManagerPlay</c> hard-casts to Root_Play and throws there.
        /// </summary>
        public static MusicManagerPlay PlayMusic => (Current.Root as Root_Play)?.musicManagerPlay;

        public override void GameComponentUpdate()
        {
            MusicManagerPlay music = PlayMusic;
            if (music == null)
            {
                return;
            }
            if (!Find.TickManager.Paused)
            {
                unpausedTime += Mathf.Min(Time.deltaTime, MaxFrameSeconds);
            }
            bool forced = ForcedSongPlaying(music);
            Refresh(music, forced);
            DriveTransitions(music, forced);
        }

        public override void GameComponentOnGUI()
        {
            if (DMSMusicDebugActions.ShowOverlay)
            {
                DMSMusicDebugActions.DrawOverlay(this);
            }
        }

        public DMSMusicClientMapState ClientStateFor(Map map)
        {
            if (!mapStates.TryGetValue(map.uniqueID, out DMSMusicClientMapState client))
            {
                client = new DMSMusicClientMapState();
                mapStates[map.uniqueID] = client;
            }
            return client;
        }

        public void Forget(Map map)
        {
            mapStates.Remove(map.uniqueID);
            if (viewedMap == map)
            {
                viewedMap = null;
                viewedComp = null;
            }
        }

        /// <summary>由 <c>ForcePlaySong</c> 的 postfix 呼叫。Called by the <c>ForcePlaySong</c> postfix.</summary>
        public void Notify_ForcedSong(SongDef song)
        {
            forcedSong = song;
            checkPending = true;
        }

        public bool ForcedSongPlaying(MusicManagerPlay music)
        {
            if (music == null || !music.IsPlaying)
            {
                return false;
            }
            if (songWasForced != null)
            {
                return songWasForced(music);
            }
            return forcedSong != null && music.CurrentSong == forcedSong;
        }

        private void Refresh(MusicManagerPlay music, bool forced)
        {
            Map map = Find.CurrentMap;
            // 關閉時讓位：正在播的 DMS 序列會由 MusicSequenceWorker_DMS.ShouldEnd() 立即淡出。
            // Disabled yields: a playing DMS sequence fades out at once via MusicSequenceWorker_DMS.ShouldEnd().
            Yield = DMS_Mod.Settings?.situationalMusic == false ? DMSMusicYield.Disabled
                : forced ? DMSMusicYield.ForcedSong
                : WorldRendererUtility.WorldSelected ? DMSMusicYield.WorldView
                : map == null ? DMSMusicYield.NoMap
                : DMSMusicYield.None;
            MapComponent_DMSMusicState comp = Yield == DMSMusicYield.None ? CompFor(map) : null;
            if (comp == null)
            {
                StateMap = null;
                State = DMSMusicState.None;
                Facts = default;
                EncounterSong = null;
                return;
            }

            DMSMusicFacts facts = comp.Facts;
            DMSMusicClientMapState client = ClientStateFor(map);
            client.Observe(facts, unpausedTime);

            // 餘波一開始播就算播過（原版沒有序列完成回呼）；之後 transition 不成立，但單曲會播完或播到時限。
            // The aftermath counts as played once it starts (vanilla has no sequence-finished callback); the transition
            // then fails, but the song still plays to its end or time limit.
            if (lastState == DMSMusicState.Aftermath && lastStateMap == map
                && music?.TriggeredTransition is MusicTransition_DMS triggered && triggered.State == DMSMusicState.Aftermath)
            {
                client.MarkAftermathPlayed(facts.aftermathCrisisId);
            }

            StateMap = map;
            Facts = facts;
            EncounterSong = comp.EncounterSong;
            State = DMSMusicSelector.Select(facts, client, unpausedTime, Yield);
        }

        // 每幀都要查觀看地圖的組件；GetComponent 是線性搜尋，換地圖時才重查。
        // The viewed map's component is needed every frame; GetComponent is a linear search, so only redo it on a map change.
        private MapComponent_DMSMusicState CompFor(Map map)
        {
            if (map != viewedMap)
            {
                viewedMap = map;
                viewedComp = MapComponent_DMSMusicState.For(map);
            }
            return viewedComp;
        }

        private void DriveTransitions(MusicManagerPlay music, bool forced)
        {
            if (music == null)
            {
                return;
            }
            bool fading = music.State == MusicManagerPlay.MusicManagerState.Fadeout;
            if (State != lastState || StateMap != lastStateMap)
            {
                checkPending = true;
                if (DMSMusicDebugActions.LogStateChanges)
                {
                    Log.Message($"[DMS] Music {lastState} → {State} (map {StateMap?.uniqueID.ToString() ?? "-"}, yield {Yield}, reasons {Facts.reasons}, bosses {Facts.activeBosses}, lockdown {Facts.lockdown})");
                }
            }
            if (State != DMSMusicState.None && ((fadingLastFrame && !fading) || (forcedLastFrame && !forced)))
            {
                checkPending = true;
            }
            // 淡出中原版不檢查 transition；強制曲期間檢查會讓 transition 被標為觸發卻不播放。等兩者結束再檢查。
            // Vanilla skips transitions while fading, and checking during a forced song marks a transition triggered
            // without playing it; wait for both to end.
            if (checkPending && !fading && !forced && !music.disabled)
            {
                music.CheckTransitions();
                checkPending = false;
            }

            lastState = State;
            lastStateMap = StateMap;
            fadingLastFrame = music.State == MusicManagerPlay.MusicManagerState.Fadeout;
            forcedLastFrame = forced;
        }

        private static AccessTools.FieldRef<MusicManagerPlay, bool> SongWasForcedRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<MusicManagerPlay, bool>("songWasForced");
            }
            catch (Exception e)
            {
                Log.Warning($"[DMS] MusicManagerPlay.songWasForced not found; forced songs are detected by title only. {e.Message}");
                return null;
            }
        }
    }
}
