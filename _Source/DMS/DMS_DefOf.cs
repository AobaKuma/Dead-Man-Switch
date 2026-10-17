using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

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
        // 撞擊地點的密鑰伺服器：駭入才吐出密鑰(見 QuestPart_SteleKeySite)
        public static ThingDef DMS_OccultechPedestal;
        // 人工肌肉:模組選項可關閉其腐爛(見 Patch_CompRottable_Active)
        public static ThingDef Artifuscle;
		public static TransportShipDef DMS_Ship_TransportShuttle;
        public static JobDef DMS_ProcessQuestWorkable;
        // 集群織鳥的無人機投放動作(見 JobDriver_DeployDroneSwarm)
        public static JobDef DMS_DeployDroneSwarm;
        // 從發射器箱取出武器並裝備(見 JobDriver_TakeWeaponFromCase)
        public static JobDef DMS_TakeWeaponFromCase;
        // 設施增援的警報回應勤務：前往崗位、只打看得到的敵人(見 LordJob_AlarmResponse)
        public static DutyDef DMS_AlarmResponse;
        // 守墓人巡邏隊的勤務與站崗工作(見 LordJob_GravekeeperPatrol)
        public static DutyDef DMS_GravekeeperPatrol;
        public static DutyDef DMS_GravekeeperChallenge;
        public static DutyDef DMS_GravekeeperEngage;
        public static JobDef DMS_GravekeeperHold;
        // 守墓人盤查時寫進社交紀錄的對話(見 LordToil_GravekeeperPatrol.ChallengeTalk)
        public static InteractionDef DMS_GravekeeperChallengeTalk;
        public static RulePackDef DMS_QuestDocumentRules;

        // 軍法審判判決用(見 QuestPart_CourtVerdict)
        public static TaleDef DMS_Tale_CourtMartialed;
        public static TaleDef DMS_Tale_Acquitted;
        public static HistoryEventDef DMS_MemberCourtMartialed;

        // 封存科技制裁造成好感度變動時的理由(見 OccultechSanctionUtility)。
        public static HistoryEventDef DMS_OccultechResearched;

        // 機動載人升降艙降落在空白地塊時生成的臨時地圖世界物件(見 TransportersArrivalAction_LifterLanding / LifterLandingSite)
        public static WorldObjectDef DMS_LifterLandingSite;

        // 追殺期間派發的 SAGE 節點任務（見 1.6/Defs/Misc/DMS_Huntdowns.xml 的 questsOnWave）。
        public static QuestScriptDef DMS_FleetNetworkSite;

        // 現實穩定錨的錨場標記：靈能封鎖與強制現形都看它(見 RealityAnchorUtility.IsAnchored)。
        public static HediffDef DMS_RealityAnchored;

        // 脫序者接納任務與其追殺(見 Quests/Deviant/)。叛逃者開局的玩家派系不會收到這個任務。
        // The deviant refuge quest and its huntdown (see Quests/Deviant/). A deviant-start colony never gets the quest.
        public static QuestScriptDef DMS_DeviantRefuge;
        public static Fortified.HuntdownDef DMS_DeviantHunt;
        public static FactionDef DMS_Deviants;
        public static HistoryEventDef DMS_HarboredDeviant;
        // 引渡要求的選擇信件與交出時的好感度理由。The extradition choice letter and the goodwill reason for handing over.
        public static LetterDef DMS_DeviantExtradition;
        public static HistoryEventDef DMS_ExtraditedDeviant;
    }
}
