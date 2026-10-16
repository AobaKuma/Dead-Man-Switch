using System.Collections.Generic;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace DMS
{
    /// <summary>
    /// Like QuestNode_Letter, but picks the letter text randomly from a list of
    /// translation keys (Keyed). Each key may use {0} for the asker's full name.
    /// </summary>
    public class QuestNode_RandomLetter : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> inSignal;

        public LetterDef letterDef;

        [NoTranslate]
        public string labelKey;

        [NoTranslate]
        public List<string> textKeys = new List<string>();

        protected override bool TestRunInt(Slate slate)
        {
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            if (labelKey.NullOrEmpty() || textKeys.NullOrEmpty())
            {
                Log.Error("[DMS] QuestNode_RandomLetter: labelKey or textKeys not set.");
                return;
            }

            string askerName = slate.Get<Pawn>("asker")?.Name?.ToStringFull ?? "";
            TaggedString label = labelKey.Translate();
            TaggedString text = textKeys.RandomElement().Translate(askerName);

            ChoiceLetter letter = LetterMaker.MakeLetter(label, text,
                letterDef ?? LetterDefOf.NeutralEvent, null, null, QuestGen.quest);

            QuestPart_Letter part = new QuestPart_Letter
            {
                letter = letter,
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate))
                           ?? slate.Get<string>("inSignal")
            };
            QuestGen.quest.AddPart(part);
        }
    }
}
