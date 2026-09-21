using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 給 <see cref="Building_RefuelableGraphic"/> 用：CompRefuelable 空了之後改畫的貼圖。
    /// For <see cref="Building_RefuelableGraphic"/>: the graphic drawn once its CompRefuelable runs dry.
    /// </summary>
    public class ModExtension_RefuelableGraphic : DefModExtension
    {
        public GraphicData emptyGraphicData;
    }

    /// <summary>
    /// 有 CompRefuelable 的建築，燃料歸零時換成 <see cref="ModExtension_RefuelableGraphic.emptyGraphicData"/>
    /// 的貼圖（例如發射器箱打完後的空箱）。建築是靜態網格，燃料狀態變了要自己把網格標髒重畫。
    /// A building with a CompRefuelable that switches to
    /// <see cref="ModExtension_RefuelableGraphic.emptyGraphicData"/> once its fuel hits zero (an empty
    /// launcher case after firing, say). Buildings are drawn in the static map mesh, so the mesh is
    /// dirtied by hand whenever the loaded state flips.
    /// </summary>
    public class Building_RefuelableGraphic : Building
    {
        private CompRefuelable refuelable;
        private Graphic emptyGraphic;
        private bool lastDrawnLoaded;

        private CompRefuelable Refuelable => refuelable ??= GetComp<CompRefuelable>();

        /// <summary>用 Fuel 而不是 HasFuel：真空之類的運作條件不該讓箱子看起來變空。Fuel rather than HasFuel: vacuum and the like must not make the case look empty.</summary>
        public bool Loaded => Refuelable == null || Refuelable.Fuel > 0f;

        public override Graphic Graphic
        {
            get
            {
                if (Loaded) return base.Graphic;
                if (emptyGraphic == null)
                {
                    GraphicData data = def.GetModExtension<ModExtension_RefuelableGraphic>()?.emptyGraphicData;
                    emptyGraphic = data != null ? data.GraphicColoredFor(this) : base.Graphic;
                }
                return emptyGraphic;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            lastDrawnLoaded = Loaded;
        }

        protected override void Tick()
        {
            base.Tick();
            bool loaded = Loaded;
            if (loaded == lastDrawnLoaded) return;
            lastDrawnLoaded = loaded;
            if (Spawned) DirtyMapMesh(Map);
        }
    }
}
