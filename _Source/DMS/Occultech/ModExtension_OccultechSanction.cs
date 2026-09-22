using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 封存科技的三個管制等級。掛在 <see cref="KnowledgeCategoryDef"/> 上，
    /// 決定該類別的研究完成時要對玩家施加哪一種制裁。
    ///
    /// The three Occultech classification tiers. Attached to a KnowledgeCategoryDef to
    /// decide which sanction fires when a project of that category is completed.
    /// </summary>
    public enum OccultechTier
    {
        /// <summary>限制級：扣好感度，有軍官在場可豁免。</summary>
        Restricted,
        /// <summary>封存級：立即敵對＋襲擊，必須放棄技術才能回到盟友。</summary>
        Sealed,
        /// <summary>隱匿級：永久敵對＋追殺＋連坐盟友，只能靠軍事法庭解除。</summary>
        Occulted,
    }

    /// <summary>
    /// 封存科技制裁規則。掛在 <see cref="KnowledgeCategoryDef"/> 的 modExtensions 上；
    /// 沒掛的類別完全不受制裁機制影響（其他模組的知識分頁不會被波及）。
    ///
    /// 豁免以 <see cref="exemptSeniority"/> 表示而非直接引用 RoyalTitleDef，因為軍銜 Def 位於
    /// 1.6/Royalty/ 之下，只有啟用 Royalty DLC 時才存在，直接跨引用會在無 DLC 時炸掉 Def 解析。
    /// </summary>
    public class ModExtension_OccultechSanction : DefModExtension
    {
        /// <summary>本類別屬於哪一個管制等級。</summary>
        public OccultechTier tier = OccultechTier.Restricted;

        /// <summary>完成一項本級研究時對艦隊好感度的影響（負值）。0 ＝不扣。</summary>
        public int goodwillPenalty;

        /// <summary>
        /// 豁免所需的最低艦隊官階 seniority。負數 ＝ 無豁免條件。
        /// 殖民地（含商隊／運輸途中）任一自由殖民者持有艦隊官階且 seniority 達標即豁免。
        /// </summary>
        public int exemptSeniority = -1;

        /// <summary>研究完成後是否立即與艦隊敵對。</summary>
        public bool hostileOnComplete;

        /// <summary>敵對後是否立刻降下一波襲擊。</summary>
        public bool raidOnComplete;

        /// <summary>立即襲擊的威脅點數係數與下限。</summary>
        public float raidPointsFactor = 1f;
        public float minRaidPoints = 300f;

        /// <summary>
        /// 是否為永久敵對（隱匿級）。永久敵對期間所有對艦隊的正向好感度變動都會被擋下，
        /// 只能透過軍事法庭（<see cref="QuestPart_CourtTruce"/>）解除。
        /// </summary>
        public bool permanentHostility;

        /// <summary>永久敵對期間的追殺襲擊間隔（遊戲日）與強度係數。</summary>
        public FloatRange huntIntervalDays = new FloatRange(4f, 7f);
        public float huntPointsFactor = 1.25f;

        /// <summary>連坐：與多少個艦隊友好派系一併敵對（不鎖好感，可自行修復）。</summary>
        public int hostileAllyCount;

        /// <summary>
        /// 是否可透過通訊台向艦隊申報放棄。封存級為 true：放棄後研究進度歸零、
        /// 解鎖物重新上鎖，艦隊關係才能重新升到盟友。
        /// </summary>
        public bool renounceable;

        public override string ToString()
        {
            return $"OccultechSanction({tier})";
        }
    }
}
