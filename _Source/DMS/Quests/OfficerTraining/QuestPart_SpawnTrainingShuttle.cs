using Verse;

namespace DMS
{
    /// <summary>受訓任務的接送機:降落等學員登機,信件走 DMS_OfficerTrainingLetters。</summary>
    public class QuestPart_SpawnTrainingShuttle : QuestPart_SpawnPickupShuttle
    {
        /// <summary>課程名稱,寫進抵達信件。</summary>
        public string courseName;

        protected override RulePackDef LetterPack => OfficerTrainingText.Pack;

        protected override string[] LetterVars()
        {
            return new[]
            {
                "traineeName", passenger?.LabelShort ?? "?",
                "issuerFactionName", issuerFactionName ?? "?",
                "askerName", askerName ?? "?",
                "courseName", courseName ?? "?",
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref courseName, "courseName");
        }
    }

    /// <summary>軍官受訓文本 pack 快取。</summary>
    public static class OfficerTrainingText
    {
        private static RulePackDef cached;
        public static RulePackDef Pack =>
            cached ??= DefDatabase<RulePackDef>.GetNamed("DMS_OfficerTrainingLetters");
    }
}
