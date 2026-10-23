using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 具名封存匣：一件物品、一個檔名、一份完整的封存檔案。
    ///
    /// 匣內放的檔案在「製作完成」那一刻就決定：<see cref="Thing.PostMake"/> 時向
    /// <see cref="CompUseEffect_UnsealArchive"/> 要一份可還原的檔案寫進 <see cref="storedProject"/>，
    /// 檔名同時從 DMS_ArchiveCasketName 規則包生成。所以同一份 Def 做出來的每個匣子內容都不同，
    /// 而且兩者都寫進存檔，存讀檔不會重抽，也不會因為重抽而對不上已經發出去的信件。
    ///
    /// A casket holds one whole sealed file. The file is drawn in PostMake, when the casket is made,
    /// and scribed along with the designation generated at the same moment: two caskets of the same
    /// def never hold the same file by accident, and loading a save never rerolls either of them.
    /// </summary>
    public class Item_ArchiveCasket : ThingWithComps
    {
        /// <summary>封條上刻的檔名。Generated archive designation stamped on the seal.</summary>
        public string archiveName;

        /// <summary>匣裡的檔案。The file inside the casket.</summary>
        public ResearchProjectDef storedProject;

        /// <summary>匣裡是否還有一份沒開過的檔案。Whether the casket still holds an unopened file.</summary>
        public bool HasSealedFile => storedProject != null && !storedProject.IsFinished;

        public override string LabelNoCount
        {
            get
            {
                if (archiveName.NullOrEmpty())
                {
                    return base.LabelNoCount;
                }
                return "DMS_ArchiveCasket_LabelFormat".Translate(base.LabelNoCount, archiveName);
            }
        }

        public override void PostMake()
        {
            base.PostMake();
            AssignContents();
        }

        /// <summary>
        /// 抽檔名與檔案。只在物品誕生時跑一次（PostMake），不是每次使用才抽。
        /// Draws the designation and the file. Runs once, when the item is created, not on use.
        /// </summary>
        private void AssignContents()
        {
            if (archiveName.NullOrEmpty())
            {
                archiveName = ArchiveCasketUtility.GenerateArchiveName();
            }
            if (storedProject == null)
            {
                storedProject = GetComp<CompUseEffect_UnsealArchive>()?.PickProject();
            }
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            if (storedProject == null)
            {
                return text;
            }
            string tier = storedProject.knowledgeCategory != null
                ? storedProject.knowledgeCategory.LabelCap.ToString()
                : string.Empty;
            string line = "DMS_ArchiveCasket_Contents".Translate(storedProject.LabelCap, tier);
            return text.NullOrEmpty() ? line : text + "\n" + line;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref archiveName, "archiveName");
            Scribe_Defs.Look(ref storedProject, "storedProject");
            // 舊存檔或除錯生成的匣子沒有檔名：補一個，不要讓標籤空著。
            // A casket from an older save or a debug spawn has no designation yet: give it one.
            if (Scribe.mode == LoadSaveMode.PostLoadInit && archiveName.NullOrEmpty())
            {
                archiveName = ArchiveCasketUtility.GenerateArchiveName();
            }
        }
    }
}
