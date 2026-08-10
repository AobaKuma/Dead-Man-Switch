using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 拆卸「需要手術才能脫下」的裝備。
    /// 設計取向：安全但耗時 —— 不做手術失敗判定、不造成傷害，裝備完好回收。
    /// 只要在 RecipeDef 中設定 targetsBodyPart = false 即可對整體施作。
    /// </summary>
    public class Recipe_RemoveSurgicalApparel : Recipe_Surgery
    {
        /// <summary>
        /// 找出這隻 pawn 身上、其解除配方指向本配方的裝備。
        /// 若裝備沒有指定 removalRecipe，則退回「任何手術裝備」。
        /// </summary>
        private Apparel FindTarget(Pawn pawn)
        {
            if (pawn?.apparel == null)
            {
                return null;
            }

            List<Apparel> worn = pawn.apparel.WornApparel;
            Apparel fallback = null;

            for (int i = 0; i < worn.Count; i++)
            {
                CompSurgicalApparel comp = worn[i].TryGetComp<CompSurgicalApparel>();
                if (comp == null)
                {
                    continue;
                }

                if (comp.Props.removalRecipe == recipe)
                {
                    return worn[i];
                }

                if (comp.Props.removalRecipe == null && fallback == null)
                {
                    fallback = worn[i];
                }
            }

            return fallback;
        }

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            return base.AvailableOnNow(thing, part) && FindTarget(thing as Pawn) != null;
        }

        public override bool CompletableEver(Pawn surgeryTarget)
        {
            return FindTarget(surgeryTarget) != null;
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            Apparel target = FindTarget(pawn);
            if (target == null)
            {
                return;
            }

            if (billDoer != null)
            {
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            // 先解鎖再脫下，避免 DropAll / 面板邏輯把它當成鎖定裝備。
            pawn.apparel.Unlock(target);

            IntVec3 dropPos = pawn.PositionHeld;
            if (!pawn.apparel.TryDrop(target, out Apparel _, dropPos, forbid: false))
            {
                // 極端情況（無地圖等）下退回移入物品欄，確保裝備不會憑空消失。
                pawn.apparel.TryMoveToInventory(target);
            }

            Messages.Message(
                "DMS_SurgicalApparel_Removed".Translate(pawn.LabelShortCap, target.LabelShortCap),
                pawn,
                MessageTypeDefOf.PositiveEvent,
                historical: false);
        }
    }
}
