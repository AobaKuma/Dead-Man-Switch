using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 具名封存匣的解封效果：把匣裡那一份完整檔案直接完成。
    ///
    /// 與 FFF 的 CompProperties_UseEffect_DiscoverResearch 不同：那一個是在「使用時」才抽檔案、
    /// 而且只給部分進度；封存匣的檔案是製作時就抽好的（見 <see cref="Item_ArchiveCasket.PostMake"/>），
    /// 使用只是把它打開。抽檔案要放在製作那一刻，玩家才會在物品標籤上看到自己拿到哪一份。
    ///
    /// Unlike FFF's use-time DiscoverResearch comp, the file here is already decided when the casket is
    /// made, so the item's label can tell the player what it holds. Opening it completes that file.
    /// </summary>
    public class CompUseEffect_UnsealArchive : CompUseEffect
    {
        public CompProperties_UseEffect_UnsealArchive Props => (CompProperties_UseEffect_UnsealArchive)props;

        /// <summary>
        /// 從 props 的清單裡抽一份檔案；抽不到回 null。前置已完成的優先——大多數匣子應該一開就解鎖，
        /// 只有在池子裡一份可讀的都不剩時，才會抽到「要等前置」的那種（比照脫序者挑檔案的順序）。
        /// Picks a file, preferring ones whose prerequisites are done so that most caskets unlock at once; only
        /// when nothing in the pool is readable yet does it hand out one that has to wait, mirroring the
        /// deviant's picking order.
        /// </summary>
        public ResearchProjectDef PickProject()
        {
            List<ResearchProjectDef> pool = ArchiveCasketUtility.EligibleProjects(Props);
            if (pool.Count == 0)
            {
                return null;
            }
            List<ResearchProjectDef> ready = pool.Where(p => p.PrerequisitesCompleted).ToList();
            return (ready.Count > 0 ? ready : pool).RandomElement();
        }

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            Item_ArchiveCasket casket = parent as Item_ArchiveCasket;
            // 空匣子（生成時池子裡剛好沒有可還原的檔案）在使用時再抽一次：玩家可能已經補上前置研究了。
            // 封存匣現在只從駭入與檔案庫取得，前期就撿到一顆不該是死物。
            // A casket spawned when nothing resolvable gets one more draw on use, in case the
            // prerequisites have been finished since. Caskets now come from hacks and archives only,
            // so an early find must not be a dead item.
            if (casket != null && casket.storedProject == null)
            {
                casket.storedProject = PickProject();
            }
            ResearchProjectDef proj = casket?.storedProject;
            if (proj != null && !proj.IsFinished)
            {
                Open(casket, proj, usedBy);
                return;
            }
            GrantFallback(casket, usedBy, proj);
        }

        /// <summary>
        /// 打開匣子：探明，然後完成或暫緩。走 <see cref="DeviantTechUtility.GrantFromCasket"/>，
        /// 與脫序者收留共用同一條流程——前置齊了就完成（制裁與研究信件照常由 FinishProject 的 postfix 結算），
        /// 前置未齊則登記成待解譯，前置補齊時自動解譯。
        /// Opens the casket through GrantFromCasket, the same flow the deviant handover uses: finish it when the
        /// prerequisites are in place (sanctions and the research letter still come from FinishProject), otherwise
        /// park it as pending until they are.
        /// </summary>
        private void Open(Item_ArchiveCasket casket, ResearchProjectDef proj, Pawn usedBy)
        {
            DeviantGrantResult result = DeviantTechUtility.GrantFromCasket(proj);
            if (result == DeviantGrantResult.AlreadyHeld)
            {
                GrantFallback(casket, usedBy, proj);
                return;
            }
            // 先取成字串再 Named()：寫成 usedBy?.LabelShort.Named(...) 會得到 NamedArgument?，
            // 被裝箱走進過時的多載。Take the strings out first: a ?. chain yields NamedArgument?,
            // which boxes into the wrong overload.
            string pawnName = usedBy != null ? usedBy.LabelShort : "?";
            string casketName = casket.LabelNoCount;
            if (result == DeviantGrantResult.Pending)
            {
                Find.LetterStack.ReceiveLetter(
                    "DMS_ArchiveCasket_PendingLabel".Translate(proj.LabelCap),
                    "DMS_ArchiveCasket_PendingText".Translate(
                        pawnName.Named("PAWN"),
                        casketName.Named("CASKET"),
                        proj.LabelCap.Named("PROJECT"),
                        DeviantTechUtility.MissingPrerequisites(proj).Named("PREREQS")),
                    Props.letterDef ?? LetterDefOf.PositiveEvent);
                return;
            }
            Find.LetterStack.ReceiveLetter(
                "DMS_ArchiveCasket_UnsealedLabel".Translate(proj.LabelCap),
                "DMS_ArchiveCasket_UnsealedText".Translate(
                    pawnName.Named("PAWN"),
                    casketName.Named("CASKET"),
                    proj.LabelCap.Named("PROJECT")),
                Props.letterDef ?? LetterDefOf.PositiveEvent);
        }

        /// <summary>
        /// 匣裡沒有還開著的檔案（檔案早被玩家自行研究完，或製作時就抽不到）：退回智識經驗，
        /// 不讓一件貴重物品白白吃掉。與封存資料匣抽不到專案時的處理一致。
        /// No unopened file left (the player finished it by hand, or nothing was resolvable at craft
        /// time): fall back to Intellectual experience, as the data caches do when their pool runs dry.
        /// </summary>
        private void GrantFallback(Item_ArchiveCasket casket, Pawn usedBy, ResearchProjectDef proj)
        {
            float xp = Props.fallbackXpAmount;
            SkillDef skill = Props.fallbackSkill ?? SkillDefOf.Intellectual;
            bool learned = false;
            if (xp > 0f && usedBy != null && usedBy.skills != null)
            {
                usedBy.skills.Learn(skill, xp, direct: true);
                learned = true;
            }
            string casketName = casket?.archiveName ?? parent.LabelCap.ToString();
            // 同上：先轉字串，避免 NamedArgument? 裝箱。Same as above: keep it a plain string.
            string pawnName = usedBy != null ? usedBy.LabelShort : "?";
            string text = proj != null
                // 檔案確實存在，只是已經開過了。The file exists but was already opened.
                ? "DMS_ArchiveCasket_AlreadyOpenText".Translate(casketName.Named("CASKET"), pawnName.Named("PAWN"))
                // 匣子從頭到尾就是空的。The casket never held anything.
                : "DMS_ArchiveCasket_EmptyText".Translate(casketName.Named("CASKET"), pawnName.Named("PAWN"));
            Messages.Message(text, parent, learned ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput);
        }
    }

    public class CompProperties_UseEffect_UnsealArchive : CompProperties_UseEffect
    {
        /// <summary>可以封進匣裡的檔案清單。III 級（隱匿級）刻意不列入。</summary>
        public List<ResearchProjectDef> researchProjects;

        /// <summary>匣裡沒有還開著的檔案時退回的經驗值。Experience granted when no file is left to open.</summary>
        public float fallbackXpAmount = 3000f;

        public SkillDef fallbackSkill;

        public LetterDef letterDef;

        public CompProperties_UseEffect_UnsealArchive()
        {
            compClass = typeof(CompUseEffect_UnsealArchive);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            string name = parentDef != null ? parentDef.defName : "(null)";
            if (researchProjects.NullOrEmpty())
            {
                yield return $"{name}: CompProperties_UseEffect_UnsealArchive 需要至少一個 researchProjects 項目（無效的 defName 會被靜默移除，請確認拼字）。";
            }
            else
            {
                for (int i = 0; i < researchProjects.Count; i++)
                {
                    if (researchProjects[i] == null)
                    {
                        yield return $"{name}: CompProperties_UseEffect_UnsealArchive 的 researchProjects 第 {i} 項為 null。";
                    }
                }
            }
            if (fallbackXpAmount < 0f)
            {
                yield return $"{name}: CompProperties_UseEffect_UnsealArchive 的 fallbackXpAmount 不可為負數。";
            }
        }
    }
}
