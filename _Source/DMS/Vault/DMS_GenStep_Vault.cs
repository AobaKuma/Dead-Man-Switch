using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 在口袋地圖中央鋪一張 StructureLayoutDef。等同原版 GenStep_AncientStockpile，
    /// 但拿掉了 ModsConfig.OdysseyActive 的閘門，並允許直接在 XML 指定版面。
    ///
    /// Lays a StructureLayoutDef in the middle of the map. Same shape as vanilla
    /// GenStep_AncientStockpile minus the Odyssey gate, plus an XML-settable fallback layout.
    /// 版面優先序：GenStepParams.layout（由傳送門帶進來）→ 本 def 的 layoutDef。
    /// </summary>
    public class DMS_GenStep_Vault : GenStep
    {
        /// <summary>沒有從 GenStepParams 拿到版面時使用的預設。Fallback layout.</summary>
        public LayoutDef layoutDef;

        /// <summary>結構佔地範圍（正方形邊長）。Footprint edge length.</summary>
        public IntRange sizeRange = new IntRange(40, 50);

        public override int SeedPart => 1078342511;

        public override void Generate(Map map, GenStepParams parms)
        {
            LayoutDef layout = parms.layout ?? layoutDef;
            if (layout == null)
            {
                Log.Error("[DMS] DMS_GenStep_Vault: no layout supplied by the portal and no layoutDef set.");
                return;
            }

            CellRect rect = map.Center.RectAbout(new IntVec2(sizeRange.RandomInRange, sizeRange.RandomInRange));

            StructureGenParams structureParms = new StructureGenParams
            {
                size = rect.Size
            };

            LayoutWorker worker = layout.Worker;
            LayoutStructureSketch sketch = worker.GenerateStructureSketch(structureParms);
            map.layoutStructureSketches.Add(sketch);

            float? threatPoints = null;
            if (parms.sitePart != null)
            {
                threatPoints = parms.sitePart.parms.points;
            }

            worker.Spawn(sketch, map, rect.Min, threatPoints);
        }
    }
}
