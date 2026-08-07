using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在二次發育體建築上：純計時，時間到就原地換成 resultDef。
    /// 同時描述它「像植物一樣長大」的表現方式。
    /// </summary>
    public class ModExtension_SecondaryGestation : DefModExtension
    {
        /// <summary>二次發育完成後換成的東西（休眠艙）。</summary>
        public ThingDef resultDef;

        /// <summary>發育所需 tick。預設 300000＝5 天。</summary>
        public int gestationTicks = 300000;

        /// <summary>繪製尺寸倍率隨進度從 min 長到 max（會再乘上 graphicData.drawSize.x）。</summary>
        public FloatRange visualSizeRange = new FloatRange(0.3f, 1f);

        /// <summary>true＝底部貼地往上長；false＝以格子中心等比放大。</summary>
        public bool anchorBottom = true;

        /// <summary>是否吃 CutoutPlant 著色器的風吹搖曳。貼圖 shaderType 必須是 CutoutPlant。</summary>
        public bool windSway = true;

        /// <summary>擺放時的隨機水平偏移量，讓多具並排時不會排得像貨櫃。</summary>
        public float randomPositionJitter = 0.06f;

        /// <summary>重繪的量化階數：進度每跨過 1/steps 才重刷一次地圖網格。</summary>
        public int visualGrowthSteps = 24;

        /// <summary>落成時是否直接歸玩家所有（要能被卸除搬運就需要）。</summary>
        public bool claimByPlayerOnSpawn = true;

        [MustTranslate] public string completedLetterLabel;
        [MustTranslate] public string completedLetterText;
    }

    /// <summary>
    /// 二次發育體。胚胎足月後先落成這具外骨骼結晶艙，
    /// 再花一段時間把機體長完整，之後才轉為可被機械師接管的休眠艙。
    ///
    /// 刻意不需要電力、燃料或人力：它已經是自持系統，玩家能做的只有把它搬到安全的地方保護好。
    /// 被打壞就整具報廢（killedLeavings 出廢料），不會退回胚胎。
    ///
    /// 表現上刻意仿植物：印進地圖網格（Print）、隨進度變大、並用 CutoutPlant
    /// 著色器的頂點色 alpha 觸發風吹搖曳，讓它看起來是長出來的而不是蓋出來的。
    /// </summary>
    [StaticConstructorOnStartup]
    public class Building_SecondaryGestation : Building
    {
        private static readonly Material BarFilledMat =
            SolidColorMaterials.NewSolidColorMaterial(new Color(0.32f, 0.72f, 0.55f), ShaderDatabase.MetaOverlay);

        private static readonly Material BarUnfilledMat =
            SolidColorMaterials.NewSolidColorMaterial(new Color(0.12f, 0.12f, 0.12f), ShaderDatabase.MetaOverlay);

        /// <summary>Printer_Plane 的頂點色暫存。四個頂點的 alpha 就是 CutoutPlant 的搖曳權重。</summary>
        private static readonly Color32[] WorkingColors = new Color32[4];

        /// <summary>絕對完成時點。存絕對值，這樣讀檔與時間加速都不會失準。</summary>
        private int finishTick = -1;

        /// <summary>上次印進網格時的尺寸階數，用來判斷何時該重刷。</summary>
        private int lastDrawnGrowthStep = -1;

        private ModExtension_SecondaryGestation Ext => def.GetModExtension<ModExtension_SecondaryGestation>();

        private int TotalTicks => Ext?.gestationTicks ?? 300000;

        public int TicksRemaining => Mathf.Max(0, finishTick - Find.TickManager.TicksGame);

        public float ProgressPct
        {
            get
            {
                int total = TotalTicks;
                if (total <= 0)
                {
                    return 1f;
                }
                return Mathf.Clamp01(1f - (float)TicksRemaining / total);
            }
        }

        /// <summary>目前該畫多大。未落地（迷你化中）時直接用最終尺寸，避免背包裡的圖忽大忽小。</summary>
        private float VisualSize
        {
            get
            {
                ModExtension_SecondaryGestation ext = Ext;
                FloatRange range = ext?.visualSizeRange ?? new FloatRange(0.3f, 1f);
                float baseSize = def.graphicData?.drawSize.x ?? 1f;
                return range.LerpThroughRange(ProgressPct) * baseSize;
            }
        }

        private int CurrentGrowthStep
        {
            get
            {
                int steps = Mathf.Max(1, Ext?.visualGrowthSteps ?? 24);
                return Mathf.RoundToInt(ProgressPct * steps);
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            if (!respawningAfterLoad && finishTick < 0)
            {
                finishTick = Find.TickManager.TicksGame + TotalTicks;
            }

            // 沒有派系的建築無法被指派「卸除」，玩家就搬不走它。
            if (Faction == null && (Ext?.claimByPlayerOnSpawn ?? true) && Faction.OfPlayer != null)
            {
                SetFaction(Faction.OfPlayer);
            }

            lastDrawnGrowthStep = CurrentGrowthStep;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref finishTick, "DMS_secondaryGestationFinishTick", -1);
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned || finishTick < 0)
            {
                return;
            }
            if (!this.IsHashIntervalTick(250))
            {
                return;
            }

            // 尺寸是印進地圖網格的，變大時要主動要求重刷，否則畫面不會動。
            int step = CurrentGrowthStep;
            if (step != lastDrawnGrowthStep)
            {
                lastDrawnGrowthStep = step;
                DirtyMapMesh(Map);
            }

            if (Find.TickManager.TicksGame >= finishTick)
            {
                FinishGestation();
            }
        }

        private void FinishGestation()
        {
            ModExtension_SecondaryGestation ext = Ext;
            if (ext?.resultDef == null)
            {
                Log.ErrorOnce($"[DMS] {def.defName} 缺少 ModExtension_SecondaryGestation.resultDef，二次發育無法完成。",
                    def.shortHash ^ 0x5EC0);
                finishTick = -1;
                return;
            }

            Map map = Map;
            IntVec3 pos = Position;
            Rot4 rot = Rotation;

            // Vanish：完成是轉化而不是破壞，不該掉 killedLeavings。
            Destroy(DestroyMode.Vanish);

            Thing result = GenSpawn.Spawn(ThingMaker.MakeThing(ext.resultDef), pos, map, rot, WipeMode.Vanish);

            if (!ext.completedLetterLabel.NullOrEmpty() && !ext.completedLetterText.NullOrEmpty())
            {
                Find.LetterStack.ReceiveLetter(
                    ext.completedLetterLabel.Formatted(result.Named("ITEM")),
                    ext.completedLetterText.Formatted(result.Named("ITEM")),
                    LetterDefOf.PositiveEvent,
                    new LookTargets(result));
            }
        }

        /// <summary>
        /// 仿 <see cref="Plant.Print"/>：印一張隨進度放大的平面，並把頂點色 alpha
        /// 設成上緣受風、下緣固定，交給 CutoutPlant 著色器做搖曳。
        /// </summary>
        public override void Print(SectionLayer layer)
        {
            ModExtension_SecondaryGestation ext = Ext;
            float size = VisualSize;

            Rand.PushState();
            Rand.Seed = Position.GetHashCode();

            Vector3 center = this.TrueCenter();
            float jitter = ext?.randomPositionJitter ?? 0.06f;
            if (jitter > 0f)
            {
                center += Gen.RandomHorizontalVector(jitter);
            }

            // 底部貼地：小的時候蹲在格子下緣，長大時往上頂，看起來像是從地面長出來。
            bool anchorBottom = ext?.anchorBottom ?? true;
            if (anchorBottom)
            {
                center.z = Position.z + size / 2f;
            }
            else if (center.z - size / 2f < Position.z)
            {
                center.z = Position.z + size / 2f;
            }

            bool flipUv = Rand.Bool;
            Material mat = Graphic.MatSingleFor(this);
            Graphic.TryGetTextureAtlasReplacementInfo(mat, def.category.ToAtlasGroup(), flipUv,
                vertexColors: false, out mat, out Vector2[] uvs, out Color32 _);

            // colors[1] / colors[2] 是上緣兩點，alpha 即受風程度；下緣兩點固定為 0。
            byte wind = (byte)((ext?.windSway ?? true) ? 255 : 0);
            WorkingColors[1].a = wind;
            WorkingColors[2].a = wind;
            WorkingColors[0].a = 0;
            WorkingColors[3].a = 0;

            Printer_Plane.PrintPlane(layer, center, new Vector2(size, size), mat, 0f, flipUv, uvs,
                WorkingColors, 0.1f, this.HashOffset() % 1024);

            if (def.graphicData?.shadowData != null)
            {
                Vector3 shadowCenter = center + def.graphicData.shadowData.offset * size;
                shadowCenter.y -= 0.03658537f;
                Printer_Shadow.PrintShadow(layer, shadowCenter, def.graphicData.shadowData.volume * size, Rot4.North);
            }

            Rand.PopState();
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            if (finishTick >= 0)
            {
                if (!text.NullOrEmpty())
                {
                    text += "\n";
                }
                text += "DMS.SecondaryGestationProgress".Translate(ProgressPct.ToStringPercent());
                text += "\n" + "DMS.SecondaryGestationRemaining".Translate(TicksRemaining.ToStringTicksToPeriod());
            }
            return text;
        }

        /// <summary>
        /// 進度條只在選取時畫。本體是印進地圖網格的（drawerType MapMeshOnly），
        /// 所以不能放在 DrawAt，否則不會被呼叫。
        /// </summary>
        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();

            if (finishTick < 0)
            {
                return;
            }

            GenDraw.FillableBarRequest request = default;
            request.center = DrawPos + new Vector3(0f, 0.1f, -0.55f);
            request.size = new Vector2(0.9f, 0.14f);
            request.fillPercent = ProgressPct;
            request.filledMat = BarFilledMat;
            request.unfilledMat = BarUnfilledMat;
            request.margin = 0.12f;
            request.rotation = Rot4.North;
            GenDraw.DrawFillableBar(request);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Finish gestation",
                    action = delegate { finishTick = Find.TickManager.TicksGame; }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Advance 1 day",
                    action = delegate
                    {
                        finishTick = Mathf.Max(Find.TickManager.TicksGame, finishTick - 60000);
                        if (Spawned)
                        {
                            lastDrawnGrowthStep = CurrentGrowthStep;
                            DirtyMapMesh(Map);
                        }
                    }
                };
            }
        }
    }
}
