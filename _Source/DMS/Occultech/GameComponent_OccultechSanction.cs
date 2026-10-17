using System.Collections.Generic;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 封存科技制裁的存檔狀態與長期行為。
    ///
    /// 保存三件無法從當下盤面推導的事：
    /// 1. <c>sanctionedProjects</c>：哪些研究已經結算過制裁，避免讀檔或重複 FinishProject 時再扣一次。
    /// 2. <c>permanentHostile</c>：隱匿級觸發的永久敵對狀態（解除條件只有軍事法庭）。
    /// 3. <c>collateralFactions</c>：隱匿級連坐敵對的艦隊友好派系，僅供信件與除錯顯示。
    ///
    /// 並負責派系關係的兜底校正（Harmony patch 是即時攔截，這裡是每隔一段時間的保險，兩者都失效才會出現不一致）。
    ///
    /// 追殺本身（排程、SAGE 暫停、休戰順延、伴隨任務）交給 Fortified Huntdown（DMS_OccultechHunt）；
    /// 這裡保留原本的 API，內部轉給 <see cref="HuntdownUtility"/>，舊存檔在讀取時轉換。
    /// The hunt itself (scheduling, SAGE suspension, truce postponement, quest offers) runs on Fortified Huntdown
    /// (DMS_OccultechHunt); the API here is kept and forwards to HuntdownUtility, and old saves convert on load.
    /// </summary>
    public class GameComponent_OccultechSanction : GameComponent
    {
        // 關係校正頻率。派系關係不需要即時到每 tick，2000 ticks（約 33 秒遊戲時間）足夠。
        private const int EnforceIntervalTicks = 2000;

        private List<ResearchProjectDef> sanctionedProjects = new List<ResearchProjectDef>();
        private HashSet<ResearchProjectDef> sanctionedCache;

        private bool permanentHostile;
        private List<Faction> collateralFactions = new List<Faction>();

        // 舊存檔的追殺排程，只在讀取時使用，轉換後不再存檔。
        // The hunt schedule from saves made before the Huntdown migration; read once, never saved again.
        private int legacyNextHuntTick = -1;
        private int legacyHuntSuspendedUntilTick = -1;

        private static Game cachedGame;
        private static GameComponent_OccultechSanction cached;

        public GameComponent_OccultechSanction(Game game) { }

        /// <summary>取得目前遊戲的組件實例；尚未進入遊戲時回傳 null（不拋例外）。</summary>
        public static GameComponent_OccultechSanction CompSafe
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
                    cached = game.GetComponent<GameComponent_OccultechSanction>();
                }
                return cached;
            }
        }

        /// <summary>隱匿級永久敵對是否生效中。</summary>
        public bool PermanentHostile => permanentHostile;

        /// <summary>追殺令的 HuntdownDef（隱匿級制裁設定上的 huntdown）。The kill order's HuntdownDef.</summary>
        public static HuntdownDef HuntDef => OccultechSanctionUtility.OccultedSanction?.huntdown;

        /// <summary>追殺是否正被暫停（SAGE 被摧毀）。Whether the hunt is currently suspended (a SAGE was destroyed).</summary>
        public bool HuntSuspended => HuntDef != null && HuntdownUtility.IsSuspended(HuntDef);

        /// <summary>距離追殺恢復還有幾 tick；未暫停為 0。Ticks until the hunt resumes; 0 when not suspended.</summary>
        public int HuntResumeTicksLeft => HuntDef == null ? 0 : HuntdownUtility.SuspendedTicksLeft(HuntDef);

        /// <summary>
        /// 暫停追殺；已在暫停中時疊加在目前的結束時間之後。回傳暫停後的剩餘 ticks。
        /// Suspends the hunt; while already suspended, stacks onto the current end. Returns the ticks left.
        /// </summary>
        public int SuspendHunt(int ticks)
        {
            return HuntDef == null ? 0 : HuntdownUtility.Suspend(HuntDef, ticks);
        }

        /// <summary>除錯用：讓暫停在下一次檢查時結束。Debug: end the suspension at the next check.</summary>
        public void EndHuntSuspensionNow()
        {
            if (HuntDef != null) HuntdownUtility.ResumeNow(HuntDef);
        }

        /// <summary>連坐敵對的派系（唯讀；僅供顯示）。</summary>
        public List<Faction> CollateralFactions => collateralFactions;

        private HashSet<ResearchProjectDef> SanctionedCache
        {
            get
            {
                if (sanctionedCache == null)
                {
                    sanctionedCache = new HashSet<ResearchProjectDef>();
                    if (sanctionedProjects != null)
                    {
                        for (int i = 0; i < sanctionedProjects.Count; i++)
                        {
                            if (sanctionedProjects[i] != null)
                            {
                                sanctionedCache.Add(sanctionedProjects[i]);
                            }
                        }
                    }
                }
                return sanctionedCache;
            }
        }

        /// <summary>此專案是否已經結算過制裁。</summary>
        public bool IsSanctioned(ResearchProjectDef proj)
        {
            return proj != null && SanctionedCache.Contains(proj);
        }

        /// <summary>標記此專案已結算。回傳 false 表示先前就標記過。</summary>
        public bool MarkSanctioned(ResearchProjectDef proj)
        {
            if (proj == null || !SanctionedCache.Add(proj))
            {
                return false;
            }
            if (sanctionedProjects == null)
            {
                sanctionedProjects = new List<ResearchProjectDef>();
            }
            sanctionedProjects.Add(proj);
            return true;
        }

        /// <summary>
        /// 取消某專案的制裁紀錄（放棄技術時呼叫）。之後重新研究會再次觸發制裁，
        /// 這是刻意的：重新解封就是重新犯一次。
        /// </summary>
        public void ClearSanctioned(ResearchProjectDef proj)
        {
            if (proj == null)
            {
                return;
            }
            SanctionedCache.Remove(proj);
            sanctionedProjects?.Remove(proj);
        }

        /// <summary>
        /// 啟動永久敵對與追殺令。追殺已在進行（含暫停中）時維持原狀；第一次追殺落在任一玩家據點、4~7 天後。
        /// Begins permanent hostility and the kill order. A running (or suspended) hunt is left as is; the first hunt
        /// lands on a player home after the def's raid delay.
        /// </summary>
        public void BeginPermanentHostility()
        {
            permanentHostile = true;
            if (HuntDef != null) HuntdownUtility.Start(HuntDef, source: "Occultech");
        }

        /// <summary>解除永久敵對（軍事法庭服刑期滿）。連坐派系的關係不會一併恢復。</summary>
        public void EndPermanentHostility()
        {
            permanentHostile = false;
            if (HuntDef != null) HuntdownUtility.Stop(HuntDef);
        }

        public void AddCollateralFaction(Faction faction)
        {
            if (faction == null)
            {
                return;
            }
            if (collateralFactions == null)
            {
                collateralFactions = new List<Faction>();
            }
            if (!collateralFactions.Contains(faction))
            {
                collateralFactions.Add(faction);
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int now = Find.TickManager.TicksGame;

            if (now % EnforceIntervalTicks == 0)
            {
                OccultechSanctionUtility.EnforceFleetRelation();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref sanctionedProjects, "dms_occultechSanctioned", LookMode.Def);
            Scribe_Collections.Look(ref collateralFactions, "dms_occultechCollateral", LookMode.Reference);
            Scribe_Values.Look(ref permanentHostile, "dms_occultechPermanentHostile");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // 只讀不寫：Huntdown 遷移前的存檔。Read-only: saves from before the Huntdown migration.
                Scribe_Values.Look(ref legacyNextHuntTick, "dms_occultechNextHuntTick", -1);
                Scribe_Values.Look(ref legacyHuntSuspendedUntilTick, "dms_occultechHuntSuspendedUntil", -1);
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sanctionedProjects ??= new List<ResearchProjectDef>();
                collateralFactions ??= new List<Faction>();
                sanctionedProjects.RemoveAll(p => p == null);
                collateralFactions.RemoveAll(f => f == null);
                sanctionedCache = null;
            }
        }

        /// <summary>
        /// 讀檔後讓追殺令與永久敵對狀態一致，並轉換舊存檔的排程（下一次追殺、SAGE 暫停）。
        /// After loading, keep the kill order in step with permanent hostility and convert an old save's schedule.
        /// </summary>
        public override void LoadedGame()
        {
            base.LoadedGame();
            HuntdownDef def = HuntDef;
            if (def == null) return;

            if (!permanentHostile)
            {
                if (HuntdownUtility.IsActive(def)) HuntdownUtility.Stop(def);
            }
            else if (!HuntdownUtility.IsActive(def))
            {
                int now = Find.TickManager.TicksGame;
                HuntdownUtility.Start(def, source: "Occultech");
                if (legacyHuntSuspendedUntilTick > now)
                {
                    HuntdownUtility.Suspend(def, legacyHuntSuspendedUntilTick - now);
                }
                else if (legacyNextHuntTick > now && Find.AnyPlayerHomeMap is Map home && HuntdownUtility.TrackMap(def, home))
                {
                    // 沿用舊存檔排好的下一次追殺時間。Keep the next hunt the old save had scheduled.
                    HuntdownUtility.Delay(def, home, legacyNextHuntTick - now - HuntdownUtility.TicksUntilNextWave(def, home));
                }
            }
            legacyNextHuntTick = -1;
            legacyHuntSuspendedUntilTick = -1;
        }
    }
}
