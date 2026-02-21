using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Grammar;

namespace DMS
{
    // 舰队文书任务物品组件
    public class CompQuestWorkable : ThingComp
    {
        public CompProperties_QuestWorkable Props => (CompProperties_QuestWorkable)props;

        // 处理进度
        private float progress;
        // 是否已完成
        private bool isCompleted;
        // 所需工作量
        private float workAmount = -1f;
        // 覆盖标签
        private string labelOverride;
        // 覆盖描述
        private string descriptionOverride;

        public float WorkAmount
        {
            get
            {
                if (workAmount < 0) workAmount = Props.workAmount;
                return workAmount;
            }
        }

        public float ProgressPercent => progress / WorkAmount;
        public bool IsCompleted => isCompleted;

        public override string TransformLabel(string label) => labelOverride ?? label;
        public override string GetDescriptionPart() => descriptionOverride;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref progress, "progress", 0f);
            Scribe_Values.Look(ref isCompleted, "isCompleted", false);
            Scribe_Values.Look(ref workAmount, "workAmount", -1f);
            Scribe_Values.Look(ref labelOverride, "labelOverride");
            Scribe_Values.Look(ref descriptionOverride, "descriptionOverride");
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            // 产生时初始化数据
            InitializeData();
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            // 初始化文书数据
            InitializeData();
        }

        // 初始化文书随机属性
        private void InitializeData()
        {
            if (workAmount >= 0 && labelOverride != null) return;

            // 生成唯一随机种子
            int seed = parent.thingIDNumber;
            if (seed <= 0)
            {
                seed = parent.GetHashCode() ^ Find.TickManager.TicksGame ^ parent.Position.GetHashCode();
            }

            Rand.PushState(seed ^ 741852963);
            try
            {
                // 随机设置工作量
                if (workAmount < 0)
                {
                    workAmount = Rand.Range(25f, 300f);
                }

                // 基于上下文生成名称与描述
                if (labelOverride == null && DMS_DefOf.DMS_QuestDocumentRules != null)
                {
                    // 随机逻辑分类
                    string[] contexts = new string[] { "supply", "combat", "personnel" };
                    string context = contexts.RandomElement();

                    GrammarRequest request = new GrammarRequest();
                    request.Includes.Add(DMS_DefOf.DMS_QuestDocumentRules);
                    // 注入逻辑上下文
                    request.Constants.Add("context", context);

                    // 生成随机文本
                    labelOverride = NameGenerator.GenerateName(request, rootKeyword: "docName");
                    descriptionOverride = NameGenerator.GenerateName(request, rootKeyword: "docDesc");
                }
            }
            finally
            {
                Rand.PopState();
            }
        }

        public void DoWork(float amount, Pawn worker)
        {
            if (isCompleted) return;

            progress += amount;
            if (progress >= WorkAmount)
            {
                // 完成工作
                progress = WorkAmount;
                ProcessCompleted(worker);
            }
        }

        private void ProcessCompleted(Pawn worker)
        {
            if (isCompleted) return;
            isCompleted = true;

            if (parent.questTags != null)
            {
                // 发送信号
                QuestUtility.SendQuestTargetSignals(parent.questTags, "Processed", parent.Named("SUBJECT"), worker.Named("ACTIVATOR"));
                parent.questTags.Clear();
            }
            // 立即消失，只增加任务进度
            parent.Destroy(DestroyMode.Vanish);
        }

        // 处理文书堆叠拆分数据丢失
        public override void PostSplitOff(Thing piece)
        {
            base.PostSplitOff(piece);

            // 同步任务标签
            if (parent.questTags != null && piece.questTags == null)
            {
                piece.questTags = new List<string>(parent.questTags);
            }

            // 同步随机属性
            CompQuestWorkable comp = piece.TryGetComp<CompQuestWorkable>();
            if (comp != null)
            {
                comp.workAmount = workAmount;
                comp.labelOverride = labelOverride;
                comp.descriptionOverride = descriptionOverride;
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            // 损毁时触发失败信号
            if (mode != DestroyMode.QuestLogic && parent.questTags != null && parent.questTags.Count > 0)
            {
                QuestUtility.SendQuestTargetSignals(parent.questTags, "Destroyed", parent.Named("SUBJECT"));
            }
        }

        // 阻止文书堆叠
        public override bool AllowStackWith(Thing other)
        {
            return false;
        }


        public override string CompInspectStringExtra()
        {
            if (isCompleted)
            {
                return "DMS_WorkableCompleted".Translate();
            }
            // 修正翻译键显示
            return "DMS_WorkableProgress".Translate() + ": " + progress.ToString("F0") + " / " + WorkAmount.ToString("F0");
        }
    }

    public class CompProperties_QuestWorkable : CompProperties
    {
        public float workAmount = 1000f; // 默认工作量

        public CompProperties_QuestWorkable()
        {
            compClass = typeof(CompQuestWorkable);
        }
    }
}
