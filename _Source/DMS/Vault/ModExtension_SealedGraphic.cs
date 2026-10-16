using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在具 CompSealable 的 MapPortal 上，指定封閉之後要換上的貼圖。
    /// Attached to a sealable MapPortal to give it a distinct graphic once it has been sealed.
    ///
    /// 沒有這個擴充時建築就維持原本的 graphicData，所以是純加值、可選的。
    /// Without this extension the building just keeps its normal graphicData; it is purely optional.
    /// </summary>
    public class ModExtension_SealedGraphic : DefModExtension
    {
        /// <summary>封閉後使用的圖像資料。Graphic used once sealed.</summary>
        public GraphicData sealedGraphicData;
    }
}
