using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// DMS 版地下設施出入口：一台仍能運作的貨運電梯。
    /// A DMS-flavoured entrance to an underground facility — a freight elevator that still runs.
    ///
    /// 繼承 <see cref="AncientHatch"/> 以沿用「駭入後才能進入 + 口袋地圖 + 密封」的整套行為，
    /// 但改寫兩件事：
    ///   1. Print()：原版會在解鎖後強制切換到 AncientHatch_Open 單張貼圖，DMS 電梯用的是
    ///      Graphic_Multi（Things/Building/Elevator/building_*），所以直接畫自己的 Graphic。
    ///   2. GetExtraGenSteps()：原版寫死 GenStepDefOf.AncientStockpile，改由 ThingDef 上的
    ///      <see cref="ModExtension_PortalLayout"/> 指定，讓 DMS 能塞自己的版面。
    ///
    /// Inherits AncientHatch for the hack-to-open / pocket-map / sealable behaviour, but overrides
    /// Print (vanilla swaps to a hardcoded single-texture "open" graphic) and GetExtraGenSteps
    /// (vanilla hardcodes the AncientStockpile genstep + layout).
    /// </summary>
    public class Building_VaultElevator : AncientHatch
    {
        private ModExtension_PortalLayout extCached;
        private bool extResolved;

        private ModExtension_PortalLayout Ext
        {
            get
            {
                if (!extResolved)
                {
                    extCached = def.GetModExtension<ModExtension_PortalLayout>();
                    extResolved = true;
                }
                return extCached;
            }
        }

        // ── 繪製 / Drawing ────────────────────────────────────────────────────

        /// <summary>
        /// 直接畫 def 的 Graphic。Graphic_Multi 會依 <see cref="Thing.Rotation"/> 選面，
        /// 所以四張 building_north/east/south/west 都會被用到。
        /// </summary>
        public override void Print(SectionLayer layer)
        {
            Graphic.Print(layer, this, 0f);
        }

        // ── 口袋地圖產生 / Pocket map generation ───────────────────────────────

        protected override IEnumerable<GenStepWithParams> GetExtraGenSteps()
        {
            ModExtension_PortalLayout ext = Ext;
            if (ext?.genStep == null)
            {
                // 沒設定就退回原版行為，至少不會產生一張空地圖。
                // Fall back to vanilla so we never hand back a blank map.
                foreach (GenStepWithParams step in base.GetExtraGenSteps())
                {
                    yield return step;
                }
                yield break;
            }

            // layout 欄位（AncientHatch 自帶、可被任務覆寫）優先，其次才是 ThingDef 上的預設。
            // The inherited `layout` field wins if something set it; otherwise use the ThingDef default.
            yield return new GenStepWithParams(ext.genStep, new GenStepParams
            {
                layout = layout ?? ext.layout
            });
        }
    }
}
