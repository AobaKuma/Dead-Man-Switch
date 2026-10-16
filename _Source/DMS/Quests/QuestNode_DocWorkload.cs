using RimWorld;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace DMS
{
    // 依難度點數決定挑戰等級(星數),並算出每份文書的工作量:總工作量 = 星數 × workPerStar
    public class QuestNode_DocWorkload : QuestNode
    {
        public SlateRef<int> documentCount;
        // 難度點數 → 挑戰等級
        public SimpleCurve pointsToChallengeRatingCurve;
        // 每顆星的總工作量(研究速度 1.0 的人員每 tick 累積 1 點)
        public float workPerStar = 90000f;

        [NoTranslate]
        public SlateRef<string> storeAs;

        protected override bool TestRunInt(Slate slate)
        {
            SetVars(slate);
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestGen.quest.challengeRating = SetVars(slate);
        }

        private int SetVars(Slate slate)
        {
            float points = slate.Get("points", 0f);
            int challengeRating = Mathf.Clamp(Mathf.RoundToInt(pointsToChallengeRatingCurve?.Evaluate(points) ?? 1f), 1, 3);
            slate.Set("challengeRating", challengeRating);
            int count = Mathf.Max(1, documentCount.GetValue(slate));
            slate.Set(storeAs.GetValue(slate), challengeRating * workPerStar / count);
            return challengeRating;
        }
    }

    // 覆寫單份文書的工作量,附帶隨機浮動
    public class QuestNode_SetDocWork : QuestNode
    {
        public SlateRef<Thing> thing;
        public SlateRef<float> workAmount;
        public FloatRange randomFactor = new FloatRange(0.8f, 1.2f);

        protected override bool TestRunInt(Slate slate)
        {
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            CompQuestWorkable comp = thing.GetValue(slate)?.TryGetComp<CompQuestWorkable>();
            comp?.SetWorkAmount(workAmount.GetValue(slate) * randomFactor.RandomInRange);
        }
    }
}
