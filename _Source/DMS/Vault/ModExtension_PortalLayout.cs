using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 MapPortal 類 ThingDef 上，指定它產生的口袋地圖要跑哪個 GenStep 與哪張 StructureLayoutDef。
    /// Attached to a MapPortal ThingDef to override which GenStep runs on the generated pocket map
    /// and which <see cref="LayoutDef"/> that GenStep should lay out.
    ///
    /// 原版 <see cref="AncientHatch"/> 硬綁 GenStepDefOf.AncientStockpile + LayoutDefOf.AncientStockpile，
    /// 這個擴充讓 DMS 變體能沿用同一套產生流程但換掉版面。
    /// Vanilla AncientHatch hardcodes GenStepDefOf.AncientStockpile / LayoutDefOf.AncientStockpile;
    /// this extension lets a DMS variant reuse the same pipeline with a different layout.
    /// </summary>
    public class ModExtension_PortalLayout : DefModExtension
    {
        /// <summary>要額外執行的 GenStep（通常就是原版的 AncientStockpile）。</summary>
        public GenStepDef genStep;

        /// <summary>交給 GenStep 的版面定義。null 時退回 GenStep 自己的預設。</summary>
        public LayoutDef layout;
    }
}
