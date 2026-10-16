using System;
using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace DMS
{
    /// <summary>
    /// 情境音樂的遊戲端：每 60 tick 讀一次地圖狀態，交給 <see cref="DMSMusicGameState"/> 換算成 <see cref="DMSMusicFacts"/>，
    /// 並存下換算所需的 tick 紀錄。只在 tick 流程與讀檔初始化時修改，Multiplayer 下各端一致；客戶端只讀 <see cref="Facts"/>。
    ///
    /// The game side of situational music: every 60 ticks it reads the map, lets <see cref="DMSMusicGameState"/> turn
    /// that into <see cref="DMSMusicFacts"/>, and saves the tick records it needs. It only changes inside the tick flow
    /// and load initialisation, so every Multiplayer client agrees; clients only read <see cref="Facts"/>.
    ///
    /// 每張地圖都會自動掛上這個組件，存在不代表是 DMS 地圖：沒有情境也沒有登記遭遇的地圖，選擇結果一律交還原版。
    /// Every map gets this component automatically, so its presence does not make a map a DMS map: with no context and no
    /// registered encounter the selection always hands music back to vanilla.
    /// </summary>
    public class MapComponent_DMSMusicState : MapComponent
    {
        private const int ErrorOnceKey = 0x44_4D_53_4D; // "DMSM"

        private DMSMusicGameState state = new DMSMusicGameState();

        private DMSMusicFacts facts;

        // 登記為封存科技追殺的襲擊 lord（在 DMS 自己的發動點登記）；lord 解散後自動清掉。
        // Raid lords registered as Occultech huntdowns (at DMS's own firing points); dropped once the lord is gone.
        private List<Lord> huntdownLords = new List<Lord>();

        // 開火 hook 每發都要查組件；同一幀的射擊幾乎都在同一張地圖，單一快取就夠。
        // The firing hook looks the component up on every shot; shots in a frame are nearly always on one map.
        private static Map cachedMap;
        private static MapComponent_DMSMusicState cachedComp;

        public MapComponent_DMSMusicState(Map map) : base(map)
        {
        }

        /// <summary>最近一次刷新的結論。The conclusion of the latest refresh.</summary>
        public DMSMusicFacts Facts => facts;

        public DMSMusicGameState GameState => state;

        /// <summary>這張地圖是否記錄交火：DMS 地圖，或有登記遭遇的一般地圖。Whether this map records fire: a DMS map, or any map with a registered encounter.</summary>
        public bool RecordsCombat => facts.Relevant;

        /// <summary>啟動中 Boss 遭遇指定的曲目（召喚時選定），沒有則為 null；不存檔，每次刷新重算。The active boss encounter's song (picked at summoning), or null; recomputed on each refresh, not saved.</summary>
        public SongDef EncounterSong { get; private set; }

        public IReadOnlyList<Lord> HuntdownLords => huntdownLords;

        /// <summary>開發者注入的測試事件；平常為 null，不存檔。Developer-injected test events; normally null, never saved.</summary>
        public DMSMusicDebugInjection DebugInjection { get; set; }

        /// <summary>登記一支封存科技追殺襲擊；只能從 tick 流程呼叫。Registers an Occultech huntdown raid; call from the tick flow only.</summary>
        public void RegisterHuntdown(Lord lord)
        {
            if (lord != null && !huntdownLords.Contains(lord))
            {
                huntdownLords.Add(lord);
            }
        }

        public static MapComponent_DMSMusicState For(Map map)
        {
            return map?.GetComponent<MapComponent_DMSMusicState>();
        }

        public static MapComponent_DMSMusicState CachedFor(Map map)
        {
            if (map != cachedMap)
            {
                cachedMap = map;
                cachedComp = For(map);
            }
            return cachedComp;
        }

        public static void ClearCache()
        {
            cachedMap = null;
            cachedComp = null;
        }

        /// <summary>記錄一次敵我交火；只能從 tick 流程呼叫。Records hostile fire; call from the tick flow only.</summary>
        public void NoteCombat()
        {
            state.NoteCombat(Find.TickManager.TicksGame);
        }

        /// <summary>重新讀取地圖並更新結論；只能從 tick 流程或初始化呼叫。Re-reads the map and updates the conclusion; call from the tick flow or initialisation only.</summary>
        public void Refresh()
        {
            try
            {
                int now = Find.TickManager.TicksGame;
                PruneHuntdownLords();
                DMSMusicInputs inputs = DMSMusicEventBridge.ReadInputs(this, state.Tracking(now) || DebugInjection != null, out SongDef encounterSong);
                DebugInjection?.Apply(ref inputs, now);
                facts = state.Evaluate(inputs, now);
                EncounterSong = encounterSong;
            }
            catch (Exception e)
            {
                // 讀取來源出錯時保留上一次結論，每張地圖只記一次，不在每 60 tick 洗版。
                // On a reader failure keep the previous conclusion and log once per map instead of every 60 ticks.
                Log.ErrorOnce($"[DMS] Situational music refresh failed on map {map.uniqueID}: {e}", ErrorOnceKey ^ map.uniqueID);
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // 讀檔時遊戲通常是暫停的；先算一次，暫停中切到這張地圖也有正確結論。
            // Loads usually start paused; refresh once so the map has a conclusion even before the first tick.
            Refresh();
        }

        public override void MapComponentTick()
        {
            if (GenTicks.IsTickInterval(map.uniqueID, DMSMusicTuning.RefreshIntervalTicks))
            {
                Refresh();
            }
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            if (cachedMap == map)
            {
                ClearCache();
            }
            GameComponent_DMSMusic.Instance?.Forget(map);
        }

        private void PruneHuntdownLords()
        {
            huntdownLords.RemoveAll(lord => lord == null || !map.lordManager.lords.Contains(lord));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // 已解散的 lord 不在任何地方存檔，留著參照讀檔時會無法解析。A disbanded lord is saved nowhere, so its reference would not resolve on load.
                PruneHuntdownLords();
            }
            Scribe_Collections.Look(ref huntdownLords, "huntdownLords", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                huntdownLords ??= new List<Lord>();
                huntdownLords.RemoveAll(lord => lord == null);
            }
            Scribe_Values.Look(ref state.lastEvaluatedTick, "lastEvaluatedTick", DMSMusicTuning.Never);
            Scribe_Values.Look(ref state.lastAlertLevel, "lastAlertLevel");
            Scribe_Values.Look(ref state.lastAlertRiseTick, "lastAlertRiseTick", DMSMusicTuning.Never);
            Scribe_Values.Look(ref state.searchActive, "searchActive");
            Scribe_Values.Look(ref state.searchStartTick, "searchStartTick", DMSMusicTuning.Never);
            Scribe_Values.Look(ref state.lastRaidsSentCount, "lastRaidsSentCount");
            Scribe_Values.Look(ref state.lastReinforcementArrivalTick, "lastReinforcementArrivalTick", DMSMusicTuning.Never);
            Scribe_Values.Look(ref state.lastCombatTick, "lastCombatTick", DMSMusicTuning.Never);
            Scribe_Values.Look(ref state.lastActiveBosses, "lastActiveBosses");
            Scribe_Values.Look(ref state.crisisActive, "crisisActive");
            Scribe_Values.Look(ref state.crisisCounter, "crisisCounter");
            Scribe_Values.Look(ref state.lastCrisisEndTick, "lastCrisisEndTick", DMSMusicTuning.Never);
        }
    }
}
