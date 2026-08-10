using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 共用單一套穿著貼圖的服裝渲染節點。
    ///
    /// 原版走 ApparelGraphicRecordGetter.TryGetGraphicApparel，會把 wornGraphicPath
    /// 接上「_體型defName」（Apparel_Male_south…），因此每件衣服都得備齊
    /// Male / Female / Thin / Fat / Hulk 五套貼圖。
    ///
    /// 這個節點改成直接吃 renderNodeProperties 的 texPath，不做任何體型後綴，
    /// 所有體型（含 Biotech 幼年體型與外部種族追加的體型）共用同一組
    /// Apparel_north / south / east。
    ///
    /// 使用方式：ThingDef 的 apparel 區塊「不要」填 wornGraphicPath
    /// （否則原版仍會額外建立一個找不到貼圖的節點），改填 renderNodeProperties。
    /// </summary>
    public class PawnRenderNode_ApparelUniform : PawnRenderNode_Apparel
    {
        public PawnRenderNode_ApparelUniform(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public PawnRenderNode_ApparelUniform(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree, Apparel apparel)
            : base(pawn, props, tree, apparel)
        {
        }

        protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
        {
            if (!HasGraphic(pawn))
            {
                yield break;
            }

            Graphic graphic = GraphicFor(pawn);
            if (graphic != null)
            {
                yield return graphic;
            }
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            string path = TexPathFor(pawn);
            if (path.NullOrEmpty())
            {
                return null;
            }

            // 沒有掛上 apparel 時退回原版行為，避免任何邊界情況直接炸掉渲染樹。
            if (apparel == null)
            {
                return base.GraphicFor(pawn);
            }

            bool forStatue = pawn.Drawer.renderer.StatueColor.HasValue;
            Shader shader = ShaderDatabase.Cutout;
            if (!forStatue)
            {
                if (Props.shaderTypeDef?.Shader != null)
                {
                    shader = Props.shaderTypeDef.Shader;
                }
                else if (apparel.def.apparel.useWornGraphicMask)
                {
                    shader = ShaderDatabase.CutoutComplex;
                }
            }

            Vector2 drawSize = apparel.def.graphicData?.drawSize ?? Vector2.one;
            return GraphicDatabase.Get<Graphic_Multi>(path, shader, drawSize, apparel.DrawColor);
        }
    }

    /// <summary>
    /// 搭配 PawnRenderNode_ApparelUniform 的身體層 worker。
    ///
    /// 原版的服裝節點是由 DynamicPawnRenderNodeSetup_Apparel 動態建立的，
    /// 它會把 baseLayer 算成「父節點 baseLayer + 同層服裝件數位移」。
    /// 由 renderNodeProperties 靜態宣告的節點拿不到那個計算結果，
    /// 若在 XML 裡寫死絕對圖層，一旦原版調整渲染樹就會錯位。
    ///
    /// 因此這裡改成執行期解析：XML 的 baseLayer 視為「相對父節點的位移」，
    /// 而 drawData 的旋轉覆寫值維持原版的絕對圖層語意
    /// （外套層朝北時要蓋在頭部前面，原版用的就是絕對值 88）。
    /// </summary>
    public class PawnRenderNodeWorker_ApparelUniform_Body : PawnRenderNodeWorker_Apparel_Body
    {
        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
        {
            // 與原版 PawnRenderNodeWorker_Apparel_Body 一致：頭部翻轉時以反向朝向計算圖層。
            if (parms.flipHead && node.Props.oppositeFacingLayerWhenFlipped)
            {
                PawnDrawParms flipped = parms;
                flipped.facing = parms.facing.Opposite;
                flipped.flipHead = false;
                return LayerFor(node, flipped);
            }

            float parentLayer = node.parent?.Props.baseLayer ?? 0f;
            float ownLayer = parentLayer + node.Props.baseLayer;

            DrawData drawData = node.Props.drawData;
            if (drawData != null)
            {
                ownLayer = drawData.LayerForRot(parms.facing, ownLayer);
            }

            return ownLayer + node.debugLayerOffset;
        }
    }
}
