using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// DMS 版地下設施出入口：一台仍能運作的貨運電梯。
    /// A DMS entrance to an underground facility — a freight elevator that still runs.
    ///
    /// 直接繼承 <see cref="MapPortal"/>（Core），不走 Odyssey 的 AncientHatch，因此不需要 DLC。
    /// 自行處理三件事：
    ///   1. 駭入才能進入（CompHackable）。
    ///   2. 依 <see cref="ModExtension_PortalLayout"/> 決定口袋地圖要跑哪個 GenStep 與版面。
    ///   3. 封閉後改畫 <see cref="ModExtension_SealedGraphic"/> 的貼圖。
    ///
    /// Extends Core's MapPortal directly rather than Odyssey's AncientHatch, so no DLC is required.
    /// It handles the hack gate, the extra gen step, and the sealed graphic itself.
    /// </summary>
    public class Building_VaultElevator : MapPortal
    {
        private ModExtension_PortalLayout extCached;
        private bool extResolved;

        private CompHackable hackableCached;
        private CompSealable sealableCached;
        private Graphic sealedGraphicCached;

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

        private CompHackable Hackable => hackableCached ?? (hackableCached = GetComp<CompHackable>());

        private CompSealable Sealable => sealableCached ?? (sealableCached = GetComp<CompSealable>());

        // ── 進入條件 / Entry gate ──────────────────────────────────────────────

        public override bool IsEnterable(out string reason)
        {
            CompHackable hack = Hackable;
            if (hack != null && !hack.IsHacked)
            {
                reason = "Locked".Translate();
                return false;
            }

            // base 會走一遍所有 comp 的 CanEnterPortal()，密封狀態就是在那裡擋下來的。
            // The base walks every comp's CanEnterPortal(); that's where sealing is enforced.
            return base.IsEnterable(out reason);
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            CompHackable hack = Hackable;
            if (hack != null && hack.IsHacked)
            {
                sb.AppendLineIfNotEmpty();
                sb.Append("DMS_ElevatorUnlocked".Translate());
            }
            return sb.ToString();
        }

        // ── 繪製 / Drawing ────────────────────────────────────────────────────

        /// <summary>
        /// CompSealable.isSealed 是私有的，但封閉之後 CanEnterPortal() 就只會因為這個理由失敗，
        /// 所以拿它當判斷來源，不需要 publicize 私有欄位。
        /// CompSealable.isSealed is private, but sealing is the only thing that makes
        /// CanEnterPortal() fail, so we read the state through that instead of publicizing a field.
        /// </summary>
        private bool IsSealed
        {
            get
            {
                CompSealable comp = Sealable;
                return comp != null && !comp.CanEnterPortal().Accepted;
            }
        }

        private Graphic SealedGraphic
        {
            get
            {
                if (sealedGraphicCached == null)
                {
                    GraphicData data = def.GetModExtension<ModExtension_SealedGraphic>()?.sealedGraphicData;
                    if (data != null)
                    {
                        sealedGraphicCached = data.GraphicColoredFor(this);
                    }
                }
                return sealedGraphicCached;
            }
        }

        /// <summary>
        /// Graphic_Multi 會依 <see cref="Thing.Rotation"/> 選面，所以四張 building_north/east/south/west
        /// 都會被用到。封閉後改畫 Elevator_sealed 那組；CompSealable.Seal() 會呼叫 DirtyMapMesh，
        /// 所以不需要額外通知重繪。
        /// Sealing already calls DirtyMapMesh, so swapping the graphic here is enough to refresh it.
        /// </summary>
        public override void Print(SectionLayer layer)
        {
            Graphic graphic = (IsSealed ? SealedGraphic : null) ?? Graphic;
            graphic.Print(layer, this, 0f);
        }

        // ── 口袋地圖產生 / Pocket map generation ───────────────────────────────

        protected override IEnumerable<GenStepWithParams> GetExtraGenSteps()
        {
            ModExtension_PortalLayout ext = Ext;
            if (ext?.genStep == null)
            {
                Log.ErrorOnce(
                    $"[DMS] {def.defName} has no ModExtension_PortalLayout.genStep; the pocket map will be empty.",
                    def.shortHash);
                yield break;
            }

            yield return new GenStepWithParams(ext.genStep, new GenStepParams
            {
                layout = ext.layout
            });
        }
    }
}
