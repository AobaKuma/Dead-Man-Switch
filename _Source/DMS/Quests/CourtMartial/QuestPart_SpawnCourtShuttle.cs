using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// inSignal(接受任務):讓生成時預先建立的穿梭機 Thing 以 TransportShip 降落,
    /// 等待被告登機(登機即離場),並發出抵達信件。
    ///
    /// 共用邏輯已抽到 QuestPart_SpawnPickupShuttle;本類別名不可更動 —— QuestPart 以
    /// 類別名存檔,改名會讓正在跑軍事法庭的存檔讀不回這個 part。
    /// </summary>
    public class QuestPart_SpawnCourtShuttle : QuestPart_SpawnPickupShuttle
    {
        protected override RulePackDef LetterPack => CourtMartialText.Pack;

        /// <summary>舊存檔用的是 "defendant",維持不變。</summary>
        protected override string PassengerScribeLabel => "defendant";

        protected override string[] LetterVars()
        {
            return new[]
            {
                "defendantName", passenger?.LabelShort ?? "?",
                "issuerFactionName", issuerFactionName ?? "?",
                "askerName", askerName ?? "?",
            };
        }
    }

    /// <summary>軍事法庭文本 pack 快取。</summary>
    public static class CourtMartialText
    {
        private static RulePackDef cached;
        public static RulePackDef Pack =>
            cached ??= DefDatabase<RulePackDef>.GetNamed("DMS_CourtMartialLetters");
    }
}
