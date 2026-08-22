using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    [DefOf]
    internal static class DMS_DefOf
    {
        static DMS_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DMS_DefOf));
        }
        public static FactionDef DMS_Army;
        public static QuestScriptDef DMS_PromotionCeremony;
        public static QuestScriptDef DMS_OfficerTraining;
        public static QuestScriptDef DMS_Stele;
		public static PawnKindDef DMS_Officer_Ceremonist;
        public static PawnKindDef DMS_Escort;
        public static ThingDef DMS_Shuttle;
        public static ThingDef DMS_OccultechKey;
		public static TransportShipDef DMS_Ship_TransportShuttle;
        public static JobDef DMS_ProcessQuestWorkable;
        public static RulePackDef DMS_QuestDocumentRules;

        // 軍法審判判決用(見 QuestPart_CourtVerdict)
        public static TaleDef DMS_Tale_CourtMartialed;
        public static TaleDef DMS_Tale_Acquitted;
        public static HistoryEventDef DMS_MemberCourtMartialed;
    }
}
