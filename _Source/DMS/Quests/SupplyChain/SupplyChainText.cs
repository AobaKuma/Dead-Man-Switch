using Verse;
using Verse.Grammar;

namespace DMS
{
    /// <summary>執行期解析 DMS_SupplyChainLetters 文法,產生隨機排列的信件/訊息文本。</summary>
    public static class SupplyChainText
    {
        private static RulePackDef cachedPack;
        private static RulePackDef Pack =>
            cachedPack ??= DefDatabase<RulePackDef>.GetNamed("DMS_SupplyChainLetters");

        public static string Resolve(string rootKeyword, params string[] keyValuePairs)
            => Resolve(Pack, rootKeyword, keyValuePairs);

        /// <summary>通用版:以任意 RulePackDef 解析,供其他任務(如軍事法庭)重用。</summary>
        public static string Resolve(RulePackDef pack, string rootKeyword, params string[] keyValuePairs)
        {
            GrammarRequest request = default;
            request.Includes.Add(pack);
            for (int i = 0; i + 1 < keyValuePairs.Length; i += 2)
                request.Rules.Add(new Rule_String(keyValuePairs[i], keyValuePairs[i + 1]));
            return GrammarResolver.Resolve(rootKeyword, request, "DMS_QuestText");
        }
    }
}
