using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    public enum DeviantGrantResult
    {
        Granted,
        /// <summary>前置未完成：已探明，前置完成後自動解譯。Prerequisites unmet: revealed, decoded once they are done.</summary>
        Pending,
        AlreadyHeld,
    }

    /// <summary>
    /// 脫序者交付的封存檔案。
    ///
    /// 不用 FFF 的 KnowledgeUtility.AddProgressTo：前置未完成時它照樣把進度寫滿，而 IsFinished 只看進度，
    /// 專案會在沒有完成信件、沒有制裁、沒有任務線的情況下靜默完成；Anomaly 下它走 ApplyKnowledge，會彈對話框且不發研究信件。
    /// 也不能對前置未完成的專案呼叫 FinishProject：原版會遞迴完成所有前置，連帶送出整段常規研究。
    /// 所以前置未完成的專案只探明、不寫進度，等前置完成時才在這裡完成。
    ///
    /// The sealed files the deviant hands over.
    /// Not KnowledgeUtility.AddProgressTo: with prerequisites unmet it still fills the progress, and IsFinished only
    /// reads progress, so the project would finish silently with no letter, sanction or quest line; under Anomaly it
    /// goes through ApplyKnowledge, which pops a dialog and sends no research letter. Nor FinishProject on a project
    /// with unmet prerequisites: vanilla finishes every prerequisite recursively, handing out whole stretches of the
    /// regular tree. So such projects are only revealed, and finished here once their prerequisites are done.
    /// </summary>
    public static class DeviantTechUtility
    {
        private static bool checkingPending;

        public static DeviantGrantResult Grant(ResearchProjectDef proj)
        {
            if (proj.IsFinished)
            {
                return DeviantGrantResult.AlreadyHeld;
            }
            // 任務自己負責後果（敵對＋追殺），通用制裁不再結算。The quest owns the consequences; skip the generic sanction.
            GameComponent_OccultechSanction.CompSafe?.MarkSanctioned(proj);
            ResearchDiscoveryUtility.ForceDiscover(proj, suppressLetter: true);
            if (!proj.PrerequisitesCompleted)
            {
                GameComponent_DeviantLegacy.CompSafe?.AddPending(proj);
                return DeviantGrantResult.Pending;
            }
            Complete(proj);
            return DeviantGrantResult.Granted;
        }

        /// <summary>
        /// 具名封存匣解封：探明後直接完成；前置未完成則比照脫序者交付，先登記待解譯，前置補齊時自動完成。
        /// 與 <see cref="Grant"/> 只差一件事：不呼叫 MarkSanctioned——封存匣不是任務，制裁照常由
        /// FinishProject 的 postfix 結算。
        /// Unsealing a casket: reveal, then finish. Unmet prerequisites hold the file pending exactly as a deviant
        /// handover does. The one difference from Grant() is that the generic sanction is not skipped: a casket is
        /// not a quest, so the ordinary consequences apply.
        /// </summary>
        public static DeviantGrantResult GrantFromCasket(ResearchProjectDef proj)
        {
            if (proj == null || proj.IsFinished)
            {
                return DeviantGrantResult.AlreadyHeld;
            }
            ResearchDiscoveryUtility.ForceDiscover(proj, suppressLetter: true);
            if (!proj.PrerequisitesCompleted)
            {
                GameComponent_DeviantLegacy.CompSafe?.AddPendingCasket(proj);
                return DeviantGrantResult.Pending;
            }
            Complete(proj);
            return DeviantGrantResult.Granted;
        }

        /// <summary>
        /// 完成一個前置已齊的專案。無 Anomaly 時原版 FinishProject 不寫知識型專案的進度，必須先寫進 FFF 的知識存放區，
        /// 制裁與任務線的 postfix 才讀得到 IsFinished。
        /// Finishes a project whose prerequisites are done. Without Anomaly vanilla FinishProject writes no progress for a
        /// knowledge project, so FFF's knowledge store is filled first for the postfixes to see IsFinished.
        /// </summary>
        public static void Complete(ResearchProjectDef proj)
        {
            if (!ModsConfig.AnomalyActive && proj.baseCost <= 0f && proj.knowledgeCost > 0f)
            {
                GameComponent_KnowledgeStore.CompSafe?.SetStored(proj, proj.knowledgeCost);
            }
            Find.ResearchManager.FinishProject(proj, doCompletionDialog: false, researcher: null, doCompletionLetter: true);
        }

        /// <summary>
        /// 從某知識類別挑出未完成、未待授予的專案：先取前置已完成者，不足再從其餘補。
        /// Picks unfinished, not-pending projects of a knowledge category: ready ones first, then the rest.
        /// </summary>
        public static List<ResearchProjectDef> PickRandom(KnowledgeCategoryDef category, int count, ICollection<ResearchProjectDef> exclude)
        {
            List<ResearchProjectDef> result = new List<ResearchProjectDef>();
            if (category == null || count <= 0)
            {
                return result;
            }
            GameComponent_DeviantLegacy legacy = GameComponent_DeviantLegacy.CompSafe;
            List<ResearchProjectDef> candidates = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => p.knowledgeCategory == category && !p.IsFinished
                            && (exclude == null || !exclude.Contains(p))
                            && (legacy == null || !legacy.IsPending(p)))
                .ToList();
            result.AddRange(candidates.Where(p => p.PrerequisitesCompleted).InRandomOrder().Take(count));
            if (result.Count < count)
            {
                result.AddRange(candidates.Where(p => !p.PrerequisitesCompleted).InRandomOrder().Take(count - result.Count));
            }
            return result;
        }

        /// <summary>尚未完成的前置（含隱藏前置）名稱。Labels of the prerequisites still unfinished, hidden ones included.</summary>
        public static string MissingPrerequisites(ResearchProjectDef proj)
        {
            IEnumerable<ResearchProjectDef> all = (proj.prerequisites ?? Enumerable.Empty<ResearchProjectDef>())
                .Concat(proj.hiddenPrerequisites ?? Enumerable.Empty<ResearchProjectDef>());
            return all.Where(p => p != null && !p.IsFinished)
                .Select(p => p.LabelCap.ToString())
                .Distinct()
                .ToCommaList(useAnd: true);
        }

        /// <summary>任一研究完成時（Patch_ResearchManager_FinishProject）。Called whenever any research finishes.</summary>
        public static void Notify_ProjectFinished(ResearchProjectDef proj)
        {
            CheckPending();
        }

        /// <summary>
        /// 解譯前置已齊的待授予專案。完成一項可能讓另一項的前置也齊了，所以重複掃描到沒有進展為止；
        /// 巢狀的 FinishProject 會再進 postfix，靠重入旗標擋下。兩份清單（脫序者交付／具名封存匣）走同一段
        /// 邏輯，只有完成信件的措辭不同。
        /// Decodes pending projects whose prerequisites are done, rescanning until nothing changes since one may unlock
        /// another; the nested FinishProject re-enters the postfix and is stopped by the reentrancy flag. Both lists
        /// (deviant handovers, casket unsealings) share this loop and differ only in the letter wording.
        /// </summary>
        public static void CheckPending()
        {
            GameComponent_DeviantLegacy legacy = GameComponent_DeviantLegacy.CompSafe;
            if (legacy == null || !legacy.AnyPending || checkingPending)
            {
                return;
            }
            if (Current.ProgramState != ProgramState.Playing)
            {
                return;
            }
            checkingPending = true;
            try
            {
                bool progressed = true;
                while (progressed)
                {
                    progressed = false;
                    foreach (bool fromCasket in new[] { false, true })
                    {
                        List<ResearchProjectDef> pending = fromCasket ? legacy.PendingCasketGrants : legacy.PendingGrants;
                        foreach (ResearchProjectDef proj in pending.ToList())
                        {
                            if (proj.IsFinished)
                            {
                                // 玩家自行研究完成；制裁在授予時已標記，不會重複。The player finished it; already marked as sanctioned.
                                legacy.RemovePending(proj);
                                continue;
                            }
                            if (!proj.PrerequisitesCompleted)
                            {
                                continue;
                            }
                            // 先移除再完成，巢狀呼叫看不到它。Remove before finishing so a nested call can't see it.
                            legacy.RemovePending(proj);
                            Complete(proj);
                            string labelKey = fromCasket
                                ? "DMS_ArchiveCasket_DecodedLabel"
                                : "DMS_DeviantRefuge_PendingDecodedLabel";
                            string textKey = fromCasket
                                ? "DMS_ArchiveCasket_DecodedText"
                                : "DMS_DeviantRefuge_PendingDecodedText";
                            Find.LetterStack.ReceiveLetter(
                                labelKey.Translate(proj.LabelCap.Named("PROJECT")),
                                textKey.Translate(proj.LabelCap.Named("PROJECT")),
                                LetterDefOf.PositiveEvent);
                            progressed = true;
                        }
                    }
                }
            }
            finally
            {
                checkingPending = false;
            }
        }
    }
}
