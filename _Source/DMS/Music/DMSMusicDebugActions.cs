using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 情境音樂的開發者工具（開發者模式 → Debug actions →「DMS」）：
    /// <list type="bullet">
    /// <item>狀態面板：畫面左上角顯示觀看地圖的狀態、原因、播放中的曲目與緩衝，用來對照驗收矩陣。</item>
    /// <item>注入／清除測試事件：只疊加在音樂輸入上（<see cref="DMSMusicDebugInjection"/>），不改動真正的警戒、封鎖、任務、Boss
    /// 或守軍。這些動作從介面直接修改遊戲端紀錄，Multiplayer 下會不同步，只供單人測試。</item>
    /// <item>輸出狀態、記錄狀態變化、量測更新成本。狀態變化記錄預設關閉，平常不輸出任何日誌。</item>
    /// </list>
    /// Developer tools for situational music (Dev mode → Debug actions → "DMS"): the state overlay in the top-left corner;
    /// injecting and clearing test events, which only layer onto the music inputs (<see cref="DMSMusicDebugInjection"/>)
    /// and never touch the real alert, lockdown, quests, bosses or garrison — they write the game-side record straight
    /// from the UI, so they desync Multiplayer and are for single-player testing only; and logging the state, logging
    /// state changes, and measuring refresh cost. State-change logging is off by default, so nothing is logged normally.
    /// </summary>
    public static class DMSMusicDebugActions
    {
        private const float OverlayWidth = 380f;
        private const int ApproachTestTicks = 1800;
        private const int MeasureRuns = 200;

        private static readonly Color OverlayBackground = new Color(0f, 0f, 0f, 0.65f);

        private static readonly StringBuilder text = new StringBuilder();

        public static bool ShowOverlay { get; private set; }

        public static bool LogStateChanges { get; private set; }

        [DebugAction("DMS", "Music: 狀態面板 / Toggle state overlay",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void ToggleOverlay()
        {
            ShowOverlay = !ShowOverlay;
        }

        [DebugAction("DMS", "Music: 注入測試事件 / Inject test event",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void InjectTestEvent()
        {
            MapComponent_DMSMusicState comp = MapComponent_DMSMusicState.For(Find.CurrentMap);
            if (comp == null)
            {
                return;
            }
            List<DebugMenuOption> options = new List<DebugMenuOption>
            {
                Option(comp, "交戰：開火一次 / Combat: one shot", () => comp.NoteCombat()),
                Option(comp, "守墓人接敵 / Gravekeeper contact", () => Injection(comp).sightTick = Now),
                Option(comp, "警戒上升 / Alert rise", () => comp.GameState.NoteAlertRise(Now)),
                Option(comp, "守墓人盤查（切換）/ Toggle gravekeeper challenge", () => Injection(comp).challenge ^= true),
                Option(comp, "守墓人搜索（切換）/ Toggle gravekeeper sweep", () => Injection(comp).sweep ^= true),
                Option(comp, $"增援 {ApproachTestTicks} tick 後抵達 / Reinforcements in {ApproachTestTicks} ticks",
                    () => Injection(comp).reinforcementArrivalTick = Now + ApproachTestTicks),
                Option(comp, $"Boss {ApproachTestTicks} tick 後抵達 / Boss in {ApproachTestTicks} ticks",
                    () => Injection(comp).bossArrivalTick = Now + ApproachTestTicks),
                Option(comp, "Boss +1（立即啟動）/ Boss +1 (active now)", () => Injection(comp).bosses++),
                Option(comp, "Boss −1（只減注入的）/ Boss −1 (injected only)",
                    () => Injection(comp).bosses = Math.Max(0, Injection(comp).bosses - 1)),
                Option(comp, "封鎖：不覆寫 / Lockdown: no override", () => Injection(comp).lockdown = null),
            };
            foreach (DMSLockdownPhase phase in Enum.GetValues(typeof(DMSLockdownPhase)))
            {
                DMSLockdownPhase local = phase;
                options.Add(Option(comp, $"封鎖 / Lockdown: {local}", () => Injection(comp).lockdown = local));
            }
            options.Add(Option(comp, "情境：不覆寫 / Context: no override", () => Injection(comp).context = null));
            options.Add(Option(comp, "情境 / Context: Installation", () => Injection(comp).context = DMSMusicContext.Installation));
            options.Add(Option(comp, "情境 / Context: Facility", () => Injection(comp).context = DMSMusicContext.Facility));
            foreach (DMSMusicSongTag tag in new[] { DMSMusicSongTag.Huntdown, DMSMusicSongTag.AlliedEngaged, DMSMusicSongTag.MainBattle })
            {
                DMSMusicSongTag local = tag;
                options.Add(Option(comp, $"選曲標記（切換）/ Toggle song tag: {local}", () => Injection(comp).tags ^= local));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("DMS", "Music: 清除測試事件 / Clear test events",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ClearTestEvents()
        {
            Map map = Find.CurrentMap;
            MapComponent_DMSMusicState comp = MapComponent_DMSMusicState.For(map);
            if (comp == null)
            {
                return;
            }
            // 連同真實事件的窗口與客戶端緩衝一起清掉，讓地圖立刻回到基準。Real event windows and client holds go too, so the map is back at its baseline at once.
            comp.DebugInjection = null;
            comp.GameState.ClearWindows();
            GameComponent_DMSMusic.Instance?.ClientStateFor(map).ClearHolds();
            comp.Refresh();
        }

        [DebugAction("DMS", "Music: 輸出狀態 / Log state",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void LogState()
        {
            GameComponent_DMSMusic music = GameComponent_DMSMusic.Instance;
            if (music != null)
            {
                Log.Message("[DMS] Situational music\n" + Describe(music));
            }
        }

        [DebugAction("DMS", "Music: 記錄狀態變化 / Toggle state-change log",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void ToggleStateChangeLog()
        {
            LogStateChanges = !LogStateChanges;
            Messages.Message($"[DMS] Music state-change log {(LogStateChanges ? "on" : "off")}.", MessageTypeDefOf.NeutralEvent, historical: false);
        }

        /// <summary>
        /// 量測每張地圖一次刷新（每 60 tick 一次）的耗時。同一 tick 內重複刷新結果不變，所以量測不會改動狀態。
        /// Measures one refresh (run every 60 ticks) per map. Refreshing again within the same tick changes nothing, so
        /// measuring does not disturb the state.
        /// </summary>
        [DebugAction("DMS", "Music: 量測更新成本 / Measure refresh cost",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void MeasureRefreshCost()
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine($"[DMS] Situational music refresh cost ({MeasureRuns} runs per map; quests {Find.QuestManager.QuestsListForReading.Count})");
            double totalPerTick = 0d;
            foreach (Map map in Find.Maps)
            {
                MapComponent_DMSMusicState comp = MapComponent_DMSMusicState.For(map);
                if (comp == null)
                {
                    continue;
                }
                comp.Refresh();
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < MeasureRuns; i++)
                {
                    comp.Refresh();
                }
                watch.Stop();
                double perRefresh = watch.Elapsed.TotalMilliseconds * 1000d / MeasureRuns;
                double perTick = perRefresh / DMSMusicTuning.RefreshIntervalTicks;
                totalPerTick += perTick;
                report.AppendLine($"  map {map.uniqueID} ({map.Parent?.LabelCap ?? "?"}): {perRefresh:F1} µs per refresh, {perTick:F2} µs per tick; "
                    + $"relevant {comp.Facts.Relevant}, lords {map.lordManager.lords.Count}, pawns {map.mapPawns.AllPawnsSpawnedCount}");
            }
            report.Append($"  total {totalPerTick:F2} µs per tick");
            Log.Message(report.ToString());
        }

        public static void DrawOverlay(GameComponent_DMSMusic music)
        {
            string body = Describe(music);
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float height = Text.CalcHeight(body, OverlayWidth - 12f);
            Rect rect = new Rect(10f, 80f, OverlayWidth, height + 10f);
            Widgets.DrawBoxSolid(rect, OverlayBackground);
            Widgets.Label(rect.ContractedBy(5f), body);
            Text.Font = oldFont;
        }

        private static int Now => Find.TickManager.TicksGame;

        private static DMSMusicDebugInjection Injection(MapComponent_DMSMusicState comp)
        {
            return comp.DebugInjection ??= new DMSMusicDebugInjection();
        }

        private static DebugMenuOption Option(MapComponent_DMSMusicState comp, string label, Action action)
        {
            return new DebugMenuOption(label, DebugMenuOptionMode.Action, delegate
            {
                action();
                comp.Refresh();
            });
        }

        private static string Describe(GameComponent_DMSMusic music)
        {
            text.Clear();
            Map map = Find.CurrentMap;
            text.AppendLine($"DMS music: {music.State}  (yield {music.Yield})");
            MusicManagerPlay manager = Find.MusicManagerPlay;
            if (manager != null)
            {
                MusicSequenceDef sequence = manager.TriggeredTransition?.def?.sequence;
                bool placeholder = sequence?.GetModExtension<ModExtension_DMSMusicSequence>()?.placeholder == true;
                float songTime = manager.IsPlaying ? manager.SongTime : 0f;
                text.AppendLine($"playing {manager.CurrentSong?.defName ?? "-"} ({songTime:F0}s), transition {manager.TriggeredTransition?.def?.defName ?? "-"}"
                    + $"{(placeholder ? " [PLACEHOLDER]" : "")}, manager {manager.State}, forced {music.ForcedSongPlaying(manager)}");
            }
            if (map == null)
            {
                return text.ToString().TrimEnd();
            }

            MapComponent_DMSMusicState comp = MapComponent_DMSMusicState.For(map);
            if (comp == null)
            {
                text.Append("no music component on this map");
                return text.ToString();
            }

            DMSMusicFacts facts = comp.Facts;
            DMSMusicGameState game = comp.GameState;
            DMSMusicClientMapState client = music.ClientStateFor(map);
            float now = music.UnpausedTime;
            int tick = Find.TickManager.TicksGame;

            text.AppendLine($"map {map.uniqueID} ({map.Parent?.LabelCap ?? "?"}), generator {map.generatorDef?.defName ?? "-"}");
            text.AppendLine($"context {facts.context}, player {facts.playerPresent}, encounter {facts.hasEncounter}, relevant {facts.Relevant}");
            text.AppendLine($"reasons {facts.reasons}");
            text.AppendLine($"lockdown {facts.lockdown}, bosses {facts.activeBosses}, alert {facts.alertLevel:F1}, tags {facts.songTags}, boss song {comp.EncounterSong?.defName ?? "-"}");
            text.AppendLine($"huntdown lords {comp.HuntdownLords.Count}");
            text.AppendLine($"reinforcements in {facts.reinforcementTicksLeft}, boss in {facts.bossTicksUntilArrival}");
            text.AppendLine($"last combat {Ago(game.lastCombatTick, tick)}, alert rise {Ago(game.lastAlertRiseTick, tick)}, sweep start {Ago(game.searchStartTick, tick)}");
            text.AppendLine($"crisis #{facts.crisisId} active {facts.crisisActive}, aftermath #{facts.aftermathCrisisId}");
            text.Append($"holds combat {Left(client.combatHoldUntil, now)}, alert {Left(client.alertHoldUntil, now)}; watched #{client.lastObservedCrisisId}, played #{client.lastPlayedAftermathId}");
            DMSMusicDebugInjection injection = comp.DebugInjection;
            if (injection != null)
            {
                text.Append($"\nINJECTED context {injection.context?.ToString() ?? "-"}, lockdown {injection.lockdown?.ToString() ?? "-"}, bosses {injection.bosses}, "
                    + $"challenge {injection.challenge}, sweep {injection.sweep}, tags {injection.tags}");
            }
            return text.ToString();
        }

        private static string Ago(int eventTick, int now)
        {
            return eventTick == DMSMusicTuning.Never ? "never" : $"{now - eventTick}t ago";
        }

        private static string Left(float until, float now)
        {
            return until > now ? $"{until - now:F1}s" : "-";
        }
    }
}
