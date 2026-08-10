using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 標記一件裝備為「穿上後需要手術才能脫下」。
    /// 穿戴時會透過 Pawn_ApparelTracker.Lock 上鎖（沿用原版鎖定機制，
    /// 因此 Gear 面板脫下鈕、服裝最佳化 AI、剝除、商隊裝備頁都會自動遵守），
    /// 只有 removalRecipe 指定的手術能解鎖並取回。
    /// </summary>
    public class CompProperties_SurgicalApparel : CompProperties
    {
        /// <summary>解除穿戴所需的手術配方，用於提示文字與說明超連結。</summary>
        public RecipeDef removalRecipe;

        /// <summary>玩家手動下令穿戴前是否跳出確認視窗。</summary>
        public bool confirmBeforeWearing = true;

        /// <summary>確認視窗內文的 Keyed 索引（會帶入 0=裝備名稱, 1=手術名稱）。</summary>
        public string confirmMessageKey = "DMS_SurgicalApparel_WearConfirm";

        /// <summary>裝備欄位說明段落的 Keyed 索引（會帶入 0=手術名稱）。</summary>
        public string inspectMessageKey = "DMS_SurgicalApparel_Inspect";

        public CompProperties_SurgicalApparel()
        {
            compClass = typeof(CompSurgicalApparel);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }

            if (parentDef.apparel == null)
            {
                yield return "CompSurgicalApparel 只能掛在服裝 (apparel) 上。";
            }
        }
    }

    public class CompSurgicalApparel : ThingComp
    {
        public CompProperties_SurgicalApparel Props => (CompProperties_SurgicalApparel)props;

        public string RemovalRecipeLabel =>
            Props.removalRecipe != null ? Props.removalRecipe.LabelCap.ToString() : string.Empty;

        public override string CompInspectStringExtra()
        {
            if (!(parent is Apparel apparel) || apparel.Wearer == null)
            {
                return null;
            }

            return Props.inspectMessageKey.Translate(RemovalRecipeLabel);
        }

        public override string GetDescriptionPart()
        {
            return Props.inspectMessageKey.Translate(RemovalRecipeLabel);
        }
    }

    public static class SurgicalApparelUtility
    {
        public static CompSurgicalApparel GetSurgicalComp(this Thing thing)
        {
            return (thing as Apparel)?.TryGetComp<CompSurgicalApparel>();
        }

        public static bool IsSurgicallyBonded(this Thing thing)
        {
            return thing.GetSurgicalComp() != null;
        }

        /// <summary>
        /// 找出這隻 pawn 身上第一件屬於指定 def 且已鎖定的手術裝備。
        /// </summary>
        public static Apparel FindBondedApparel(Pawn pawn, ThingDef def)
        {
            if (pawn?.apparel == null)
            {
                return null;
            }

            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].def == def && worn[i].IsSurgicallyBonded())
                {
                    return worn[i];
                }
            }

            return null;
        }
    }
}
