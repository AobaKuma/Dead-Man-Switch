using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在胚胎 ThingDef 上：指定該胚胎在製造出來時就固定寫入的基因組。
    /// 機兵胚胎不繼承任何父母基因，基因組完全由配方決定。
    /// </summary>
    public class MechEmbryoExtension : DefModExtension
    {
        /// <summary>製造時強制寫入的基因。</summary>
        public List<GeneDef> fixedGenes;

        /// <summary>基因組顯示名稱（留空則由遊戲自動生成）。</summary>
        public string geneSetName;
    }

    /// <summary>
    /// 人造機兵胚胎。
    ///
    /// 刻意繼承 <see cref="HumanEmbryo"/>：這樣就能直接沿用原版胚胎的兩條發育路線
    /// ——人工移植（Command_Action → RecipeDefOf.ImplantEmbryo）與培育艙
    /// （Building_GrowthVat.SelectEmbryo，其型別為 HumanEmbryo）——不必自己重寫
    /// 一整套 UI 與工作流程。
    ///
    /// 與原版的差異只有一點：基因組在 PostMake 就被寫死，而不是從父母繼承。
    /// 發育完成時由 <see cref="Patch_ApplyBirthOutcome_MechBirth"/> 攔截產出結果。
    /// </summary>
    public class MechEmbryo : HumanEmbryo
    {
        public override void PostMake()
        {
            base.PostMake();
            EnsureFixedGenes();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureFixedGenes();
            }
        }

        /// <summary>
        /// 補齊 ModExtension 指定的固定基因。可重入，不會重複加入同一個基因。
        /// </summary>
        private void EnsureFixedGenes()
        {
            if (Destroyed)
            {
                return;
            }

            MechEmbryoExtension ext = def.GetModExtension<MechEmbryoExtension>();
            if (ext == null || ext.fixedGenes.NullOrEmpty())
            {
                return;
            }

            if (geneSet == null)
            {
                geneSet = new GeneSet();
            }

            List<GeneDef> current = geneSet.GenesListForReading;
            foreach (GeneDef gene in ext.fixedGenes)
            {
                if (gene != null && !current.Contains(gene))
                {
                    geneSet.AddGene(gene);
                }
            }

            if (!ext.geneSetName.NullOrEmpty())
            {
                geneSet.SetNameDirect(ext.geneSetName);
            }
        }
    }
}
