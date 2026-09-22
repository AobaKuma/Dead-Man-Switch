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
        // 軍事法庭:封存科技隱匿級的永久敵對只能靠它解除(見 OccultechSanctionUtility)。
        public static QuestScriptDef DMS_CourtMartial;
		public static PawnKindDef DMS_Officer_Ceremonist;
        public static PawnKindDef DMS_Escort;
        public static ThingDef DMS_Shuttle;
        public static ThingDef DMS_OccultechKey;
		public static TransportShipDef DMS_Ship_TransportShuttle;
        public static JobDef DMS_ProcessQuestWorkable;
        // 集群織鳥的無人機投放動作(見 JobDriver_DeployDroneSwarm)
        public static JobDef DMS_DeployDroneSwarm;
        // 從發射器箱取出武器並裝備(見 JobDriver_TakeWeaponFromCase)
        public static JobDef DMS_TakeWeaponFromCase;
        public static RulePackDef DMS_QuestDocumentRules;

        // 軍法審判判決用(見 QuestPart_CourtVerdict)
        public static TaleDef DMS_Tale_CourtMartialed;
        public static TaleDef DMS_Tale_Acquitted;
        public static HistoryEventDef DMS_MemberCourtMartialed;

        // 封存科技制裁造成好感度變動時的理由(見 OccultechSanctionUtility)。
        public static HistoryEventDef DMS_OccultechResearched;

        // 機動載人升降艙降落在空白地塊時生成的臨時地圖世界物件(見 TransportersArrivalAction_LifterLanding / LifterLandingSite)
        public static WorldObjectDef DMS_LifterLandingSite;
    }
}
