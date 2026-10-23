using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 具名封存匣共用的判定：哪些檔案有資格被封進匣裡、新匣子的檔名怎麼來。
    ///
    /// 資格只有兩條：還沒完成、前置已完成。第二條是硬條件，不是偏好——對前置未完成的專案呼叫
    /// FinishProject，原版會把整串前置一起完成，等於白送一長段常規研究。
    ///
    /// Two conditions only: unfinished, and prerequisites already done. The second is not a preference:
    /// FinishProject on a project with unmet prerequisites finishes the whole prerequisite chain.
    /// </summary>
    public static class ArchiveCasketUtility
    {
        public static List<ResearchProjectDef> EligibleProjects(CompProperties_UseEffect_UnsealArchive props)
        {
            return EligibleProjects(props?.researchProjects);
        }

        public static List<ResearchProjectDef> EligibleProjects(IEnumerable<ResearchProjectDef> source)
        {
            if (source == null)
            {
                return new List<ResearchProjectDef>();
            }
            // 前置不列入條件：解封時前置未齊會比照脫序者收留轉成待解譯（見 DeviantTechUtility.GrantFromCasket）。
            // 只排除已完成與已經在待解譯清單裡的（那件事遲早會自己完成）。
            // Prerequisites are deliberately not part of the filter: an unmet one turns the unsealing into a
            // pending grant, the same way a deviant handover does. Only finished and already-pending files are out.
            GameComponent_DeviantLegacy legacy = GameComponent_DeviantLegacy.CompSafe;
            return source
                .Where(p => p != null && !p.IsFinished && (legacy == null || !legacy.IsPending(p)))
                .ToList();
        }

        /// <summary>
        /// 從 DMS_ArchiveCasketName 規則包生成一個檔名。規則包按語言定義，
        /// 所以同一顆種子的檔名會是該語言自己的樣子。
        /// The designation comes off a rule pack, so it is defined per language rather than in C#.
        /// </summary>
        public static string GenerateArchiveName()
        {
            if (DMS_DefOf.DMS_ArchiveCasketName == null)
            {
                Log.Error("[DMS] Missing RulePackDef DMS_ArchiveCasketName; archive caskets will be nameless.");
                return null;
            }
            return NameGenerator.GenerateName(DMS_DefOf.DMS_ArchiveCasketName);
        }
    }
}
