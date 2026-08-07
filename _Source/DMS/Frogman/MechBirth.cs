using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在「發育導向基因」上：帶有這個基因的胚胎不會生出嬰兒，
    /// 而是在孕育完成的當下落成一具建築（蛙人流程中是二次發育體）。
    ///
    /// 與 Fortified.PregnancyOutcomeExtension（產出普通物品）的差別在於：
    /// 這裡產出的是建築，必須用 GenSpawn 精準落位，不能走 GenPlace 的掉落物流程。
    /// </summary>
    public class MechBirthExtension : DefModExtension, Fortified.IHiddenGeneSource
    {
        /// <summary>孕育成功時落成的建築。蛙人指向二次發育體，而非直接指向休眠艙。</summary>
        public ThingDef productDef;

        /// <summary>是否在開局的異種人編輯器裡隱藏這個基因（由框架的 GeneEditorVisibility 讀取）。</summary>
        public bool hideInGeneEditor = true;

        bool Fortified.IHiddenGeneSource.HideInGeneEditor => hideInGeneEditor;

        /// <summary>發育失敗時的殘骸物品，可留空。</summary>
        public ThingDef failureThingDef;

        public IntRange failureThingCount = IntRange.One;

        /// <summary>產出時灑落的污漬。</summary>
        public ThingDef filthDef;

        public IntRange filthCount = new IntRange(3, 5);

        /// <summary>代孕者是否會因為分娩而力竭。</summary>
        public bool exhaustBirther = true;

        [MustTranslate] public string letterLabel;
        [MustTranslate] public string letterText;
        [MustTranslate] public string failLetterLabel;
        [MustTranslate] public string failLetterText;
    }

    /// <summary>
    /// 攔截胚胎的出生結算。培育艙與人工移植（代孕）兩條路線最後都會走到
    /// PregnancyUtility.ApplyBirthOutcome，所以只需要在這一個點接管即可。
    /// </summary>
    [HarmonyPatch(typeof(PregnancyUtility), nameof(PregnancyUtility.ApplyBirthOutcome))]
    public static class Patch_ApplyBirthOutcome_MechBirth
    {
        [HarmonyPriority(Priority.High)]
        [HarmonyPrefix]
        public static bool Prefix(RitualOutcomePossibility outcome, List<GeneDef> genes,
            Pawn geneticMother, Thing birtherThing, bool preventLetter, ref Thing __result)
        {
            if (birtherThing == null)
            {
                return true;
            }

            // 先看呼叫端傳進來的基因，再退回去問母體／培育艙自己帶的基因組。
            // 不能只信 genes 參數：一旦它是 null（其他 mod 改寫呼叫、舊存檔遷移、
            // 非標準的分娩路徑），原版就會照常生出嬰兒並跳出命名信件。
            MechBirthExtension ext = FindExtension(genes) ?? FindExtension(GestatingGenesOf(birtherThing));

            if (ext == null || ext.productDef == null)
            {
                return true; // 不是機兵胚胎，交還原版與其他框架處理
            }

            Pawn birtherPawn = birtherThing as Pawn;
            Map map = birtherThing.MapHeld;
            IntVec3 cell = SpawnCellFor(birtherThing, map);

            if (birtherThing.Spawned)
            {
                EffecterDefOf.Birth.Spawn(birtherThing, birtherThing.Map);
            }

            if (ext.exhaustBirther && birtherPawn != null)
            {
                birtherPawn.health.AddHediff(HediffDefOf.PostpartumExhaustion);
            }

            if (map != null && ext.filthDef != null)
            {
                FilthMaker.TryMakeFilth(cell, map, ext.filthDef, ext.filthCount.RandomInRange);
            }

            // positivityIndex < 0 代表原版判定為死產：機兵胚胎同樣視為培養失敗。
            bool failed = outcome != null && outcome.positivityIndex < 0;

            Thing product = null;
            if (failed)
            {
                if (ext.failureThingDef != null)
                {
                    product = ThingMaker.MakeThing(ext.failureThingDef);
                    product.stackCount = Mathf.Max(1, ext.failureThingCount.RandomInRange);
                    if (map == null || !GenPlace.TryPlaceThing(product, cell, map, ThingPlaceMode.Near))
                    {
                        product = null;
                    }
                }
            }
            else if (map != null)
            {
                product = GenSpawn.Spawn(ThingMaker.MakeThing(ext.productDef), cell, map,
                    WipeMode.VanishOrMoveAside);
            }
            else
            {
                Log.Warning("[DMS] 機兵胚胎孕育完成，但 " + birtherThing.ToStringSafe() +
                            " 不在任何地圖上，無法落成二次發育體。");
            }

            if (!preventLetter)
            {
                SendLetter(ext, failed, birtherPawn ?? geneticMother, birtherThing, product);
            }

            __result = null; // 必須回傳 null，原版呼叫端會把非 null 結果當成 Pawn 使用
            return false;
        }

        private static MechBirthExtension FindExtension(List<GeneDef> genes)
        {
            if (genes.NullOrEmpty())
            {
                return null;
            }
            for (int i = 0; i < genes.Count; i++)
            {
                MechBirthExtension ext = genes[i]?.GetModExtension<MechBirthExtension>();
                if (ext != null)
                {
                    return ext;
                }
            }
            return null;
        }

        /// <summary>
        /// 直接向孕育中的來源要基因組，不依賴呼叫端傳了什麼。
        /// 代孕者身上的 PregnantHuman / PregnancyLabor / PregnancyLaborPushing 都是
        /// HediffWithParents，基因組會沿著這三個階段一路複製下去；培育艙則問它選中的胚胎。
        /// </summary>
        private static List<GeneDef> GestatingGenesOf(Thing birtherThing)
        {
            if (birtherThing is Building_GrowthVat vat)
            {
                return vat.selectedEmbryo?.GeneSet?.GenesListForReading;
            }

            if (birtherThing is Pawn pawn && pawn.health?.hediffSet != null)
            {
                List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    if (hediffs[i] is HediffWithParents withParents && withParents.geneSet != null)
                    {
                        return withParents.geneSet.GenesListForReading;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 決定二次發育體的落點。優先用培育艙／代孕者旁邊的互動格，
        /// 但它是建築，必須落在沒有其他建築、走得到的格子上。
        /// </summary>
        private static IntVec3 SpawnCellFor(Thing birtherThing, Map map)
        {
            IntVec3 preferred = birtherThing.def.hasInteractionCell
                ? birtherThing.InteractionCell
                : birtherThing.PositionHeld;

            if (map == null)
            {
                return preferred;
            }

            if (IsUsable(preferred, map))
            {
                return preferred;
            }

            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(preferred, 4f, useCenter: false))
            {
                if (IsUsable(candidate, map))
                {
                    return candidate;
                }
            }

            return preferred;
        }

        /// <summary>產物是建築，落點不能疊在其他建築上，也不能是不可站立的格子。</summary>
        private static bool IsUsable(IntVec3 cell, Map map)
        {
            return cell.InBounds(map)
                   && cell.Standable(map)
                   && cell.GetEdifice(map) == null;
        }

        private static void SendLetter(MechBirthExtension ext, bool failed, Pawn subject,
            Thing birtherThing, Thing product)
        {
            string label = failed ? ext.failLetterLabel : ext.letterLabel;
            string text = failed ? ext.failLetterText : ext.letterText;
            if (label.NullOrEmpty() || text.NullOrEmpty())
            {
                return;
            }

            // subject 在培育艙路線可能為 null（胚胎沒有母體），退回用培育艙本身。
            Thing named = (Thing)subject ?? birtherThing;
            LookTargets targets = (product != null) ? new LookTargets(product) : new LookTargets(birtherThing);

            // label / text 標了 [MustTranslate]，值本身就是已翻譯的字串，不再走 Translate()。
            Find.LetterStack.ReceiveLetter(
                label.Formatted(named.Named("PAWN")),
                text.Formatted(named.Named("PAWN"), (product ?? birtherThing).Named("ITEM")),
                failed ? LetterDefOf.NegativeEvent : LetterDefOf.PositiveEvent,
                targets);
        }
    }
}
