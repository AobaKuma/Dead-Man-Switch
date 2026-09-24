using System.Collections.Generic;
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
    /// 並負責兩件持續性工作：永久敵對期間的追殺襲擊排程，以及派系關係的兜底校正
    /// （Harmony patch 是即時攔截，這裡是每隔一段時間的保險，兩者都失效才會出現不一致）。
    /// </summary>
    public class GameComponent_OccultechSanction : GameComponent
    {
        // 關係校正頻率。派系關係不需要即時到每 tick，2000 ticks（約 33 秒遊戲時間）足夠。
        private const int EnforceIntervalTicks = 2000;

        private List<ResearchProjectDef> sanctionedProjects = new List<ResearchProjectDef>();
        private HashSet<ResearchProjectDef> sanctionedCache;

        private bool permanentHostile;
        private List<Faction> collateralFactions = new List<Faction>();
        private int nextHuntTick = -1;

        // 追殺暫停（摧毀 SAGE）：到這個 tick 之前不追殺；-1 = 未暫停。
        // Hunt suspension (SAGE destroyed): no hunting before this tick; -1 = not suspended.
        private int huntSuspendedUntilTick = -1;

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

        /// <summary>追殺是否正被暫停（SAGE 被摧毀）。Whether the hunt is currently suspended (a SAGE was destroyed).</summary>
        public bool HuntSuspended => huntSuspendedUntilTick > Find.TickManager.TicksGame;

        /// <summary>距離追殺恢復還有幾 tick；未暫停為 0。Ticks until the hunt resumes; 0 when not suspended.</summary>
        public int HuntResumeTicksLeft => HuntSuspended ? huntSuspendedUntilTick - Find.TickManager.TicksGame : 0;

        /// <summary>
        /// 暫停追殺；已在暫停中時疊加在目前的結束時間之後。回傳暫停後的剩餘 ticks。
        /// Suspends the hunt; while already suspended, stacks onto the current end. Returns the ticks left.
        /// </summary>
        public int SuspendHunt(int ticks)
        {
            int now = Find.TickManager.TicksGame;
            huntSuspendedUntilTick = System.Math.Max(now, huntSuspendedUntilTick) + System.Math.Max(0, ticks);
            nextHuntTick = -1;
            return HuntResumeTicksLeft;
        }

        /// <summary>除錯用：讓暫停在下一 tick 結束。Debug: end the suspension on the next tick.</summary>
        public void EndHuntSuspensionNow()
        {
            if (huntSuspendedUntilTick > 0) huntSuspendedUntilTick = Find.TickManager.TicksGame;
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

        /// <summary>啟動永久敵對並排定第一次追殺。</summary>
        public void BeginPermanentHostility(FloatRange huntIntervalDays)
        {
            permanentHostile = true;
            // 暫停中完成新的隱匿級研究：暫停不受影響，恢復時才排程追殺。
            // A new Occulted project during a suspension leaves it alone; the hunt is scheduled on resume.
            if (HuntSuspended) return;
            ScheduleNextHunt(huntIntervalDays);
        }

        /// <summary>解除永久敵對（軍事法庭服刑期滿）。連坐派系的關係不會一併恢復。</summary>
        public void EndPermanentHostility()
        {
            permanentHostile = false;
            nextHuntTick = -1;
            huntSuspendedUntilTick = -1;
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

        private void ScheduleNextHunt(FloatRange intervalDays)
        {
            float days = intervalDays.RandomInRange;
            if (days <= 0f)
            {
                days = 5f;
            }
            nextHuntTick = Find.TickManager.TicksGame + (int)(days * GenDate.TicksPerDay);
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int now = Find.TickManager.TicksGame;

            // 暫停結束：SAGE 網路內另一台主機升格，重新排程追殺並補一張軍事法庭傳票
            // （暫停期間沒有追殺，也就沒有人補發傳票）。
            // Suspension over: another SAGE host takes over. Reschedule the hunt and re-offer the court martial
            // (nothing re-offers it while the hunt is paused).
            if (huntSuspendedUntilTick > 0 && now >= huntSuspendedUntilTick)
            {
                huntSuspendedUntilTick = -1;
                if (permanentHostile)
                {
                    ModExtension_OccultechSanction ext = OccultechSanctionUtility.OccultedSanction;
                    ScheduleNextHunt(ext?.huntIntervalDays ?? new FloatRange(4f, 7f));
                    OccultechSanctionUtility.SendHuntResumedLetter();
                    OccultechSanctionUtility.EnsureCourtMartialOffered();
                }
            }

            // 追殺：永久敵對期間週期性派遣艦隊襲擊，同時確保玩家手上有一份可接的軍事法庭傳票。
            // 那是解除永久敵對唯一的出路，不能只靠說書人隨機抽中。
            if (permanentHostile && !HuntSuspended && nextHuntTick > 0 && now >= nextHuntTick)
            {
                ModExtension_OccultechSanction ext = OccultechSanctionUtility.OccultedSanction;
                OccultechSanctionUtility.TryFireFleetRaid(
                    ext?.huntPointsFactor ?? 1.25f,
                    ext?.minRaidPoints ?? 300f);
                OccultechSanctionUtility.EnsureCourtMartialOffered();
                OccultechSanctionUtility.TryOfferNetworkSite();
                ScheduleNextHunt(ext?.huntIntervalDays ?? new FloatRange(4f, 7f));
            }

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
            Scribe_Values.Look(ref nextHuntTick, "dms_occultechNextHuntTick", -1);
            Scribe_Values.Look(ref huntSuspendedUntilTick, "dms_occultechHuntSuspendedUntil", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sanctionedProjects ??= new List<ResearchProjectDef>();
                collateralFactions ??= new List<Faction>();
                sanctionedProjects.RemoveAll(p => p == null);
                collateralFactions.RemoveAll(f => f == null);
                sanctionedCache = null;
            }
        }
    }
}
