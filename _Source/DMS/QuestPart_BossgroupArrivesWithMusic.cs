using Verse;
using RimWorld;
using LudeonTK;
using System.Collections.Generic;

namespace DMS
{
    public class QuestPart_BossgroupArrivesWithMusic : QuestPart_BossgroupArrives
    {
        public SongDef song;
        protected override void Complete(SignalArgs signalArgs)
        {
            base.Complete(signalArgs);
            if (song != null) Find.MusicManagerPlay.ForcePlaySong(song, false);
            foreach (var q in quest.PartsListForReading)
            {
                if (q is QuestPart_Bossgroup questPart_Bossgroup)
                {
                    questPart_Bossgroup.bosses.Clear();
                }
            }
        }
        [DebugAction("DMS", "Flush quest bosses", false, false, false, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 1000)]
        public static void DebugInfo()
        {
            List<Quest> questsListForReading = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < questsListForReading.Count; i++)
            {
                if (questsListForReading[i].State != QuestState.Ongoing)
                {
                    continue;
                }
                List<QuestPart> partsListForReading = questsListForReading[i].PartsListForReading;
                for (int j = 0; j < partsListForReading.Count; j++)
                {
                    if (partsListForReading[j] is QuestPart_Bossgroup questPart_Bossgroup)
                    {
                        questPart_Bossgroup.bosses.Clear();
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref song, "song");
        }
    }
}