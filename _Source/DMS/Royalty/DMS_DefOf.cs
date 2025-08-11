using RimWorld;
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
        public static PawnKindDef DMS_Officer_Ceremonist;
        public static PawnKindDef DMS_Escort;
        public static ThingDef DMS_Shuttle;
        public static TransportShipDef DMS_Ship_TransportShuttle;
    }
}