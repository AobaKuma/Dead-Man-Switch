using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 RoyalTitleDef 上：標記「這個階級不辦授銜典禮,改用受訓晉升任務」。
    /// Patch_GenerateBestowingCeremonyQuest 讀到就改跑 DMS_OfficerTraining。
    /// </summary>
    public class TitleTrainingExtension : DefModExtension
    {
        /// <summary>受訓天數(登機到完訓)。</summary>
        public IntRange trainingDaysRange = new IntRange(15, 20);

        /// <summary>完訓灌進課程技能的經驗值。原版升級曲線:L→L+1 需要 1000×(L+1)。</summary>
        public float skillXp = 35000f;

        /// <summary>該技能無熱情時升一級熱情的機率(小熱情→大熱情用一半機率)。</summary>
        public float passionUpgradeChance = 0.25f;

        /// <summary>沒有負面特質可移除時的經驗補償倍率(1 + 這個值)。</summary>
        public float noTraitBonusXpFactor = 0.5f;
    }

    /// <summary>受訓課程:一門課對應一個技能與一組文案。</summary>
    public class TrainingCourse
    {
        public SkillDef skill;

        /// <summary>課程名稱的 Keyed 鍵值,例如 DMS_Course_Infantry。</summary>
        [NoTranslate]
        public string labelKey;

        public float weight = 1f;

        public string ResolvedLabel =>
            labelKey.NullOrEmpty() ? (skill?.LabelCap.ToString() ?? "?") : labelKey.Translate().ToString();
    }

    /// <summary>
    /// 特質白名單條目。刻意不用 Verse.TraitRequirement —— 那個是 TraitDef 直接參照,
    /// defName 打錯或該特質來自沒裝的 DLC 會在載入期噴紅字;這裡改成字串延遲解析,
    /// 找不到就當作沒這條。
    /// </summary>
    public class TraitEntry
    {
        /// <summary>TraitDef 的 defName。</summary>
        [NoTranslate]
        public string trait;

        /// <summary>指定 degree;留空(int.MinValue)代表任何 degree 都算。</summary>
        public int degree = int.MinValue;

        private TraitDef resolved;
        private bool resolvedOnce;

        public TraitDef Def
        {
            get
            {
                if (!resolvedOnce)
                {
                    resolvedOnce = true;
                    resolved = trait.NullOrEmpty() ? null : DefDatabase<TraitDef>.GetNamedSilentFail(trait);
                }
                return resolved;
            }
        }

        public bool Matches(Trait t)
        {
            if (t?.def == null || Def == null || t.def != Def) return false;
            return degree == int.MinValue || t.Degree == degree;
        }

        public static bool AnyMatch(List<TraitEntry> entries, Trait t)
        {
            if (entries == null) return false;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].Matches(t)) return true;
            return false;
        }
    }
}
