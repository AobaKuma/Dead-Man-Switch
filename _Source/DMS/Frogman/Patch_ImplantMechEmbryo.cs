using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 原版 <see cref="Recipe_ImplantEmbryo"/> 用 <c>t.def == ThingDefOf.HumanEmbryo</c>
    /// 硬篩材料，機兵胚胎（自訂 ThingDef）會被判定成「沒有胚胎」而白做一場手術。
    ///
    /// 這裡只在材料裡出現 <see cref="MechEmbryo"/> 時接管，其餘情況原封不動交還原版。
    /// </summary>
    [HarmonyPatch(typeof(Recipe_ImplantEmbryo), nameof(Recipe_ImplantEmbryo.ApplyOnPawn))]
    public static class Patch_Recipe_ImplantEmbryo_ApplyOnPawn
    {
        [HarmonyPrefix]
        public static bool Prefix(Recipe_ImplantEmbryo __instance, Pawn pawn, BodyPartRecord part,
            Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            MechEmbryo embryo = ingredients?.FirstOrDefault(t => t is MechEmbryo) as MechEmbryo;
            if (embryo == null)
            {
                return true; // 不是機兵胚胎，走原版流程
            }

            if (!ModsConfig.BiotechActive)
            {
                return false;
            }

            if (__instance.CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
            {
                return false;
            }

            if (Rand.Chance(PregnancyUtility.PregnancyChanceImplantEmbryo(pawn)))
            {
                Hediff_Pregnant hediff = (Hediff_Pregnant)HediffMaker.MakeHediff(HediffDefOf.PregnantHuman, pawn);
                hediff.SetParents(embryo.Mother, embryo.Father, embryo.GeneSet);
                pawn.health.AddHediff(hediff);
            }
            else
            {
                Messages.Message("ImplantFailedMessage".Translate(embryo.Label, pawn), pawn,
                    MessageTypeDefOf.NegativeHealthEvent);
            }

            return false;
        }
    }
}
