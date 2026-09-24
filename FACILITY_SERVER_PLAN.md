# 設施伺服主機群 / Facility Servers：執行計畫

> 目標：新增四座「電晶體電腦」建築（設施伺服核心、設施數據輔機、站點伺服主機、賢者 SAGE），
> 一套**可多階段駭入、每次開始駭入都拉警報**的機制，以及**摧毀 SAGE 後暫停殖民艦隊追殺一到兩年**的機制，
> 並配上一條只在追殺期間出現的站點任務線。
>
> 撰寫依據：`DMS2-Dev`（2026-09-24）。貼圖已在 `1.6/NewContent/Textures/Things/Building/AlertCaller/`。

---

## 實作狀態（2026-09-24）

已全部實作並通過編譯（DMS 與 FFF 皆 0 錯誤），**尚未進遊戲實測**。第 9、11.11、12.8、13.10 節的測試清單仍待執行。

**新增／修改的檔案**

| 範圍 | 檔案 |
|---|---|
| FFF：設施封鎖 | `_Fortified-Framework/_Sources/Fortified/StandaloneFunctions/AlertSystem/Lockdown/`（`MapComponent_FacilityLockdown`、`CompFacilityLockdownController`、`CompFacilityLockdownGate`、`JobDriver_LockdownOverride`、`FacilityLockdownUtility`）；`AlertEffects.cs`（舊版刪除，留 `WorldComponent_AlertLockdownDriver` 空殼）；`FFF_JobDefOf`、`1.6/Defs/JobDef.xml`；三語 `Keyed/AlertSystem.xml`、`DefInjected/JobDef/JobDef.xml` |
| DMS：伺服主機 | `_Source/DMS/FacilityServer/`（`CompServerHackable`、`ServerHackReward`、`FacilityAlarmUtility`、`CompHuntBreaker`）；`CompAlertEffector_CallBattleGroup.cs`（抽出 `BattleGroupCallUtility`）；`DMS.csproj`（6 條 Publicize） |
| DMS：追殺暫停 | `GameComponent_OccultechSanction.cs`、`OccultechSanctionUtility.cs`、`DebugActions_Occultech.cs`、`DMS_DefOf.cs`、`Quests/QuestNode_Root_DMS_FleetNetworkSite.cs` |
| DMS：毒氣釋放口 | `_Source/DMS/CompAlertEffector_GasVent.cs`（含 `MapComponent_DMSToxGasFallback`） |
| DMS：設施檔案庫 | `Vault/RoomContents_Archive.cs`、`Vault/FacilityLockdownPlacement.cs`；`RoomContents_VaultTreasury.cs`（`IsTreasuryWorker`）、`RoomContents_VaultMainHall.cs` + `ModExtension_VaultSecurity.cs`（毒氣口）、`DMS_LayoutWorker_Vault.cs`（放中控） |
| DMS：Def | `Things_Building/DMS_ExplorationBuildings_Server.xml`（新）、`DMS_ExplorationBuildings_Alert.xml`（毒氣口、封鎖中控）、`Vault/DMS_Archive.xml`（新）、`Vault/DMS_Vault_Portal.xml`（閘門 comp、`Name="DMS_ElevatorBase"`）、`Vault/DMS_Vault_Layout.xml`（儲存庫也放中控）、`Quests/DMS_FleetNetworkQuest.xml`（新）、`GenStructures/Site_FleetNetwork.xml`（新，SAGE 節點版面）、`GenStructures/Site_LegacyInstallations.xml`（六座設施各加站點伺服主機；荒廢站點加檔案庫電梯） |
| DMS：文本 | 三語 `Keyed/DMS_FacilityServer.xml`、`DefInjected/{ThingDef/DMS_FacilityServer,MapGeneratorDef/DMS_Archive,SitePartDef/DMS_FleetNetwork,QuestScriptDef/DMS_FleetNetwork}.xml`；六座地表設施的任務描述補句（Def 與三語 DefInjected）；`OCCULTECH.md` |

**與本計畫的差異**

- 伺服主機不設 `autohackWarningString`：原版把它當成原文而非翻譯鍵，改由檢視字串 `DMS_ServerHack_AlarmWarning` 提示。
- 封鎖不需要 `CancelEnterJobs` 與 Harmony：原版 `JobDriver_EnterPortal` 本身就 `FailOn(!IsEnterable)`；`Site.ShouldRemoveMapNow` 在口袋地圖內有玩家 pawn 時會保留地表地圖，所以 §13.4 的 `MapDeiniter` prefix 不必做，寬限流程自然成立。
- 毒氣口的噴發特效直接用 Core 的 `ToxGasReleasing`（含嘶聲），不另做 EffecterDef。
- 封鎖中控暫用變電箱貼圖（`Things/Building/DistributionBox/building`），XML 內有 TODO。
- 軍事儲存庫也放了封鎖中控（偏好指揮室、動力室）。
- 站點伺服主機在六座既有版面的位置由腳本挑選（有屋頂、周圍 2~3 格內無其他物件）；荒廢站點的檔案庫電梯放在 `(74, 0, 9)`。進遊戲時請逐一確認位置合理。
- SAGE 節點版面為手寫（29×23），未經遊戲內工具匯出。
- `FFF_Alert_PawnMissing_*` 兩個鍵隨舊版一併刪除（只有舊版使用）。

## 目錄

1. [總覽](#1-總覽)
2. [現有系統對照（這份計畫會接上哪些東西）](#2-現有系統對照)
3. [設計決策](#3-設計決策)
4. [C# 實作](#4-c-實作)
5. [XML Def](#5-xml-def)
6. [站點任務](#6-站點任務)
7. [文本（EN / 繁中）](#7-文本)
8. [執行順序與檢查清單](#8-執行順序與檢查清單)
9. [測試計畫](#9-測試計畫)
10. [設計決策紀錄](#10-設計決策紀錄)
11. [地下設施：設施檔案庫](#11-地下設施設施檔案庫)
12. [警報建築：毒氣釋放口](#12-警報建築毒氣釋放口)
13. [警報建築：設施封鎖中控](#13-警報建築設施封鎖中控)

---

## 1. 總覽

| defName | 標籤 | 佔地 | drawSize | 旋轉 | 貼圖 | 駭入次數 | 獎勵池 | 摧毀效果 |
|---|---|---|---|---|---|---|---|---|
| `DMS_FacilityServerCore` | 設施伺服核心 | 2×2 | 4×4 | 否 | `AlertCaller/FacilityServerCore`（Single） | **3~4** | 科技藍圖／封存技術匣／訪問密鑰 | 無 |
| `DMS_FacilityDataRack` | 設施數據輔機 | 2×1 | 3×3 | 是 | `AlertCaller/FacilityDataRack`（Multi，缺 east → 用 west 翻轉） | — | — | 無（純裝飾） |
| `DMS_SiteServerCore` | 站點伺服主機 | 1×1 | 2×2 | 否 | `AlertCaller/SiteServerCore`（Single） | **1** | 科技藍圖／訪問密鑰（**不含**技術匣） | 無 |
| `DMS_SageCore` | 賢者 SAGE | 3×1 | 5×3 | 是 | `AlertCaller/Sage`（Multi，缺 west → 用 east 翻轉） | **2~3** | 科技藍圖／封存技術匣／訪問密鑰 | **追殺暫停 1~2 年** |

**駭入流程（三台共用）**

```
殖民者開始駭入（每一次新的駭入作業）
   └─► 設施警報：MapComponent_AlertCounter.Notify(+N) ＋ 廣播 FFF_AlertScanner_Triggered
         └─► 地圖上的 FPV 機庫／戰鬥群呼叫器／砲擊呼叫器照常反應
         └─► 地圖上沒有任何警報反應建築 → 直接叫 DMS_RaidWave_FacilityQRF（保底）
   └─► 進度滿 → 本階段完成：掉一件獎勵、工作結束
         └─► 還有剩餘階段 → 無人佔用後重置進度，下一階段防禦值提高
         └─► 最後一階段 → 永久標記為已駭入（螢幕熄滅）
中途打斷 → 進度保留，但下一次開始駭入會**再拉一次警報**
```

---

## 2. 現有系統對照

| 需求 | 接到哪裡 | 位置 |
|---|---|---|
| 駭入工作、進度條、自動駭入、鎖定、Gizmo、右鍵選單 | 原版 `CompHackable` + `JobDriver_Hack`（`JobDefOf.Hack`） | Assembly-CSharp |
| 繼承 `CompHackable` 的先例 | `CompVaultConsole` | `_Source/DMS/Vault/CompVaultConsole.cs` |
| 警報值與效果 | FFF `MapComponent_AlertCounter.Notify(float)` | `_Fortified-Framework/.../AlertSystem/MapComponent_AlertCounter.cs` |
| 警報訊號格式 | `FFF_AlertScanner_Triggered`，參數 `SUBJECT` / `POSITION` / `MAP`（`CompAlertScanner.FireSignal`） | `CompAlertScanner.cs:466` |
| 警報反應建築只收同圖訊號 | `signal.IsForParent(parent)` 看 `MAP` 參數 | `SignalMapUtility.cs` |
| 保底援軍 | `DMS_RaidWave_FacilityQRF` 與 `CompAlertEffector_CallBattleGroup.DoEffect` 的呼叫邏輯 | `_Source/DMS/CompAlertEffector_CallBattleGroup.cs` |
| 追殺 | `GameComponent_OccultechSanction.permanentHostile` / `nextHuntTick` | `_Source/DMS/Occultech/GameComponent_OccultechSanction.cs` |
| 追殺參數 | `OccultechSanctionUtility.OccultedSanction.huntIntervalDays` | `_Source/DMS/Occultech/OccultechSanctionUtility.cs` |
| 訪問密鑰 | `DMS_AccessKey_Nara`（奈良重工）、`DMS_AccessKey_Neutrals`（燈塔先鋒） | `1.6/NewContent/Defs/Things_Item/DMS_Item_AccessCards.xml` |
| 封存技術匣 | `DMS_DataCache_Restricted` / `_Sealed` / `_Occulted` | `1.6/NewContent/Defs/Things_Item/DMS_Item_Occultech.xml` |
| 科技藍圖 | 原版 techprint（`ResearchProjectDef.Techprint`，**需要 Royalty**） | — |
| 站點任務骨架 | `QuestNode_Root_DMS_RandomInstallation`（繼承 FFF `QuestNode_Root_FFF_TimedHostileSite`） | `_Source/DMS/Quests/QuestNode_Root_DMS_RandomInstallation.cs` |
| 站點生成 | `Fortified.SitePartWorker_FFF_TimedHostileSite` + `GenStep_FFFGarrisonedStructure` | `1.6/NewContent/Defs/Quests/DMS_LegacySite_*.xml` |

---

## 3. 設計決策

### 3.1 繼承 `CompHackable`，不自寫 JobDriver

`CompServerHackable : CompHackable`。原版的駭入工作、`HackingSpeed`、`HackingStealth` 鎖定、自動駭入切換、進度條、音效、右鍵選單、機械師遠端駭入全部可以直接沿用；**只多做兩件事**：

1. **偵測「一次新的駭入作業」並拉警報**
2. **完成一個階段後重置，進入下一階段**

原版 `CompHackable` 的 `progress` / `hacked` / `lastHackTick` 等是 private，需要在 `DMS.csproj` 補 Publicize（專案已經用 Krafs.Publicizer 對 `ResearchManager.progress` 做過同樣的事）。

### 3.2 如何判定「開始駭入」：輪詢 `lastHackTick`，不用 Harmony

`JobDriver_Hack` 每 tick 呼叫 `CompHackable.Hack()`，而 `Hack()` 會把 `lastHackTick` 寫成當下 tick。
在 `CompTick` 裡比對：

- `lastHackTick == now`（這一 tick 有人在駭）
- 且 **距離上一次看到駭入超過 `SessionGapTicks`（60）** 或**換了駭客**

→ 視為新的一次駭入作業，拉警報。

理由：

- 原版的 `hackingStartedSignal` 只在 `lastHackTick < 0` 時送一次，不能用於「每次都觸發」。
- `JobDriver.Notify_Starting` 會在殖民者**開始走路**時就觸發，玩家下令後改主意也會拉警報，太早。
- 60 tick 的容忍避免建築 tick 與 pawn tick 先後順序造成的誤判；遊戲暫停時 `TicksGame` 不動，所以暫停不會被當成中斷。
- 伺服器建築本來就是 `tickerType Normal`，每 tick 一次整數比較可以忽略。

### 3.3 階段轉換：延遲到「沒人佔用」才重置

`JobDriver_Hack` 的工作 toil 帶 `FailOn(() => IsHacked)`。如果在 `OnHacked` 裡**當場**把 `hacked` 設回 false，殖民者會無縫繼續駭下一階段，**跳過第二次警報**。

因此：

1. `OnHacked` → 發獎勵、`pendingNextStage = true`，**維持 `hacked = true`**，讓工作自然結束（原版還會播「駭入完成」音效）。
2. `CompTick` 看到 `pendingNextStage` 且 `parent` 沒有被任何人預約（`Map.reservationManager.IsReservedByAnyoneOf(parent, Faction.OfPlayer)` 為 false）→ 重置進度、提高防禦值、階段 +1。

這樣「下一階段」一定需要一份新的駭入工作，也就一定會再拉一次警報，符合「每次開始駭入就會觸發警報」。

### 3.4 追殺暫停

「追殺」＝隱匿級制裁的 `permanentHostile` 週期襲擊（`GameComponent_OccultechSanction.GameComponentTick`）。
**只有 SAGE** 會影響追殺；摧毀站點伺服主機與設施伺服核心不影響追殺。摧毀 SAGE：

| 狀態 | 效果 |
|---|---|
| 永久敵對中、未暫停 | 追殺暫停 `1~2` 年（RimWorld 一年 = 60 天，即 60~120 天），發信 |
| 永久敵對中、已暫停 | 每座 **+15 天**，疊加在目前結束時間之後（`stackDaysWhileSuspended`）；發簡短訊息，附上新的剩餘時間 |
| 未處於永久敵對 | 無效果，只發一則中性訊息（「網路中斷，但艦隊目前並未追緝你」） |

暫停結束 → 發信「SAGE 網路已由另一台主機接手」，重新排程下一次追殺（`huntIntervalDays` 之後），**並補一次** `EnsureCourtMartialOffered()`（暫停期間不會觸發追殺，也就不會補發傳票）。

其他規則：

- **暫停不等於解除**：永久敵對、好感度封鎖、連坐全部照舊；軍事法庭仍是唯一出路。
- `EndPermanentHostility()`（軍事法庭服刑期滿）同時清掉暫停狀態。
- 暫停期間又完成一項隱匿級研究：制裁照常結算（好感度、連坐等），但**暫停不受影響**。`BeginPermanentHostility` 在暫停中只設旗標、不重新排程追殺，恢復時才由暫停結束流程排程。
- 建築**不可拆除**（`deconstructible false`），只能打爛。計入的摧毀方式：`DestroyMode.KillFinalize`、`KillFinalizeLeavingsOnly`。不計：`Vanish`、`WillReplace`、`QuestLogic`、`Refund`、`FailConstruction`、`Deconstruct`。
- 已駭完才摧毀也算，這是預期玩法（先榨乾再炸掉）。

### 3.5 獎勵

每完成一階段，從該建築的 `rewards` 表依權重抽一項，掉在建築旁：

| 類型 `kind` | 內容 | 無 Royalty 時 |
|---|---|---|
| `Techprint` | 從 **DMS 自己的** 研究專案中抽一個：`techprintCount > 0`、未完成、藍圖未收滿；DMS 抽不到才放寬到所有模組 | 改給 `fallbackThing`（預設 `DMS_DataCache_Restricted`） |
| `Thing` | 固定 `thingDef`（例：`DMS_DataCache_Sealed`） | 同左 |
| `AccessKey` | `DMS_AccessKey_Nara` / `DMS_AccessKey_Neutrals` 依 `keyWeights` 抽 | 同左 |

每個選項可設 `maxPerBuilding`，避免四次都抽到同一種（例如技術匣上限 2）。抽不到任何合格選項時退回 `fallbackThing`。

> 技術匣的等級：需求寫「封存技術匣」，對應 `DMS_DataCache_Sealed`。SAGE 另外給一個低權重的 `DMS_DataCache_Occulted`（見 §10）。

### 3.6 警報強度

| 建築 | `alertIncrement` | 說明 |
|---|---|---|
| 設施伺服核心 | 35 | 3 次駭入即填滿警戒值（100）並觸發 FFF 的全圖警報效果 |
| 站點伺服主機 | 40 | 單次駭入，但小站點通常反應建築少 |
| SAGE | 50 | 兩次駭入即滿 |

保底：`fallbackRaidWave`。拉警報時若地圖上**沒有任何** `CompAlertEffector` 在聽 `FFF_AlertScanner_Triggered`，就直接呼叫 RaidWave（沿用戰鬥群呼叫器的邏輯），確保「一定有警報響應」。

### 3.7 站點歸屬

站點的守軍與建築派系用 **`DMS_Legacy`（先鋒殘餘）**，不是 `DMS_Army`：

- 設定上 SAGE 是殖民艦隊所**使用**的網路，由遺留機兵看守，與既有的 Legacy 設施一致；
- 若用 `DMS_Army`，軍事法庭休戰期間站點會變成非敵對，任務與戰鬥 AI 都會出狀況。

---

## 4. C# 實作

新資料夾 `_Source/DMS/FacilityServer/`，命名空間 `DMS`。

### 4.1 `DMS.csproj`：Publicize

```xml
<Publicize Include="Assembly-CSharp:RimWorld.CompHackable.progress" />
<Publicize Include="Assembly-CSharp:RimWorld.CompHackable.hacked" />
<Publicize Include="Assembly-CSharp:RimWorld.CompHackable.lastHackTick" />
<Publicize Include="Assembly-CSharp:RimWorld.CompHackable.lastUser" />
<Publicize Include="Assembly-CSharp:RimWorld.CompHackable.progressLastLockout" />
<Publicize Include="Assembly-CSharp:RimWorld.CompHackable.sentLetter" />
```

### 4.2 `ServerHackReward.cs`：獎勵選項

```csharp
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    public enum ServerHackRewardKind { Thing, Techprint, AccessKey }

    /// <summary>
    /// 伺服器每完成一階段駭入時抽的一個獎勵選項。
    /// One reward option a server rolls each time a hack stage completes.
    /// </summary>
    public class ServerHackReward
    {
        public ServerHackRewardKind kind = ServerHackRewardKind.Thing;
        public float weight = 1f;

        /// <summary>kind == Thing 時的物品。The item when kind == Thing.</summary>
        public ThingDef thingDef;
        public IntRange count = IntRange.One;

        /// <summary>同一座建築最多給幾次，0 = 不限。Times this option may pay out per building; 0 = unlimited.</summary>
        public int maxPerBuilding;

        /// <summary>kind == AccessKey 時的密鑰權重。Key weights when kind == AccessKey.</summary>
        public List<ThingDefCountClass> keyWeights;   // count 欄位當權重用 / count is used as the weight

        [NoTranslate] public string label;            // 除錯用 / debug only
    }
}
```

### 4.3 `ServerHackRewardUtility.cs`

```csharp
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    public static class ServerHackRewardUtility
    {
        /// <summary>
        /// 產生一件獎勵；Techprint 在無 Royalty 或抽不到專案時回傳 null，由呼叫端退回 fallback。
        /// Makes one reward; Techprint yields null without Royalty or when no project qualifies,
        /// and the caller falls back.
        /// </summary>
        public static Thing Make(ServerHackReward option)
        {
            switch (option.kind)
            {
                case ServerHackRewardKind.Techprint:
                    ResearchProjectDef proj = RandomTechprintProject();
                    if (proj?.Techprint == null) return null;
                    return ThingMaker.MakeThing(proj.Techprint);

                case ServerHackRewardKind.AccessKey:
                    if (option.keyWeights.NullOrEmpty()) return null;
                    ThingDefCountClass key = option.keyWeights
                        .Where(k => k.thingDef != null && k.count > 0)
                        .RandomElementByWeightWithFallback(k => k.count);
                    return key == null ? null : MakeStack(key.thingDef, IntRange.One);

                default:
                    return option.thingDef == null ? null : MakeStack(option.thingDef, option.count);
            }
        }

        private static Thing MakeStack(ThingDef def, IntRange count)
        {
            Thing t = ThingMaker.MakeThing(def);
            t.stackCount = UnityEngine.Mathf.Clamp(count.RandomInRange, 1, def.stackLimit);
            return t;
        }

        /// <summary>
        /// 先抽 DMS 自己的專案，抽不到才放寬到全部。沿用原版「未完成且藍圖未收滿」的條件。
        /// DMS projects first, then any mod. Same rule as vanilla: unfinished, techprints not complete.
        /// </summary>
        public static ResearchProjectDef RandomTechprintProject()
        {
            if (!ModsConfig.RoyaltyActive) return null;

            IEnumerable<ResearchProjectDef> open = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => p.TechprintCount > 0 && !p.IsFinished && !p.TechprintRequirementMet
                            && p.Techprint != null);

            ModContentPack dms = DMS_Mod.Content;   // 見 §4.8；或用 LoadedModManager 以 packageId 找
            if (open.Where(p => p.modContentPack == dms).TryRandomElement(out ResearchProjectDef own))
                return own;
            return open.TryRandomElement(out ResearchProjectDef any) ? any : null;
        }
    }
}
```

> 動工時用 decompiler 確認 1.6 的欄位名：`TechprintCount` / `TechprintRequirementMet` / `Techprint`（`TechprintsApplied` 由 `ResearchManager.GetTechprints` 提供）。

### 4.4 `CompServerHackable.cs`：核心

```csharp
using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DMS
{
    public class CompProperties_ServerHackable : CompProperties_Hackable
    {
        /// <summary>可駭入的次數，生成時擲一次。Number of hack stages, rolled once on spawn.</summary>
        public IntRange stageCount = IntRange.One;

        /// <summary>每過一階段增加的防禦值。Defence added per completed stage.</summary>
        public float defencePerStage = 0f;

        // ── 警報 / Alarm ──
        public float alertIncrement = 35f;
        [NoTranslate] public string alarmSignal = "FFF_AlertScanner_Triggered";
        /// <summary>地圖上沒有警報反應建築時直接叫的援軍。Called directly when nothing on the map answers the alarm.</summary>
        public RaidWaveDef fallbackRaidWave;
        public SoundDef alarmSound;
        public EffecterDef alarmEffecter;

        // ── 獎勵 / Rewards ──
        public List<ServerHackReward> rewards = new List<ServerHackReward>();
        public ThingDef fallbackThing;

        public CompProperties_ServerHackable()
        {
            compClass = typeof(CompServerHackable);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef)) yield return e;
            if (stageCount.min < 1) yield return $"{parentDef.defName}: stageCount must be >= 1";
            if (rewards.NullOrEmpty() && fallbackThing == null)
                yield return $"{parentDef.defName}: CompProperties_ServerHackable has no rewards and no fallbackThing";
            if (parentDef.tickerType != TickerType.Normal)
                yield return $"{parentDef.defName}: CompServerHackable needs tickerType Normal to detect hack sessions";
        }
    }

    /// <summary>
    /// 多階段駭入的伺服器主機。沿用原版 CompHackable 的工作、鎖定與 UI，多做兩件事：
    /// 每一次新的駭入作業都拉設施警報；完成一階段後等沒人佔用再重置進入下一階段，
    /// 讓下一階段必須重新開工（也就必然再拉一次警報）。
    ///
    /// A multi-stage hackable server. Reuses vanilla CompHackable's job, lockout and UI, and adds two
    /// things: every new hacking session trips the facility alarm, and a finished stage only resets
    /// once nobody holds the server, so the next stage always needs a fresh job (and a fresh alarm).
    /// </summary>
    public class CompServerHackable : CompHackable
    {
        private const int SessionGapTicks = 60;

        private int stagesTotal = -1;
        private int stagesDone;
        private bool pendingNextStage;
        private int lastSeenHackTick = -99999;
        private Pawn lastSessionPawn;
        private List<int> payoutCounts = new List<int>();   // 與 Props.rewards 同索引 / parallel to Props.rewards

        public new CompProperties_ServerHackable Props => (CompProperties_ServerHackable)props;

        public int StagesTotal => stagesTotal;
        public int StagesDone => stagesDone;
        public bool FullyHacked => stagesDone >= stagesTotal;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (stagesTotal < 0) stagesTotal = Mathf.Max(1, Props.stageCount.RandomInRange);
        }

        // ── 偵測駭入作業 / Session detection ─────────────────────────────

        public override void CompTick()
        {
            base.CompTick();   // 原版的鎖定解除 / vanilla lockout expiry
            if (!parent.Spawned) return;

            int now = Find.TickManager.TicksGame;

            if (lastHackTick == now)
            {
                bool newSession = now - lastSeenHackTick > SessionGapTicks || lastUser != lastSessionPawn;
                lastSeenHackTick = now;
                lastSessionPawn = lastUser;
                if (newSession && !IsHacked) TripAlarm(lastUser);
            }

            if (pendingNextStage && !IsHeld()) AdvanceStage();
        }

        private bool IsHeld()
        {
            return parent.Map.reservationManager.IsReservedByAnyoneOf(parent, Faction.OfPlayer);
        }

        // ── 警報 / Alarm ─────────────────────────────────────────────────

        private void TripAlarm(Pawn hacker)
        {
            FacilityAlarmUtility.Trip(parent, Props.alertIncrement, Props.alarmSignal, Props.fallbackRaidWave);
            Props.alarmSound?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            Props.alarmEffecter?.Spawn(parent.Position, parent.Map).Cleanup();
            Messages.Message("DMS_ServerHack_AlarmTripped".Translate(parent.Named("SERVER"), hacker.Named("HACKER")),
                parent, MessageTypeDefOf.ThreatSmall);
        }

        // ── 階段 / Stages ────────────────────────────────────────────────

        protected override void OnHacked(Pawn hacker = null, bool suppressMessages = false)
        {
            base.OnHacked(hacker, suppressMessages);   // Notify_Hacked、dropOnHacked、原版訊息
            stagesDone++;
            GiveReward(hacker);
            if (!FullyHacked) pendingNextStage = true;  // hacked 維持 true → 工作自然結束
        }

        private void AdvanceStage()
        {
            pendingNextStage = false;
            hacked = false;
            progress = 0f;
            progressLastLockout = 0f;
            lastHackTick = -1;          // 讓 HackingStarted 任務訊號每階段都送一次
            sentLetter = false;
            defence = Props.defence + Props.defencePerStage * stagesDone;
            parent.DirtyMapMesh(parent.Map);   // hacked/unhacked 圖層切回
        }

        private void GiveReward(Pawn hacker)
        {
            Thing reward = RollReward();
            if (reward == null) return;
            if (GenPlace.TryPlaceThing(reward, parent.InteractionCell.IsValid ? parent.InteractionCell : parent.Position,
                    parent.Map, ThingPlaceMode.Near, out Thing placed))
            {
                Find.LetterStack.ReceiveLetter(
                    "DMS_ServerHack_StageLetterLabel".Translate(parent.Named("SERVER")),
                    "DMS_ServerHack_StageLetterText".Translate(parent.Named("SERVER"), placed.Named("REWARD"),
                        stagesDone.Named("DONE"), stagesTotal.Named("TOTAL")),
                    LetterDefOf.PositiveEvent, placed);
            }
        }

        private Thing RollReward()
        {
            List<ServerHackReward> list = Props.rewards;
            while (payoutCounts.Count < list.Count) payoutCounts.Add(0);

            List<int> candidates = Enumerable.Range(0, list.Count)
                .Where(i => list[i].weight > 0f && (list[i].maxPerBuilding <= 0 || payoutCounts[i] < list[i].maxPerBuilding))
                .ToList();

            while (candidates.TryRandomElementByWeight(i => list[i].weight, out int pick))
            {
                Thing t = ServerHackRewardUtility.Make(list[pick]);
                if (t != null) { payoutCounts[pick]++; return t; }
                candidates.Remove(pick);   // 例：無 Royalty 的 Techprint → 換下一項
            }
            return Props.fallbackThing != null ? ThingMaker.MakeThing(Props.fallbackThing) : null;
        }

        // ── UI ───────────────────────────────────────────────────────────

        public override string CompInspectStringExtra()
        {
            string s = base.CompInspectStringExtra();
            string stage = FullyHacked
                ? "DMS_ServerHack_Depleted".Translate().Resolve()
                : "DMS_ServerHack_Stage".Translate(stagesDone, stagesTotal).Resolve();
            if (!FullyHacked) stage += "\n" + "DMS_ServerHack_AlarmWarning".Translate().Colorize(ColorLibrary.RedReadable);
            return s.NullOrEmpty() ? stage : s + "\n" + stage;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Trip alarm",
                    action = () => TripAlarm(null),
                };
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref stagesTotal, "dms_stagesTotal", -1);
            Scribe_Values.Look(ref stagesDone, "dms_stagesDone", 0);
            Scribe_Values.Look(ref pendingNextStage, "dms_pendingNextStage", false);
            Scribe_Collections.Look(ref payoutCounts, "dms_payoutCounts", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                payoutCounts ??= new List<int>();
                // 原版 PostLoadInit 會做 hacked = hacked || progress >= defence；已在 AdvanceStage 歸零，不受影響。
            }
        }
    }
}
```

**注意事項**

- 原版 `ProcessHacked` 在 `hacked == true` 時直接 return，所以**必須**在 `AdvanceStage` 把 `hacked` 設回 false，否則第二階段永遠不會完成。
- `PostExposeData` 的原版邏輯 `hacked = hacked || progress >= defence`：`pendingNextStage` 期間存檔時 `hacked == true`，讀檔後仍是 true，`CompTick` 會繼續等到沒人佔用再重置，沒問題。
- `lastSeenHackTick` / `lastSessionPawn` 不存檔：讀檔後第一 tick 若有人在駭，會被視為新作業而多拉一次警報。可接受（讀檔等於中斷），不想要的話就把它們一起存。
- 機械師遠端駭入（`Props.onlyRemotelyHackable` 以外的路徑）一樣會寫 `lastHackTick`，所以也會觸發警報。

### 4.5 `FacilityAlarmUtility.cs`

```csharp
using System.Linq;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 從非掃描器的來源（例如伺服器被駭）拉設施警報，行為與 CompAlertScanner.FireSignal 對齊：
    /// 先累加警戒值、再廣播帶 MAP 參數的訊號，讓同圖的 CompAlertEffector 反應。
    /// 地圖上沒有任何反應建築時，改用 fallbackRaidWave 保底。
    ///
    /// Trips the facility alarm from something other than a scanner (e.g. a server being hacked), matching
    /// CompAlertScanner.FireSignal: bump the alert level, then broadcast the signal with a MAP arg so
    /// same-map CompAlertEffectors respond. With no responders on the map, fall back to a raid wave.
    /// </summary>
    public static class FacilityAlarmUtility
    {
        public static void Trip(Thing source, float increment, string signal, RaidWaveDef fallbackRaidWave)
        {
            Map map = source?.MapHeld;
            if (map == null) return;

            map.GetComponent<MapComponent_AlertCounter>()?.Notify(increment);

            if (!signal.NullOrEmpty())
            {
                Find.SignalManager.SendSignal(new Signal(signal,
                    source.Named("SUBJECT"), source.Position.Named("POSITION"), map.Named("MAP")));
            }

            if (fallbackRaidWave != null && !HasResponders(map, signal))
            {
                BattleGroupCallUtility.Call(fallbackRaidWave, source, respectCooldown: false);
            }
        }

        private static bool HasResponders(Map map, string signal)
        {
            foreach (Building b in map.listerBuildings.allBuildingsNonColonist)
            {
                if (b.Destroyed) continue;
                if (b.AllComps.OfType<CompAlertEffector>().Any(c => c.Props.listenSignal == signal)) return true;
            }
            return false;
        }
    }
}
```

**重構**：把 `CompAlertEffector_CallBattleGroup.DoEffect` 裡「檢查冷卻 → `RaidWaveWorker` 呼叫」那段抽成 `BattleGroupCallUtility.Call(RaidWaveDef, Thing source, bool respectCooldown)`，`DoEffect` 改呼叫它。行為不變，只是讓伺服器能共用。

> `CompAlertEffector.Props` 若不是 public 或型別不同，動工時看 FFF 的 `CompAlertEffector.cs` 調整。
> 被 EMP 癱瘓或斷電的反應建築仍算「有反應者」：玩家先斷電再駭本來就是正確玩法，那時不該再保底叫援軍。

### 4.6 `CompHuntBreaker.cs`：摧毀暫停追殺

只掛在 `DMS_SageCore` 上。寫成獨立 comp 而非寫死在 SAGE 裡，是為了之後其他節點建築能直接沿用。

```csharp
using RimWorld;
using Verse;

namespace DMS
{
    public class CompProperties_HuntBreaker : CompProperties
    {
        /// <summary>暫停年數（RimWorld 一年 = 60 天）。Suspension in years (one RimWorld year = 60 days).</summary>
        public FloatRange suspendYears = new FloatRange(1f, 2f);

        /// <summary>
        /// 追殺已在暫停中時，這座節點被摧毀要疊加幾天；小於 0 = 再擲一次 suspendYears 疊加。
        /// Days to stack when the hunt is already suspended; below 0 = roll suspendYears again and stack that.
        /// </summary>
        public float stackDaysWhileSuspended = -1f;

        /// <summary>本次摧毀要暫停／疊加的 ticks。Ticks this destruction suspends or stacks.</summary>
        public int RollTicks(bool alreadySuspended)
        {
            if (alreadySuspended && stackDaysWhileSuspended >= 0f)
                return (int)(stackDaysWhileSuspended * GenDate.TicksPerDay);
            return (int)(suspendYears.RandomInRange * GenDate.TicksPerYear);
        }

        public CompProperties_HuntBreaker() { compClass = typeof(CompHuntBreaker); }
    }

    /// <summary>
    /// 殖民艦隊追緝網路上的節點。被摧毀時暫停隱匿級的追殺；不解除永久敵對。
    /// A node in the fleet's tracking network. Destroying it suspends the Occulted-tier hunt; it does
    /// not end permanent hostility.
    /// </summary>
    public class CompHuntBreaker : ThingComp
    {
        public CompProperties_HuntBreaker Props => (CompProperties_HuntBreaker)props;

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (!Counts(mode)) return;
            OccultechSanctionUtility.TrySuspendHunt(Props, parent, previousMap);
        }

        private static bool Counts(DestroyMode mode)
        {
            // 建築不可拆除，只有打爛才算。Not deconstructible: only being destroyed counts.
            return mode == DestroyMode.KillFinalize
                || mode == DestroyMode.KillFinalizeLeavingsOnly;
        }

        public override string CompInspectStringExtra()
        {
            GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
            if (comp == null || !comp.PermanentHostile) return null;
            return comp.HuntSuspended
                ? "DMS_HuntBreaker_InspectSuspended".Translate(comp.HuntResumeTicksLeft.ToStringTicksToPeriod())
                : "DMS_HuntBreaker_InspectActive".Translate();
        }
    }
}
```

> `PostDestroy` 時 `parent` 已 despawn，所以信件的 look target 用 `new GlobalTargetInfo(parent.PositionHeld, previousMap)`。

### 4.7 `GameComponent_OccultechSanction` 修改

```csharp
private int huntSuspendedUntilTick = -1;

public bool HuntSuspended => huntSuspendedUntilTick > Find.TickManager.TicksGame;
public int HuntResumeTicksLeft => HuntSuspended ? huntSuspendedUntilTick - Find.TickManager.TicksGame : 0;

/// <summary>
/// 暫停追殺；已暫停時疊加在目前的結束時間之後。回傳暫停後的剩餘 ticks。
/// Suspends the hunt; stacks onto the current end time when already suspended. Returns ticks left.
/// </summary>
public int SuspendHunt(int ticks)
{
    int now = Find.TickManager.TicksGame;
    huntSuspendedUntilTick = System.Math.Max(now, huntSuspendedUntilTick) + ticks;
    nextHuntTick = -1;
    return HuntResumeTicksLeft;
}

public void BeginPermanentHostility(FloatRange huntIntervalDays)
{
    permanentHostile = true;
    // 暫停中完成新的隱匿級研究：暫停不受影響，恢復時才排程追殺。
    // A new Occulted project during a suspension leaves it alone; the hunt is scheduled on resume.
    if (HuntSuspended) return;
    ScheduleNextHunt(huntIntervalDays);
}

public void EndPermanentHostility()
{
    permanentHostile = false;
    nextHuntTick = -1;
    huntSuspendedUntilTick = -1;
}

public override void GameComponentTick()
{
    base.GameComponentTick();
    int now = Find.TickManager.TicksGame;

    // 暫停結束：SAGE 網路另一台主機升格，重新排程並補傳票。
    if (huntSuspendedUntilTick > 0 && now >= huntSuspendedUntilTick)
    {
        huntSuspendedUntilTick = -1;
        if (permanentHostile)
        {
            ModExtension_OccultechSanction ext = OccultechSanctionUtility.OccultedSanction;
            ScheduleNextHunt(ext?.huntIntervalDays ?? new FloatRange(4f, 7f));
            OccultechSanctionUtility.SendHuntResumedLetter();
            OccultechSanctionUtility.EnsureCourtMartialOffered();
        }
    }

    if (permanentHostile && !HuntSuspended && nextHuntTick > 0 && now >= nextHuntTick)
    {
        // …原本的追殺…
        OccultechSanctionUtility.TryOfferNetworkSite();   // §6.4
    }
    // …EnforceFleetRelation 不變…
}

// ExposeData
Scribe_Values.Look(ref huntSuspendedUntilTick, "dms_occultechHuntSuspendedUntil", -1);
```

### 4.8 `OccultechSanctionUtility` 新增

```csharp
public static void TrySuspendHunt(CompProperties_HuntBreaker props, Thing source, Map map)
{
    GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
    GlobalTargetInfo look = new GlobalTargetInfo(source.PositionHeld, map);
    if (comp == null || !comp.PermanentHostile)
    {
        Messages.Message("DMS_HuntBreaker_NoHunt".Translate(source.Named("NODE")), look, MessageTypeDefOf.NeutralEvent);
        return;
    }

    bool wasSuspended = comp.HuntSuspended;
    // 首次暫停擲 1~2 年；已在暫停中則依節點設定疊加（SAGE +15 天）。
    // First suspension rolls 1~2 years; while suspended, stack per the node's setting (SAGE +15 days).
    int ticks = props.RollTicks(wasSuspended);
    int left = comp.SuspendHunt(ticks);

    if (wasSuspended)
    {
        Messages.Message("DMS_HuntBreaker_Extended".Translate(source.Named("NODE"), left.ToStringTicksToPeriod().Named("DURATION")),
            look, MessageTypeDefOf.PositiveEvent);
        return;
    }
    Find.LetterStack.ReceiveLetter(
        "DMS_HuntBreaker_SuspendedLabel".Translate(),
        "DMS_HuntBreaker_SuspendedText".Translate(source.Named("NODE"), Fleet.Named("FLEET"), left.ToStringTicksToPeriod().Named("DURATION")),
        LetterDefOf.PositiveEvent, look);
}

public static void SendHuntResumedLetter()
{
    Find.LetterStack.ReceiveLetter("DMS_HuntBreaker_ResumedLabel".Translate(),
        "DMS_HuntBreaker_ResumedText".Translate(Fleet.Named("FLEET")), LetterDefOf.ThreatBig);
}
```

`DMS_Mod.Content`（§4.3 用到）：若專案尚無 `Mod` 子類別，改用
`LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageIdPlayerFacing.EqualsIgnoreCase("<About.xml 的 packageId>"))` 並快取。

### 4.9 `DebugActions_Occultech.cs` 新增

| 名稱 | 動作 |
|---|---|
| `Suspend hunt (1 year)` | `comp.SuspendHunt(GenDate.TicksPerYear)`（略過節點設定） |
| `End hunt suspension now` | `huntSuspendedUntilTick = now`（下一 tick 觸發恢復流程） |
| `Offer fleet network site` | 強制產生 §6 的任務（略過追殺判定） |

另外 `LogState` 補印 `HuntSuspended` 與剩餘時間。

### 4.10 `DMS_DefOf` 新增

```csharp
public static QuestScriptDef DMS_FleetNetworkSite;
```

本計畫沒有任何 DefOf 指向 Royalty 內容（techprint 是在執行期由 `ResearchProjectDef.Techprint` 取得），不需要 `[MayRequire…]`。

---

## 5. XML Def

新檔：`1.6/NewContent/Defs/Things_Building/DMS_ExplorationBuildings_Server.xml`

### 5.1 共用抽象父

```xml
<!-- 電晶體電腦：伺服器群共用。不可建造（FFF_DevCategory）、不可認領、tick Normal（偵測駭入作業需要）。
     Transistor computers: shared base. Not buildable, not claimable, ticks Normal (session detection needs it). -->
<ThingDef ParentName="DMS_BuildingBase" Name="DMS_ServerBuildingBase" Abstract="True">
  <category>Building</category>
  <selectable>true</selectable>
  <useHitPoints>true</useHitPoints>
  <pathCost>50</pathCost>
  <fillPercent>0.9</fillPercent>
  <blockLight>true</blockLight>
  <tickerType>Normal</tickerType>
  <receivesSignals>true</receivesSignals>
  <hasInteractionCell>true</hasInteractionCell>
  <interactionCellOffset>(0,0,-1)</interactionCellOffset>
  <statBases>
    <Flammability>0.3</Flammability>
  </statBases>
  <building>
    <claimable>false</claimable>
    <deconstructible>false</deconstructible>  <!-- 只能打爛 / must be destroyed, see §10 -->
    <ai_chillDestination>false</ai_chillDestination>
  </building>
  <killedLeavings>
    <ChunkSlagSteel>2</ChunkSlagSteel>
    <ComponentIndustrial>2</ComponentIndustrial>
  </killedLeavings>
  <comps>
    <!-- 放射性電池：自帶的微弱光 / the radioisotope cell's faint glow -->
    <li Class="CompProperties_Glower">
      <glowRadius>3</glowRadius>
      <glowColor>(90,200,160,0)</glowColor>
    </li>
  </comps>
</ThingDef>
```

> `CompProperties_Glower` 要在「駭乾」後熄滅的話，交給原版 `CompHackable.ShouldBeLitNow` + `glowIfHacked=false`（CompHackable 實作 `IThingGlower`）。

### 5.2 設施伺服核心

```xml
<ThingDef ParentName="DMS_ServerBuildingBase">
  <defName>DMS_FacilityServerCore</defName>
  <label>facility server core</label>
  <description>(見 §7.1)</description>
  <graphicData>
    <texPath>Things/Building/AlertCaller/FacilityServerCore</texPath>
    <graphicClass>Graphic_Single</graphicClass>
    <drawSize>(4,4)</drawSize>
    <shadowData><volume>(1.8, 0.8, 1.6)</volume></shadowData>
  </graphicData>
  <uiIconScale>0.8</uiIconScale>
  <size>(2,2)</size>
  <rotatable>false</rotatable>
  <staticSunShadowHeight>0.5</staticSunShadowHeight>
  <statBases>
    <MaxHitPoints>900</MaxHitPoints>
    <Mass>120</Mass>
  </statBases>
  <comps>
    <li Class="DMS.CompProperties_ServerHackable">
      <defence>1800</defence>
      <defencePerStage>600</defencePerStage>
      <stageCount>3~4</stageCount>
      <intellectualSkillPrerequisite>6</intellectualSkillPrerequisite>
      <lockoutDurationHoursRange>6~12</lockoutDurationHoursRange>
      <glowIfHacked>false</glowIfHacked>
      <autohackWarningString>DMS_ServerHack_AutohackWarning</autohackWarningString>
      <alertIncrement>35</alertIncrement>
      <!-- 只出現在地下檔案庫，空投穿不過岩頂，不設保底援軍（見 §11.6）。
           Underground only; drop pods can't get through the rock, so no fallback wave (see §11.6). -->
      <alarmSound>DMS_ServerAlarm</alarmSound>   <!-- 沒有新音效前先用 FlickSwitch / FlickSwitch until a real sound exists -->
      <fallbackThing>DMS_DataCache_Restricted</fallbackThing>
      <rewards>
        <li><kind>Techprint</kind><weight>5</weight></li>
        <li><kind>Thing</kind><thingDef>DMS_DataCache_Sealed</thingDef><weight>3</weight><maxPerBuilding>2</maxPerBuilding></li>
        <li>
          <kind>AccessKey</kind><weight>4</weight><maxPerBuilding>2</maxPerBuilding>
          <keyWeights>
            <DMS_AccessKey_Nara>1</DMS_AccessKey_Nara>
            <DMS_AccessKey_Neutrals>1</DMS_AccessKey_Neutrals>
          </keyWeights>
        </li>
      </rewards>
    </li>
  </comps>
</ThingDef>
```

### 5.3 設施數據輔機（純裝飾）

```xml
<ThingDef ParentName="DMS_ServerBuildingBase">
  <defName>DMS_FacilityDataRack</defName>
  <label>facility data rack</label>
  <description>(見 §7.2)</description>
  <tickerType>Never</tickerType>
  <hasInteractionCell>false</hasInteractionCell>
  <graphicData>
    <texPath>Things/Building/AlertCaller/FacilityDataRack</texPath>
    <graphicClass>Graphic_Multi</graphicClass>
    <drawSize>(3,3)</drawSize>
  </graphicData>
  <size>(1,2)</size>
  <rotatable>true</rotatable>
  <statBases>
    <MaxHitPoints>400</MaxHitPoints>
    <Mass>60</Mass>
  </statBases>
</ThingDef>
```

### 5.4 站點伺服主機

```xml
<ThingDef ParentName="DMS_ServerBuildingBase">
  <defName>DMS_SiteServerCore</defName>
  <label>site server</label>
  <description>(見 §7.3)</description>
  <graphicData>
    <texPath>Things/Building/AlertCaller/SiteServerCore</texPath>
    <graphicClass>Graphic_Single</graphicClass>
    <drawSize>(2,2)</drawSize>
  </graphicData>
  <size>(1,1)</size>
  <rotatable>false</rotatable>
  <statBases>
    <MaxHitPoints>350</MaxHitPoints>
    <Mass>40</Mass>
  </statBases>
  <comps>
    <li Class="DMS.CompProperties_ServerHackable">
      <defence>1200</defence>
      <stageCount>1</stageCount>
      <intellectualSkillPrerequisite>4</intellectualSkillPrerequisite>
      <lockoutDurationHoursRange>4~8</lockoutDurationHoursRange>
      <glowIfHacked>false</glowIfHacked>
      <autohackWarningString>DMS_ServerHack_AutohackWarning</autohackWarningString>
      <alertIncrement>40</alertIncrement>
      <fallbackRaidWave>DMS_RaidWave_FacilityQRF</fallbackRaidWave>
      <fallbackThing>DMS_AccessKey_Neutrals</fallbackThing>
      <rewards>
        <li><kind>Techprint</kind><weight>5</weight></li>
        <li>
          <kind>AccessKey</kind><weight>5</weight>
          <keyWeights>
            <DMS_AccessKey_Nara>1</DMS_AccessKey_Nara>
            <DMS_AccessKey_Neutrals>1</DMS_AccessKey_Neutrals>
          </keyWeights>
        </li>
      </rewards>
    </li>
  </comps>
</ThingDef>
```

> 站點伺服主機**不掛** `CompHuntBreaker`：摧毀它不影響追殺。
> 站點伺服主機無 Royalty 時 `fallbackThing` 用密鑰而非技術匣，遵守「不包含封存技術匣」。

### 5.5 賢者 SAGE

```xml
<ThingDef ParentName="DMS_ServerBuildingBase">
  <defName>DMS_SageCore</defName>
  <label>SAGE</label>
  <description>(見 §7.4)</description>
  <graphicData>
    <texPath>Things/Building/AlertCaller/Sage</texPath>
    <graphicClass>Graphic_Multi</graphicClass>
    <drawSize>(3,5)</drawSize>
    <drawOffset>(0,0,0.5)</drawOffset>   <!-- 依貼圖實際底座調整 / tune to the art's base line -->
  </graphicData>
  <size>(1,3)</size>
  <rotatable>true</rotatable>
  <staticSunShadowHeight>0.8</staticSunShadowHeight>
  <statBases>
    <MaxHitPoints>1600</MaxHitPoints>
    <Mass>300</Mass>
  </statBases>
  <comps>
    <li Class="DMS.CompProperties_ServerHackable">
      <defence>2500</defence>
      <defencePerStage>800</defencePerStage>
      <stageCount>2~3</stageCount>
      <intellectualSkillPrerequisite>10</intellectualSkillPrerequisite>
      <lockoutDurationHoursRange>8~16</lockoutDurationHoursRange>
      <glowIfHacked>false</glowIfHacked>
      <autohackWarningString>DMS_ServerHack_AutohackWarning</autohackWarningString>
      <alertIncrement>50</alertIncrement>
      <fallbackRaidWave>DMS_RaidWave_FacilityQRF</fallbackRaidWave>
      <fallbackThing>DMS_DataCache_Restricted</fallbackThing>
      <rewards>
        <li><kind>Techprint</kind><weight>4</weight></li>
        <li><kind>Thing</kind><thingDef>DMS_DataCache_Sealed</thingDef><weight>3</weight><maxPerBuilding>2</maxPerBuilding></li>
        <li><kind>Thing</kind><thingDef>DMS_DataCache_Occulted</thingDef><weight>1</weight><maxPerBuilding>1</maxPerBuilding></li>
        <li>
          <kind>AccessKey</kind><weight>3</weight>
          <keyWeights>
            <DMS_AccessKey_Nara>1</DMS_AccessKey_Nara>
            <DMS_AccessKey_Neutrals>1</DMS_AccessKey_Neutrals>
          </keyWeights>
        </li>
      </rewards>
    </li>
    <li Class="DMS.CompProperties_HuntBreaker">
      <suspendYears>1~2</suspendYears>
      <!-- 追殺已暫停時，每摧毀一座只再延後 15 天。While already suspended, each one adds only 15 days. -->
      <stackDaysWhileSuspended>15</stackDaysWhileSuspended>
    </li>
  </comps>
</ThingDef>
```

**旋轉注意**：`(1,3)` 的建築朝東／西時佔地變成 3×1，但 `drawSize (3,5)` 不會自動交換。`Sage_east` 必須是**已經轉好**的橫向圖（寬高比請確認），否則要在 `graphicData` 用 `drawSize` 配合 `drawRotated`／分方向偏移調整。進遊戲四個方向各放一台檢查。

### 5.6 `keyWeights` 的解析

`List<ThingDefCountClass>` 支援 `<DefName>數量</DefName>` 的簡寫，與原版 `costList` 相同。

---

## 6. 站點任務

### 6.1 放置策略

| 建築 | 出現在哪裡 |
|---|---|
| 站點伺服主機 | **所有既有地面設施**各 1 台：`DMS_VaultStation`、`DMS_SiteStation`、`DMS_Airport`、`DMS_RepairStation`、`DMS_Ruin_LaunchSite`、`DMS_OperationBase`（放在指揮／通訊用的內室） |
| 設施伺服核心 + 數據輔機 | **新的地下設施類型「設施檔案庫」**（`DMS_DataArchive`），入口在荒廢站點地表，詳見 §11 |
| SAGE + 數據輔機 | **新站點** `DMS_SageNodeSite`（SAGE 節點站），輔機排成一整列「無上限擴充」的機櫃陣列 |

> 設施伺服核心只在地下的設施檔案庫出現，那裡的警報由地板開口（`CompAlertEffector_HoleEmerge`）與 FPV 巢負責，不用空投保底，見 §11.6。
>
> 站點伺服主機與設施伺服核心純粹是駭入獎勵點，摧毀它們不影響追殺；能暫停追殺的只有 SAGE 節點。

版面在遊戲內用 FFF 的結構匯出工具擺好後再匯出成 `FFF_StructureDef`，**不要手寫**（現有版面都是上千行的匯出結果）。

### 6.2 新 SitePartDef

新檔 `1.6/NewContent/Defs/Quests/DMS_FleetNetworkSite_Sage.xml`，照 `DMS_LegacySite_SiteStation.xml` 的結構：

| 欄位 | SAGE 節點 `DMS_SageNodeSite` |
|---|---|
| `workerClass` | `Fortified.SitePartWorker_FFF_TimedHostileSite` |
| `baseEntryHours` / `min` / `max` | 36 / 10 / 72 |
| `minThreatPoints` | 500 |
| `minMapSize` | (250,0,250) |
| GenStep `useTag` | `DMS_SageNode` |
| `spawnDefenders` / `defendRadius` | true / 24 |
| 衛星 | `DMS_RuinSatellite` 3~4 |
| 必備警報建築 | 大型哨塔、監視器、FPV 機庫、**戰鬥群呼叫器 ×2**、**砲擊呼叫器 ×1** |
| 版面內其他主機 | SAGE ×1、數據輔機一整列；可另放 1 台站點伺服主機當額外駭入點 |

> 原本規劃的中繼站站點（`DMS_RelayStationSite`）已移除：它存在的理由是「摧毀站點伺服主機暫停追殺」，這條規則取消後就與既有地面設施重複。

### 6.3 任務腳本 `DMS_FleetNetworkSite`

新檔 `1.6/NewContent/Defs/Quests/DMS_FleetNetworkQuest.xml`：

```xml
<QuestScriptDef>
  <defName>DMS_FleetNetworkSite</defName>
  <successHistoryEvent>DMS_QuestSuccess_Military</successHistoryEvent>
  <failedOrExpiredHistoryEvent>DMS_QuestFailed_Military</failedOrExpiredHistoryEvent>
  <rootSelectionWeight>1.2</rootSelectionWeight>
  <rootMinPoints>500</rootMinPoints>
  <expireDaysRange>4~8</expireDaysRange>
  <sendAvailableLetter>true</sendAvailableLetter>
  <questNameRules>…§7.6…</questNameRules>
  <questDescriptionRules>…§7.6…</questDescriptionRules>
  <root Class="DMS.QuestNode_Root_DMS_FleetNetworkSite">
    <factionDef>DMS_Legacy</factionDef>
    <baseTimeoutDays>12</baseTimeoutDays>
    <minTimeoutDays>4</minTimeoutDays>
    <maxTimeoutDays>24</maxTimeoutDays>
    <installations>
      <li>
        <sitePartDef>DMS_SageNodeSite</sitePartDef>
        <weight>1</weight>
        <minPoints>500</minPoints>
        <expiredLetterLabelKey>DMS_SageNode_ExpiredLabel</expiredLetterLabelKey>
        <expiredLetterTextKey>DMS_SageNode_ExpiredText</expiredLetterTextKey>
        <clearedLetterLabelKey>DMS_SageNode_ClearedLabel</clearedLetterLabelKey>
        <clearedLetterTextKey>DMS_SageNode_ClearedText</clearedLetterTextKey>
      </li>
    </installations>
  </root>
</QuestScriptDef>
```

> 目前只有一種站點，仍沿用 `installations` 清單的寫法，之後要加別的網路節點站點只需加一項。
> SAGE 守軍由 `spawnDefenders` 依威脅點數縮放，所以門檻放到 500，讓追殺較早開始的存檔也能拿到反擊機會。

### 6.4 `QuestNode_Root_DMS_FleetNetworkSite.cs`

```csharp
namespace DMS
{
    /// <summary>
    /// 追緝網路節點站：只在隱匿級追殺進行中（且未暫停）時可生成，同時只允許一份進行中。
    /// 其餘全部沿用 RandomInstallation（依點數抽站點、信件鍵覆寫、installation 語法常數）。
    /// Tracking-network sites: only while the Occulted hunt is running (and not suspended), one at a time.
    /// Everything else is RandomInstallation's.
    /// </summary>
    public class QuestNode_Root_DMS_FleetNetworkSite : QuestNode_Root_DMS_RandomInstallation
    {
        /// <summary>除錯用：略過追殺判定。Debug: skip the hunt gate.</summary>
        public static bool ignoreHuntGate;

        protected override bool TestRunInt(Slate slate)
        {
            if (!ignoreHuntGate && !HuntRunning()) return false;
            if (Find.QuestManager.QuestsListForReading.Any(q =>
                    q.root == DMS_DefOf.DMS_FleetNetworkSite && !q.Historical)) return false;
            return base.TestRunInt(slate);
        }

        public static bool HuntRunning()
        {
            GameComponent_OccultechSanction c = GameComponent_OccultechSanction.CompSafe;
            return c != null && c.PermanentHostile && !c.HuntSuspended;
        }
    }
}
```

**主動派發**：`OccultechSanctionUtility.TryOfferNetworkSite()` 在每次追殺觸發時以 **35%** 機率呼叫
`QuestUtility.GenerateQuestAndMakeAvailable(DMS_DefOf.DMS_FleetNetworkSite, points)`（`TestRun` 不過就略過），
與 `EnsureCourtMartialOffered` 同一位置。這樣玩家被追殺時一定會陸續看到「反擊」的機會，而不只靠說書人抽中。

**任務成功條件**：沿用 FFF 限時站點（肅清即成功）。追殺暫停由 SAGE 的 `CompHuntBreaker` 獨立處理，
不綁任務：玩家不接任務、或在別的地圖炸掉 SAGE，效果一樣成立。

### 6.5 既有設施的描述補句

所有地面設施的任務描述各補一句站點伺服主機（§7.7）：

- `DMS_LegacyInstallationQuest.xml`：`DMS_LaunchSiteSite`、`DMS_SiteStationSite`、`DMS_RepairStationSite`、`DMS_OperationBaseSite`、`DMS_VaultStationSite` 五段
- `DMS_LegacySite_Airport.xml`：`DMS_AirportAssault` 的描述
- `DMS_SiteStationSite` 額外再補一句地下的設施檔案庫（§11.9）

---

## 7. 文本

> 以下提供 English（Def 本體）與繁體中文（DefInjected／Keyed）。簡體中文從繁中轉寫。
> 檔案位置：
> - ThingDef：`1.6/NewContent/Languages/<lang>/DefInjected/ThingDef/DMS_ExplorationBuildings_Server.xml`
> - SitePartDef：`.../DefInjected/SitePartDef/DMS_FleetNetworkSite.xml`
> - QuestScriptDef：`.../DefInjected/QuestScriptDef/DMS_FleetNetworkQuest.xml`
> - Keyed：`1.6/NewContent/Languages/<lang>/Keyed/DMS_FacilityServer.xml`

### 7.1 設施伺服核心

**繁中**

> 一台體積不小的電晶體電腦，這種古老但可靠性極高的設備常見於殖民艦隊的各種設施內作為無紙化辦公與文書資料儲存的主機。其內部使用一種特殊的放射性電池來為自身提供運行能源來確保在無人職守的狀態下仍能夠長期運行。\n\n這些伺服核心的底層資料內通常儲存有重要的科技藍圖與通行密鑰紀錄備份，駭入必然會牽動整個設施的警報系統響應，因此在開始駭入之前最好做充足的防禦準備。

**EN**

> A sizable transistor computer. Old but extremely reliable, machines like this serve as paperless office and records hosts throughout the colony fleet's facilities. A special radioisotope cell inside keeps it running for years without anyone tending it.\n\nThe lower data layers of these server cores usually hold backups of important technical blueprints and access key records. Breaking into it will inevitably set off the whole facility's alarm response, so it is best to prepare your defenses before you start hacking.

### 7.2 設施數據輔機

**繁中**

> 與設施伺服核心並聯運作的磁帶與磁芯記憶體機櫃，負責存放主機調閱頻率較低的歸檔資料。它本身不具備獨立的運算與存取介面，所有資料都必須經由主機讀出。

**EN**

> A cabinet of tape drives and core memory wired in parallel with a facility server core, holding the archives the host rarely calls up. It has no processing or access interface of its own; everything on it has to be read out through the host.

### 7.3 站點伺服主機

**繁中**

> 一台體積較小的電晶體電腦，這種古老但可靠性極高的設備常見於殖民艦隊的各種設施內作為無紙化辦公與文書資料儲存的主機。其內部使用一種特殊的放射性電池來為自身提供運行能源來確保在無人職守的狀態下仍能夠長期運行。\n\n這種小型站點上使用的主機通常來說不會儲存太機密的數據，但仍可以從中提取到有價值的情報。一旦駭入就會觸發主機的安全警報響應，因此在開始駭入之前最好做充足的防禦準備。

**EN**

> A compact transistor computer. Old but extremely reliable, machines like this serve as paperless office and records hosts throughout the colony fleet's facilities. A special radioisotope cell inside keeps it running for years without anyone tending it.\n\nThe hosts used at small sites like this rarely hold anything truly classified, but valuable intelligence can still be pulled from them. Hacking it will trip the host's security alarm response, so it is best to prepare your defenses before you start.

### 7.4 賢者 SAGE

**繁中**

> 一種分布式計算的戰略指揮計算機，能夠根據使用需求無上限的擴充輔機並提升計算能力，被殖民艦隊用於殖民星球上的戰略防空以及連接數個大陸上的無數雷達追蹤及導航網路。將其摧毀必然會對殖民艦隊在星球上的追緝行動造成嚴重的影響，但在一兩年內整個SAGE網路內的另一台計算機就會升格為主機繼續執行任務。

**EN**

> A distributed strategic command computer that can take on auxiliary racks without limit, adding computing power as demand grows. The colony fleet uses it for planetary strategic air defense and to link the countless radar tracking and navigation networks spread across several continents. Destroying it is certain to cripple the fleet's hunting operations on the planet, though within a year or two another computer in the SAGE network will be promoted to host and carry on the task.

**機制補句**：

> 繁中：\n\n它的底層儲存區保留著部分戰略資料庫的副本，駭入時整個設施的警報系統都會被牽動。
>
> EN: \n\nIts lower storage still keeps copies of parts of the strategic database. Hacking it will bring the whole facility's alarm system down on you.

### 7.5 Keyed

```xml
<!-- 駭入 / Hacking -->
<DMS_ServerHack_Stage>駭入階段：{0} / {1}</DMS_ServerHack_Stage>
<DMS_ServerHack_Depleted>資料已全數提取</DMS_ServerHack_Depleted>
<DMS_ServerHack_AlarmWarning>每次開始駭入都會觸發設施警報</DMS_ServerHack_AlarmWarning>
<DMS_ServerHack_AutohackWarning>自動駭入的每一次開工都會觸發 {0} 的設施警報。</DMS_ServerHack_AutohackWarning>
<DMS_ServerHack_AlarmTripped>{HACKER_nameDef}開始駭入{SERVER_definite}，設施警報已被觸發！</DMS_ServerHack_AlarmTripped>
<DMS_ServerHack_StageLetterLabel>資料提取：{SERVER_label}</DMS_ServerHack_StageLetterLabel>
<DMS_ServerHack_StageLetterText>我們從{SERVER_definite}的底層儲存區中提取出了{REWARD_labelShort}。\n\n駭入進度：{DONE} / {TOTAL}。若要繼續提取，必須重新開始駭入，這會再次觸發設施警報。</DMS_ServerHack_StageLetterText>

<!-- 追殺暫停 / Hunt suspension -->
<DMS_HuntBreaker_InspectActive>摧毀此節點可暫時中斷殖民艦隊的追殺</DMS_HuntBreaker_InspectActive>
<DMS_HuntBreaker_InspectSuspended>追殺已中斷，預計 {0} 後恢復</DMS_HuntBreaker_InspectSuspended>
<DMS_HuntBreaker_NoHunt>{NODE_definite}已被摧毀。它所連接的追緝網路陷入一片寂靜，但殖民艦隊目前並未追緝我們。</DMS_HuntBreaker_NoHunt>
<DMS_HuntBreaker_Extended>{NODE_definite}已被摧毀，追緝網路的重建時間被進一步延後：追殺預計在 {DURATION} 後恢復。</DMS_HuntBreaker_Extended>
<DMS_HuntBreaker_SuspendedLabel>追殺中斷</DMS_HuntBreaker_SuspendedLabel>
<DMS_HuntBreaker_SuspendedText>{NODE_definite}已被摧毀。\n\n失去這個節點後，{FLEET_name}在星球上的雷達追蹤與導航網路出現了大面積的空白，他們已經無法掌握我們的位置，追殺行動被迫中止。\n\n但這只是暫時的。網路內的其他計算機遲早會接手這個節點的工作，預計約 {DURATION} 後追殺就會重新開始。\n\n請注意：我們與艦隊的永久敵對狀態並未因此解除。</DMS_HuntBreaker_SuspendedText>
<DMS_HuntBreaker_ResumedLabel>追緝網路恢復</DMS_HuntBreaker_ResumedLabel>
<DMS_HuntBreaker_ResumedText>SAGE 網路內的另一台計算機已經升格為主機，{FLEET_name}的追緝網路重新上線了。\n\n他們再次掌握了我們的位置，追殺行動即將恢復。</DMS_HuntBreaker_ResumedText>

<!-- SAGE 節點 / SAGE node -->
<DMS_SageNode_EntryCountdownLabel>SAGE 節點：援軍倒數</DMS_SageNode_EntryCountdownLabel>
<DMS_SageNode_EntryCountdownText>SAGE 節點的戰略防空網已經鎖定我們的位置。{0}後，艦隊的快速反應部隊就會抵達。</DMS_SageNode_EntryCountdownText>
<DMS_SageNode_ExpiredLabel>SAGE 節點失聯</DMS_SageNode_ExpiredLabel>
<DMS_SageNode_ExpiredText>SAGE 網路完成了例行的節點輪換，我們掌握的座標已經失效。</DMS_SageNode_ExpiredText>
<DMS_SageNode_ClearedLabel>SAGE 節點已肅清</DMS_SageNode_ClearedLabel>
<DMS_SageNode_ClearedText>守衛 SAGE 節點的機兵已經被清除。賢者主機仍在運作：可以先駭入提取它的戰略資料庫，再將它徹底摧毀。</DMS_SageNode_ClearedText>
```

**EN 版 Keyed**

```xml
<DMS_ServerHack_Stage>Hack stage: {0} / {1}</DMS_ServerHack_Stage>
<DMS_ServerHack_Depleted>All data extracted</DMS_ServerHack_Depleted>
<DMS_ServerHack_AlarmWarning>Every hacking attempt sets off the facility alarm</DMS_ServerHack_AlarmWarning>
<DMS_ServerHack_AutohackWarning>Every time autohacking starts on {0}, it will set off the facility alarm.</DMS_ServerHack_AutohackWarning>
<DMS_ServerHack_AlarmTripped>{HACKER_nameDef} has started hacking {SERVER_definite}. The facility alarm has been tripped!</DMS_ServerHack_AlarmTripped>
<DMS_ServerHack_StageLetterLabel>Data extracted: {SERVER_label}</DMS_ServerHack_StageLetterLabel>
<DMS_ServerHack_StageLetterText>We pulled {REWARD_labelShort} out of the lower storage of {SERVER_definite}.\n\nHack progress: {DONE} / {TOTAL}. Extracting more means starting the hack again, which will set off the facility alarm again.</DMS_ServerHack_StageLetterText>

<DMS_HuntBreaker_InspectActive>Destroying this node will break off the colony fleet's hunt for a while</DMS_HuntBreaker_InspectActive>
<DMS_HuntBreaker_InspectSuspended>Hunt broken off; expected to resume in {0}</DMS_HuntBreaker_InspectSuspended>
<DMS_HuntBreaker_NoHunt>{NODE_definite} has been destroyed. The tracking network it fed has gone quiet, but the colony fleet is not hunting us right now.</DMS_HuntBreaker_NoHunt>
<DMS_HuntBreaker_Extended>{NODE_definite} has been destroyed, pushing the tracking network's recovery back even further: the hunt should resume in about {DURATION}.</DMS_HuntBreaker_Extended>
<DMS_HuntBreaker_SuspendedLabel>Hunt broken off</DMS_HuntBreaker_SuspendedLabel>
<DMS_HuntBreaker_SuspendedText>{NODE_definite} has been destroyed.\n\nWithout it, {FLEET_name}'s radar tracking and navigation network on this planet has a gaping hole in it. They have lost track of us, and the hunt has been called off.\n\nThis will not last. Another computer on the network will take over the node's work sooner or later; the hunt should start again in about {DURATION}.\n\nNote: our permanent hostility with the fleet has not been lifted.</DMS_HuntBreaker_SuspendedText>
<DMS_HuntBreaker_ResumedLabel>Tracking network restored</DMS_HuntBreaker_ResumedLabel>
<DMS_HuntBreaker_ResumedText>Another computer on the SAGE network has been promoted to host, and {FLEET_name}'s tracking network is back online.\n\nThey know where we are again. The hunt is about to resume.</DMS_HuntBreaker_ResumedText>

<DMS_SageNode_EntryCountdownLabel>SAGE node: reinforcements inbound</DMS_SageNode_EntryCountdownLabel>
<DMS_SageNode_EntryCountdownText>The SAGE node's strategic air-defense grid has locked onto our position. In {0}, the fleet's quick-reaction force will arrive.</DMS_SageNode_EntryCountdownText>
<DMS_SageNode_ExpiredLabel>SAGE node lost</DMS_SageNode_ExpiredLabel>
<DMS_SageNode_ExpiredText>The SAGE network has completed a routine node rotation. The coordinates we had are no longer valid.</DMS_SageNode_ExpiredText>
<DMS_SageNode_ClearedLabel>SAGE node cleared</DMS_SageNode_ClearedLabel>
<DMS_SageNode_ClearedText>The machines guarding the SAGE node have been wiped out. The SAGE core itself is still running: hack it to pull what you can from its strategic database, then destroy it for good.</DMS_SageNode_ClearedText>
```

### 7.6 任務名稱與描述

**questNameRules（EN）**

```
questName(installation==DMS_SageNodeSite)->SAGE
questName(installation==DMS_SageNodeSite)->Blind the [sage]
sage->SAGE node
sage->Strategic host
sage->Air-defense host
```

**questNameRules（繁中）**

```
questName(installation==DMS_SageNodeSite)->賢者
questName(installation==DMS_SageNodeSite)->致盲[sage]
sage->SAGE 節點
sage->戰略主機
sage->防空主機
```

**questDescription：SAGE 節點（EN）**

> We have found the host that is coordinating {FLEET_name}'s hunt for us: a SAGE node, the distributed strategic command computer the fleet uses for planetary air defense and to tie together the radar and navigation networks across several continents. Its rack array is guarded by (*Threat)[enemiesCount] [enemiesLabel](/Threat), and it is wired into a full facility alarm system with fire support.\n\nThe SAGE core can be hacked two or three times. Each run pulls technical blueprints, sealed data caches or access keys out of its strategic database, and each run will set off the facility's alarm again. Destroying it will cripple the fleet's hunt; within a year or two another computer on the SAGE network will be promoted to host and pick up where it left off.\n\nThe node's coordinates hold for [siteTimeout], until the network rotates its hosts.\n\nCaution: it is a hostile installation. Once you enter, its monitoring protocol will summon enemy reinforcements after a countdown, and the node can call in mortar fire.

**questDescription：SAGE 節點（繁中）**

> 我們找到了正在協調{FLEET_name}追殺行動的主機：一座 SAGE 節點。那是艦隊用於星球戰略防空、並串連數個大陸上雷達與導航網路的分布式戰略指揮計算機。它的機櫃陣列由(*Threat)[enemiesCount][enemiesLabel](/Threat)看守，並連接著一整套附帶火力支援的設施警報系統。\n\n賢者主機可以駭入兩到三次，每一次都能從它的戰略資料庫中提取出科技藍圖、封存技術匣或訪問密鑰，而每一次都會再度觸發設施警報。將它摧毀能重創艦隊的追緝行動，但在一兩年內，SAGE 網路中的另一台計算機就會升格為主機繼續執行任務。\n\n在網路完成主機輪換之前，節點座標在[siteTimeout]內有效。\n\n注意：這是一座敵對設施。一旦進入，它的監控協議就會在倒數結束後呼叫敵方援軍，而且節點能夠呼叫迫擊砲火力支援。

> `{FLEET_name}` 需要在 `QuestNode_Root_DMS_FleetNetworkSite.RunInt` 裡以 `QuestGen.slate.Set("FLEET", OccultechSanctionUtility.Fleet)` 注入，並在規則字串中寫成 `[FLEET_name]`（QuestGen 語法用中括號）。上面的 `{}` 僅為閱讀方便，實作時統一改成 `[FLEET_name]`。

**SitePartDef arrivedLetter**

| | EN | 繁中 |
|---|---|---|
| SAGE | You have arrived at the SAGE node. Sentry towers cover the approaches, and the node is wired for fire support. | 我們抵達了 SAGE 節點。哨塔覆蓋了所有接近路線，節點還連接著火力支援系統。 |

### 7.7 既有設施描述補句

| 站點 | EN | 繁中 |
|---|---|---|
| 全部地面設施（六座） | The installation also runs a site server: a small records host that can be hacked once for blueprints or access keys, at the cost of setting off the alarm. | 設施內還有一台站點伺服主機。這種小型文書主機可以駭入一次，取得科技藍圖或訪問密鑰，代價是觸發警報。 |
| 荒廢站點（額外） | 見 §11.9 | 見 §11.9 |

插在各段描述的「Caution」段落之前。

---

## 8. 執行順序與檢查清單

### 階段 A：機制（可獨立測試，不需要站點）

- [ ] A1 `DMS.csproj` 補 6 條 Publicize，確認 build 通過
- [ ] A2 `BattleGroupCallUtility`：從 `CompAlertEffector_CallBattleGroup` 抽出呼叫邏輯，原建築行為不變
- [ ] A3 `FacilityAlarmUtility`
- [ ] A4 `ServerHackReward` / `ServerHackRewardUtility`（用 decompiler 確認 techprint API 名稱）
- [ ] A5 `CompServerHackable`
- [ ] A6 `GameComponent_OccultechSanction`：暫停欄位、`SuspendHunt`、tick 修改、存檔
- [ ] A7 `OccultechSanctionUtility`：`TrySuspendHunt`、`SendHuntResumedLetter`、`TryOfferNetworkSite`；`BeginPermanentHostility` 在暫停中不重新排程
- [ ] A8 `CompHuntBreaker`（只掛 SAGE）
- [ ] A9 `DebugActions_Occultech` 新增三項、`LogState` 補欄位
- [ ] A10 `DMS_ExplorationBuildings_Server.xml`（四座建築）
- [ ] A11 Keyed + DefInjected ThingDef（三語）
- [ ] A12 `build.bat`，更新 `1.6/Assemblies/DMS.dll`

### 階段 B：站點

- [ ] B1 在開發地圖用 FFF 工具擺 SAGE 節點版面；匯出 `Site_FleetNetwork.xml`（tag `DMS_SageNode`）
- [ ] B2 SitePartDef + GenStepDef（`DMS_SageNodeSite`）
- [ ] B3 `QuestNode_Root_DMS_FleetNetworkSite` + `DMS_DefOf.DMS_FleetNetworkSite`
- [ ] B4 `DMS_FleetNetworkQuest.xml` + 三語語法規則
- [ ] B5 六座既有地面設施版面（`DMS_VaultStation`、`DMS_SiteStation`、`DMS_Airport`、`DMS_RepairStation`、`DMS_Ruin_LaunchSite`、`DMS_OperationBase`）各放 1 台站點伺服主機；任務描述補句（三語）
- [ ] B6 設施檔案庫：依 §11.10 的清單實作

### 階段 C：文件

- [ ] C1 `OCCULTECH.md`「隱匿級」段落補「追殺暫停」小節、檔案索引、框架對照表
- [ ] C2 `README.md`（若有建築列表）

---

## 9. 測試計畫

開發模式、`DMS` 除錯分類。

| # | 情境 | 預期 |
|---|---|---|
| 1 | 放一台設施伺服核心 + 戰鬥群呼叫器，殖民者開始駭 | 開始駭入的**第一 tick** 警戒值 +35、呼叫器收到訊號；紅色訊息 |
| 2 | 同上，駭到一半徵召打斷，再解除徵召重新下令 | 第二次開工**再拉一次**警報；進度保留 |
| 3 | 駭完第一階段 | 掉一件獎勵＋信件；工作結束；檢視字串顯示 `1 / N`；沒人佔用後防禦值 = 1800 + 600 |
| 4 | 殖民者駭完一階段後**不離開**（自動駭入開啟） | 自動駭入產生的新工作會再拉警報，不會無縫跳過 |
| 5 | 駭完全部階段 | 不再出現駭入選項；檢視字串「資料已全數提取」；螢幕／光熄滅 |
| 6 | 地圖上沒有任何警報反應建築時駭入 | `DMS_RaidWave_FacilityQRF` 保底觸發 |
| 7 | 反應建築被 EMP 癱瘓時駭入 | **不**保底（視為玩家已處理防禦） |
| 8 | 關閉 Royalty 測試獎勵 | Techprint 選項退回；伺服核心／SAGE 給 `DMS_DataCache_Restricted`，站點主機給密鑰 |
| 9 | 伺服核心 4 階段都駭完 | `DMS_DataCache_Sealed` 最多 2、密鑰最多 2 |
| 10 | 永久敵對中摧毀 SAGE | 「追殺中斷」信件；`LogState` 顯示剩餘 60~120 天；`nextHuntTick` 不觸發 |
| 11 | 暫停中再摧毀 SAGE | 簡短訊息；剩餘時間 = 原剩餘 + 15 天 |
| 11b | 永久敵對中摧毀站點伺服主機／設施伺服核心 | 追殺不受影響，無信件或訊息 |
| 12 | 除錯「End hunt suspension now」 | 「追緝網路恢復」信件；重新排程追殺；補發軍事法庭傳票 |
| 13 | 未處於永久敵對時摧毀 | 中性訊息，狀態不變 |
| 14 | 暫停中完成另一項隱匿級研究 | 制裁照常結算；暫停剩餘時間不變；`nextHuntTick` 仍為 -1，恢復時才排程 |
| 14b | 對伺服主機／SAGE 下拆除指令 | 沒有拆除選項，只能攻擊 |
| 15 | 軍事法庭服刑期滿 | 永久敵對與暫停一併清除 |
| 16 | 暫停中存讀檔；駭入階段 `pendingNextStage` 中存讀檔 | 狀態保留 |
| 17 | 非永久敵對時 | `DMS_FleetNetworkSite` 不出現在說書人任務池；除錯強制產生仍可用 |
| 18 | 永久敵對中觸發多次追殺 | 約 35% 機率收到網路站點任務；同時最多一份 |
| 19 | SAGE 四方向放置 | 貼圖對齊佔地，無偏移 |
| 20 | 地下（Vault）地圖放伺服核心 | 警報訊號只影響同一張地圖（`MAP` 參數） |

---

## 10. 設計決策紀錄

### 已確認（2026-09-24）

| # | 項目 | 決定 | 落在哪裡 |
|---|---|---|---|
| 1 | SAGE 獎勵池 | 與設施伺服核心相同，**另加**低權重（1/11、上限 1）的 `DMS_DataCache_Occulted` | §5.5 |
| 2 | 暫停中完成隱匿級研究 | 制裁照常，**暫停不受影響**，追殺不會提前恢復 | §3.4、§4.7 `BeginPermanentHostility` |
| 3 | 連續摧毀 SAGE | 首座暫停 1~2 年；暫停中每再摧毀一座 **+15 天**，接在目前結束時間之後 | §3.4、§4.6 `stackDaysWhileSuspended`、§4.7 `SuspendHunt` |
| 7 | 建築放置 | 所有既有地面設施用站點伺服主機；設施伺服核心與數據輔機放在新的地下設施「設施檔案庫」 | §6.1、§11 |
| 8 | 哪些建築影響追殺 | **只有 SAGE**；站點伺服主機與設施伺服核心被摧毀不影響追殺。中繼站站點因此移除 | §3.4、§5.4、§6.2 |
| 4 | 拆除 | 不可拆除，只能打爛；`Deconstruct` 不計入 | §5.1、§4.6 |
| 5 | 站點守軍 | 先鋒殘餘 `DMS_Legacy` | §3.7、§6.3 |
| 6 | 訪問密鑰 | 奈良／先鋒各半 | §5.2~§5.5 `keyWeights` |

> 疊加沒有上限，但 SAGE 只出現在 SAGE 節點站點（同時最多一份任務），每座只加 15 天，實際上推不遠。

### 仍可調整的數值

- **任務主動派發率 35%**：放在每次追殺觸發時，追殺間隔 4~7 天，平均約兩到三週一次。
- **防禦值／技能門檻**（1800+600、1200、2500+800；智識 6 / 4 / 10）只是起始值。`JobDriver_Hack` 每 tick 加 `HackingSpeed` 點進度，所以駭入時間 ≈ 防禦值 ÷ 駭客的 HackingSpeed（tick）；拿現有的變電箱（`DMS_SubstationCabinet`，防禦 2000）對照實測後再調。

---

## 11. 地下設施：設施檔案庫

> 新的地下設施類型 `DMS_DataArchive`（設施檔案庫），專門用來放設施伺服核心與設施數據輔機。
> 完全沿用軍事儲存庫（`DMS_MilitaryVault`）的程序化產生管線：走廊先行、房間掛在兩側、分區閘門、密封獎勵房。
> 新東西只有「房型」與「版面設定」，C# 只需要一個房間 worker 與一處型別判斷的放寬。

### 11.1 設定

殖民艦隊的設施在地表只保留最低限度的站點；真正的文書與資料主機放在地下，以隔絕溫差、震動與電磁干擾。檔案庫就是這種地下機房：一條長廊串起運維室、磁帶庫、冷卻機房，最深處是用密封門隔開的伺服機房，一台設施伺服核心被一整排數據輔機圍在中央。

地表只剩一座貨運電梯。設施的放射性電池讓它在無人職守下運轉至今，保全系統也一樣。

### 11.2 與現有地下設施的對照

| | 軍事儲存庫 `DMS_MilitaryVault` | 地下大廳 `DMS_UndergroundHall` | **設施檔案庫 `DMS_DataArchive`** |
|---|---|---|---|
| 產生方式 | 程序化（`DMS_LayoutWorker_Vault`） | 手工版面三選一 | **程序化**（同儲存庫） |
| 結構大小 | 60~70 | 45×35 級 | **50~60**（房間少、較緊湊） |
| 必出房間 | 電梯廳、軍械庫、機兵庫、獎勵房 | — | **電梯廳、運維室、伺服機房** |
| 密封獎勵房 | 獎勵房（控制台在指揮室） | — | **伺服機房**（控制台在運維室） |
| 主要回報 | 軍械、機兵、後勤終端 | 貨架補給 | **設施伺服核心 3~4 次駭入**、磁帶庫補給 |
| 警報反應 | 走廊哨站、FPV 巢 | 攝影機、封存艙 | 走廊哨站、FPV 巢、**地板開口（機兵湧出）** |
| 溫度 | 12°C | 12°C | **8°C**（機房冷卻） |
| 入口 | 儲存庫地面站、前進基地、機場 | — | **荒廢站點**（新放一座電梯） |

### 11.3 玩家流程

```
荒廢站點地表：駭入檔案庫電梯（defence 20000）
   └─► 進入口袋地圖：電梯廳（出生點，無警戒設施）
         └─► 主走廊：哨站（掩體＋哨戒砲＋震動感測器）、攝影機、FPV 巢
               └─► 分區閘門：駭入閘門控制台開門（原版 CompHackable，不拉警報）
                     ├─► 磁帶庫／冷卻機房／兵舍：補給與氣氛
                     └─► 運維室：駭入安全控制台 → 伺服機房的密封門打開
                           └─► 伺服機房：設施伺服核心 × 1、數據輔機 × 6~12、地板開口 × 1
                                 └─► 每次開始駭入核心 → 設施警報 → 開口湧出機兵、FPV 起飛、毒氣釋放口噴氣（§12）
                                 └─► 第三次駭入警戒值滿 100% → 封鎖中控倒數 1 分鐘（§13）：先駭中控，或趕在封鎖前撤離
```

### 11.4 Def 清單

新檔 `1.6/NewContent/Defs/Vault/DMS_Archive.xml`（全部放同一檔，與 `DMS_Vault_Treasury.xml` 同樣做法），除非另註。

#### MapGeneratorDef / GenStepDef

```xml
<!-- 設施檔案庫的口袋地圖。只用 Core 的地形步驟，結構由電梯的 ModExtension_PortalLayout 插入。
     Pocket map for the data archive. Core terrain steps only; the portal injects the structure. -->
<MapGeneratorDef>
  <defName>DMS_DataArchive</defName>
  <label>facility data archive</label>
  <isUnderground>true</isUnderground>
  <disableCallAid>true</disableCallAid>
  <pocketMapProperties>
    <biome>Underground</biome>
    <!-- 機房冷卻，比儲存庫再冷一點。Server cooling: a little colder than the vault. -->
    <temperature>8</temperature>
    <canBeCleaned>true</canBeCleaned>
  </pocketMapProperties>
  <genSteps>
    <li>ElevationFertility</li>
    <li>Underground_RocksFromGrid</li>
    <li>Terrain</li>
    <li>RockChunks</li>
    <!-- DMS_DataArchiveStructure 由 DMS_ArchiveElevator 動態插入 / injected by the portal -->
    <li>Fog</li>
  </genSteps>
</MapGeneratorDef>

<GenStepDef>
  <defName>DMS_DataArchiveStructure</defName>
  <order>551</order>
  <genStep Class="DMS.DMS_GenStep_Vault">
    <layoutDef>DMS_DataArchive</layoutDef>
    <!-- 房型少於儲存庫，縮一圈避免空走廊過長。Fewer room types than the vault; a size down. -->
    <sizeRange>50~60</sizeRange>
  </genStep>
</GenStepDef>
```

#### StructureLayoutDef

```xml
<StructureLayoutDef>
  <defName>DMS_DataArchive</defName>
  <workerClass>DMS.DMS_LayoutWorker_Vault</workerClass>
  <canHaveMultipleLayoutsInRoom>true</canHaveMultipleLayoutsInRoom>
  <clearRoomsEntirely>true</clearRoomsEntirely>
  <minRoomWidth>9</minRoomWidth>
  <minRoomHeight>9</minRoomHeight>
  <corridorDef>DMS_ArchiveCorridor</corridorDef>

  <wallDef>DMS_SuperWall</wallDef>
  <doorDef>Door</doorDef>
  <doorStuffDef>Steel</doorStuffDef>
  <wallLampDef>DMS_EmergencyLamp</wallLampDef>
  <terrainDef>MetalTile</terrainDef>

  <roomDefs>
    <!-- 必定出現 / Guaranteed -->
    <DMS_VaultEntranceHall><countRange>1</countRange></DMS_VaultEntranceHall>
    <DMS_ArchiveOperations><countRange>1</countRange></DMS_ArchiveOperations>
    <DMS_ArchiveServerHall><countRange>1</countRange></DMS_ArchiveServerHall>
    <DMS_ArchiveTapeStore><countRange>1~2</countRange></DMS_ArchiveTapeStore>

    <!-- 加權出現 / Weighted -->
    <DMS_ArchiveCoolingPlant>1</DMS_ArchiveCoolingPlant>
    <DMS_VaultPowerPlant>0.6</DMS_VaultPowerPlant>
    <DMS_VaultGarrisonQuarters>0.6</DMS_VaultGarrisonQuarters>
    <DMS_VaultWreckHall>0.4</DMS_VaultWreckHall>
  </roomDefs>

  <junkScaterrers>
    <Filth_Dirt>
      <groupsPerHundredCells>1~3</groupsPerHundredCells>
      <itemsPerGroup>2~4</itemsPerGroup>
      <groupDistRange>3~5</groupDistRange>
    </Filth_Dirt>
    <Filth_MachineBits>
      <groupsPerHundredCells>0~2</groupsPerHundredCells>
      <itemsPerGroup>2~4</itemsPerGroup>
      <groupDistRange>2~4</groupDistRange>
    </Filth_MachineBits>
  </junkScaterrers>

  <modExtensions>
    <li Class="DMS.ModExtension_VaultLayout">
      <!-- 伺服機房要塞 2x2 核心＋兩圈機櫃，房間下限拉到 11。
           The server hall fits a 2x2 core plus two rings of racks, so rooms start at 11. -->
      <roomWidthRange>11~14</roomWidthRange>
      <roomDepthRange>11~14</roomDepthRange>
      <branchCountRange>2~2</branchCountRange>
      <subBranchChance>0.2</subBranchChance>
      <branchSpacing>18</branchSpacing>
      <!-- 不合併：沒有大型房型要用。No merging: there are no large room types here. -->
      <mergeChance>0</mergeChance>
      <gapChance>0.1</gapChance>
      <endRooms>true</endRooms>
      <sectorGates>true</sectorGates>
      <gateChance>1</gateChance>
      <gateDoorDefs>
        <li>DMS_SealedRollingDoor_Single</li>
        <li>DMS_SealedRollingDoor_Double</li>
        <li>DMS_SealedRollingDoor_Triple</li>
        <li>DMS_SealedRollingDoor_Quadruple</li>
      </gateDoorDefs>
      <gateConsoleDef>DMS_VaultGateConsole</gateConsoleDef>
    </li>
    <!-- 機房的門全部維持自動門：設施仍有電。All autodoors: the archive still has power. -->
    <li Class="DMS.ModExtension_VaultDoors">
      <reinforcedDoorDef>Autodoor</reinforcedDoorDef>
      <reinforcedDoorStuff>Steel</reinforcedDoorStuff>
      <reinforcedDoorRatio>0.8</reinforcedDoorRatio>
    </li>
  </modExtensions>
</StructureLayoutDef>
```

> 動工前先看 `DMS_LayoutWorker_Vault` / `VaultLayoutGenerator` 是否對房型 defName 有寫死的判斷；目前只看到 `corridorDef` 與 `RoomContents_VaultTreasury` 的型別判斷（後者見 §11.5）。

#### LayoutRoomDef

| defName | 說明 | worker | 內容 |
|---|---|---|---|
| `DMS_ArchiveCorridor` | 主走廊 | `DMS.RoomContents_VaultMainHall`（沿用） | 複製 `DMS_VaultMainHall` 的 `ModExtension_VaultSecurity`，改：`nestChance 0.7`、`turretWreckChance 0.2`、`checkpointSpacing 16`；prefab 換成 `DMS_Prefab_VentBank` + `DMS_Prefab_DataRackRow_Edge`（0~2） |
| `DMS_VaultEntranceHall` | 電梯廳（沿用） | `RoomContents_VaultEntrance` | 出口讀自電梯的 `portal.exitDef`，不需改 |
| `DMS_ArchiveOperations` | 運維室：放伺服機房的安全控制台 | 無（純 prefab） | `DMS_Prefab_ArchiveDeskRow`（文書桌＋終端）1~2、`DMS_Prefab_SubstationBank` 1、`DMS_Prefab_LockerRow` 0~1 |
| `DMS_ArchiveServerHall` | 伺服機房（密封） | `DMS.RoomContents_ArchiveServerHall`（新） | 見 §11.5；`minSingleRectWidth/Height 11` |
| `DMS_ArchiveTapeStore` | 磁帶庫 | `DMS.RoomContents_ArchiveTapeStore`（新，見 §11.5） | `DMS_Prefab_DataRackRow` 2~3、`fillEdges` 放 `Shelf` 2~4 座；貨架上的物資由 worker 以 `DMS_ArchiveShelfLoot` 填入 |
| `DMS_ArchiveCoolingPlant` | 冷卻機房 | 無 | `DMS_Prefab_VentBank` 2~3、`DMS_Prefab_MachineRow_Live` 1、`DMS_Prefab_ArchiveFloorHole` 0~1 |

伺服機房的 `ModExtension_VaultTreasury`：

```xml
<li Class="DMS.ModExtension_VaultTreasury">
  <sealedDoorDef>DMS_SealedRollingDoor_Single</sealedDoorDef>
  <consoleDef>DMS_VaultConsole</consoleDef>
  <preferredConsoleRoom>DMS_ArchiveOperations</preferredConsoleRoom>
</li>
```

#### PrefabDef

```xml
<!-- 數據輔機一排四台，面向房內。1x2 的機櫃並排成 4x2。
     A row of four data racks facing into the room; 1x2 racks side by side make 4x2. -->
<PrefabDef>
  <defName>DMS_Prefab_DataRackRow</defName>
  <size>(4,2)</size>
  <things>
    <DMS_FacilityDataRack>
      <positions>
        <li>(0, 0, 0)</li>
        <li>(1, 0, 0)</li>
        <li>(2, 0, 0)</li>
        <li>(3, 0, 0)</li>
      </positions>
    </DMS_FacilityDataRack>
  </things>
</PrefabDef>

<!-- 靠牆版本：最上排貼牆，機櫃正面朝房內。Wall version: the back row hugs the wall, fronts facing in. -->
<PrefabDef>
  <defName>DMS_Prefab_DataRackRow_Edge</defName>
  <edgeOnly>true</edgeOnly>
  <size>(4,2)</size>
  <things>
    <DMS_FacilityDataRack>
      <relativeRotation>Opposite</relativeRotation>
      <positions>
        <li>(0, 0, 0)</li>
        <li>(1, 0, 0)</li>
        <li>(2, 0, 0)</li>
        <li>(3, 0, 0)</li>
      </positions>
    </DMS_FacilityDataRack>
  </things>
</PrefabDef>

<!-- 檢修開口：警報時機兵從這裡爬上來。Service opening: mechs climb out of it on alarm. -->
<PrefabDef>
  <defName>DMS_Prefab_ArchiveFloorHole</defName>
  <size>(3,3)</size>
  <things>
    <DMS_Hole_3x3Empty>
      <position>(1, 0, 1)</position>
    </DMS_Hole_3x3Empty>
  </things>
</PrefabDef>
```

> 1×2 機櫃在 prefab 裡的 position 是「中心格」還是「左下角」依原版 PrefabDef 規則（多格物件以 `position` 為錨點、依 `size` 與旋轉展開）。實作時先在開發模式用 `DebugActions → Spawn prefab` 確認排列沒有重疊，再決定 x 間距是 1 還是 2。
> `DMS_Prefab_ArchiveDeskRow`：從現有家具（`DMS_ExplorationBuildings_Furniture.xml`）挑文書桌與終端組合，依 `DMS_Prefab_LockerRow` 的靠牆寫法做。

#### ThingSetMakerDef `DMS_ArchiveShelfLoot`

照 `DMS_UnderMapShelfLoot` 的結構，每項獨立擲骰：

| 內容 | 機率 | 數量 |
|---|---|---|
| `ComponentIndustrial` | 0.7 | 3~8 |
| `DMS_AccessKey_Nara` / `DMS_AccessKey_Neutrals`（各半） | 0.35 | 1 |
| `DMS_DataCache_Restricted` | 0.15 | 1 |
| `Steel` | 0.5 | 30~60 |
| 醫藥（`MedicineIndustrial`） | 0.3 | 2~4 |

> 技術匣只放管制級，封存級留給伺服核心，避免磁帶庫比核心更划算。

#### 地表電梯 `DMS_ArchiveElevator`

放在 `DMS_Vault_Portal.xml`。先把 `DMS_VaultElevator` 抽出抽象父 `DMS_ElevatorBase`（graphic、size、building、Sealable、Glower、SealedGraphic），兩座電梯都繼承它，只覆寫下列欄位：

```xml
<ThingDef ParentName="DMS_ElevatorBase">
  <defName>DMS_ArchiveElevator</defName>
  <label>archive freight elevator</label>
  <description>(見 §11.9)</description>
  <portal>
    <pocketMapGenerator>DMS_DataArchive</pocketMapGenerator>
    <exitDef>DMS_VaultElevatorExit</exitDef>
    <pocketMapSize>120</pocketMapSize>
  </portal>
  <comps>
    <li Class="CompProperties_Hackable">
      <!-- 比儲存庫電梯（39000）少一半：檔案庫真正的難關在下面。
           Half the vault lift's 39000: the real obstacle is below. -->
      <defence>20000</defence>
      <effectHacking>HackingTerminal</effectHacking>
      <lockoutDurationHoursRange>6~18</lockoutDurationHoursRange>
      <showProgressAfterHackCompletion>false</showProgressAfterHackCompletion>
      <notHackedInspectString>Hack to open.</notHackedInspectString>
      <intellectualSkillPrerequisite>4</intellectualSkillPrerequisite>
      <hackingCompletedSound>Hacking_Completed</hackingCompletedSound>
      <hackedMessage>{HACKER_labelShort} has finished hacking {SUBJECT_labelNoParenthesisDef}.</hackedMessage>
    </li>
  </comps>
  <modExtensions>
    <li Class="DMS.ModExtension_PortalLayout">
      <genStep>DMS_DataArchiveStructure</genStep>
      <layout>DMS_DataArchive</layout>
    </li>
  </modExtensions>
</ThingDef>
```

> 抽象父的 `comps` 若含 `CompProperties_Hackable`，子類覆寫時要 `Inherit="False"` 或把 Hackable 留在各自的子 Def，避免兩份 Hackable。建議 Hackable 不放進父類。
> `DMS_VaultElevatorExit` 可以直接共用：出口只負責回到地表，與口袋地圖類型無關。

### 11.5 C# 實作

#### 放寬獎勵房的型別判斷

`RoomContents_VaultTreasury.cs` 裡 `VaultTreasuryUtility` 有兩處用「型別相等」找獎勵房（`d.roomContentsWorkerType == typeof(RoomContents_VaultTreasury)`，以及排除候選控制台房間時的 `worker == typeof(RoomContents_VaultTreasury)`）。改成：

```csharp
private static bool IsTreasuryWorker(System.Type worker)
{
    return worker != null && typeof(RoomContents_VaultTreasury).IsAssignableFrom(worker);
}
```

兩處都換成 `IsTreasuryWorker(...)`。這樣伺服機房繼承獎勵房之後，就能沿用「密封門＋控制台放在偏好房間＋只有唯一通道時不封」整套邏輯。軍事儲存庫的行為不變。

#### `RoomContents_ArchiveServerHall.cs`

```csharp
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 設施檔案庫的伺服機房：中央一台設施伺服核心，四周排數據輔機，角落一個檢修開口。
    /// 繼承獎勵房，所以密封門與運維室的安全控制台由 VaultTreasuryUtility 一併處理。
    /// 核心不掛防務陣營：它是駭入目標，不是防務設施；開口掛防務陣營，警報時機兵才會是敵對的。
    ///
    /// The archive's server hall: one facility server core in the middle, data racks around it and a
    /// service opening in a corner. Derives from the treasury, so VaultTreasuryUtility handles the sealed
    /// doors and the security console in the operations room. The core stays factionless (it is a hack
    /// target, not a defence); the opening gets the defender faction so whatever climbs out is hostile.
    /// </summary>
    public class RoomContents_ArchiveServerHall : RoomContents_VaultTreasury
    {
        private const string CoreDefName = "DMS_FacilityServerCore";
        private const string HoleDefName = "DMS_Hole_3x3Empty";

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            // 先放核心：它最大，放不下時退而縮小留白，最後手段直接放在最大矩形中央。
            // Core first: it's the biggest piece. Shrink the padding if needed, else force it at the centre.
            if (!TryPlaceCore(map, room))
            {
                Log.Warning($"[DMS] Archive server hall {room.id}: could not place {CoreDefName}.");
            }

            // 開口：一個，掛防務陣營。One opening, defender faction.
            Faction defenders = faction ?? VaultRoomUtility.DefenderFaction;
            List<Thing> holes = new List<Thing>();
            RoomGenUtility.FillWithPadding(VaultRoomUtility.ThingNamed(HoleDefName), 1, room, map, null, null, holes, 1);
            foreach (Thing h in holes) VaultRoomUtility.SetDefenderFaction(h, defenders);

            // 機櫃列與其他裝飾交給 LayoutRoomDef 的 prefabs（base.FillRoom）。
            // Rack rows and dressing come from the room def's prefabs via base.FillRoom.
            base.FillRoom(map, room, faction, threatPoints);
        }

        private static bool TryPlaceCore(Map map, LayoutRoom room)
        {
            ThingDef core = VaultRoomUtility.ThingNamed(CoreDefName);
            if (core == null) return false;

            List<Thing> spawned = new List<Thing>();
            for (int padding = 3; padding >= 1 && spawned.Count == 0; padding--)
            {
                RoomGenUtility.FillWithPadding(core, 1, room, map, null, null, spawned, padding);
            }
            if (spawned.Count > 0) return true;

            CellRect largest = room.rects.MaxBy(r => r.Area);
            GenSpawn.Spawn(ThingMaker.MakeThing(core), largest.CenterCell, map, Rot4.North);
            return true;
        }
    }
}
```

#### `RoomContents_ArchiveTapeStore.cs`

`fillEdges` 只能放建築，不能指定 ThingSetMaker，所以磁帶庫需要一個很薄的 worker，沿用 `VaultRoomUtility.PlaceOnShelves`：

```csharp
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 磁帶庫：機櫃列與貨架交給 LayoutRoomDef（prefabs / fillEdges），這裡只把 DMS_ArchiveShelfLoot 擺上貨架。
    /// Tape store: racks and shelves come from the room def; this only stocks the shelves from DMS_ArchiveShelfLoot.
    /// </summary>
    public class RoomContents_ArchiveTapeStore : RoomContentsWorker
    {
        private const string LootDefName = "DMS_ArchiveShelfLoot";

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            base.FillRoom(map, room, faction, threatPoints);   // 先有貨架 / shelves first

            ThingSetMakerDef loot = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail(LootDefName);
            if (loot == null) return;
            List<Thing> items = loot.root.Generate(new ThingSetMakerParams());
            if (!VaultRoomUtility.PlaceOnShelves(map, room, items))
            {
                // 沒有貨架可放：散落在房內地上 / No shelf space: drop them on the floor.
                foreach (Thing t in items)
                    if (!t.Spawned) GenPlace.TryPlaceThing(t, room.rects[0].CenterCell, map, ThingPlaceMode.Near);
            }
        }
    }
}
```

> `PlaceOnShelves` 會把放上貨架的物品從清單移除，並在貨架滿時回傳 false，所以 fallback 迴圈裡剩下的都是還沒生成的物品。

`DMS_ArchiveServerHall` 的 prefab：

```xml
<prefabs>
  <DMS_Prefab_DataRackRow_Edge>
    <countPerTenEdgeCells>1</countPerTenEdgeCells>
    <minMaxRange>2~3</minMaxRange>
  </DMS_Prefab_DataRackRow_Edge>
  <DMS_Prefab_DataRackRow>
    <countPerHundredCells>2</countPerHundredCells>
    <minMaxRange>0~1</minMaxRange>
  </DMS_Prefab_DataRackRow>
</prefabs>
```

> 核心先放、prefab 後放：原版 prefab 放置會避開已佔用格，所以機櫃會自動繞開核心與它的互動格。
> 核心的 `hasInteractionCell` 在 §5.1 設為 `(0,0,-1)`，南側要留空；`FillWithPadding` 的 padding ≥ 1 可保證互動格在房內，實作後用開發模式檢查互動格沒有被機櫃佔住。

#### 伺服機房要在「深處」

獎勵房目前沒有位置偏好，伺服機房可能被排在電梯廳隔壁。兩個作法，擇一：

1. **先不處理**：分區閘門＋密封門＋控制台在運維室，已經保證玩家至少過兩道關卡。先實測。
2. **加一個選配擴充** `ModExtension_VaultRoomPlacement { bool farthestFromEntrance; }`：在 `DMS_LayoutWorker_Vault.GenerateStructure` 之後，從電梯廳沿 `room.connections` 做 BFS，把 `requiredDef` 為伺服機房的房間與「距離最遠、尺寸合格」的房間交換 `requiredDef`。只在掛了這個擴充的房型上生效，儲存庫不受影響。

建議先走 1，實測三到五張地圖，若常出現「伺服機房緊鄰入口」再做 2。

### 11.6 警報與防務

檔案庫地圖上會回應伺服核心警報的東西：

| 來源 | 由誰生成 | 反應 |
|---|---|---|
| 主走廊哨站（掩體＋哨戒砲＋震動感測器） | `RoomContents_VaultMainHall`（`ModExtension_VaultSecurity`） | 砲塔直接開火；感測器累加警戒值 |
| 走廊端牆攝影機 | 同上 | 偵測與累加警戒值 |
| FPV 巢（每個哨站 70%） | 同上 | 收到 `FFF_AlertScanner_Triggered` 放出 FPV |
| **毒氣釋放口**（走廊哨站周邊、伺服機房核心兩側） | §12.5 | 預警 2 秒後噴毒氣，存量 3 發 |
| **設施封鎖中控**（運維室） | §13.6 | 警戒值滿 100% 後倒數 1 分鐘封死電梯；駭入解除 |
| **伺服機房、冷卻機房的地板開口** | `RoomContents_ArchiveServerHall`、`DMS_Prefab_ArchiveFloorHole` | `CompAlertEffector_HoleEmerge`：依警戒值 150~600 點的機兵從洞裡跳出 |

因此設施伺服核心：

- **不設 `fallbackRaidWave`**（§5.2 已改）。地圖上一定有 FPV 巢或開口能接住訊號；空投又穿不過岩頂。
- `alertIncrement 35`：第三次開始駭入時警戒值滿 100，觸發 FFF 的全圖警報效果。動工時確認口袋地圖的 `MapComponent_AlertCounter.effectWorkers` 有註冊；沒有的話 FFF 只會記一條 warning，不會壞，但也不會有全圖效果（儲存庫目前是同樣狀況）。

節奏：第 1 次駭入 → 開口與 FPV 反應（低點數）；第 2 次 → 點數更高；第 3 次 → 全圖警報；第 4 次（若有）→ 警戒值已滿，開口依上限點數反應。

### 11.7 地表入口

- **荒廢站點 `DMS_SiteStation`**：在版面內找一處半毀的室內空間放 `DMS_ArchiveElevator`（5×5）。用遊戲內 FFF 工具加一個 `FFF_Element_Thing` 後重新匯出該 `FFF_StructureDef`，不要手改 4500 行的版面。
- 電梯不掛防務陣營、不可摧毀（沿用 `DMS_VaultElevator` 的設定）。
- 其他入口（選配）：之後若要讓機場或前進基地也可能通往檔案庫，最省事的作法是在 `ModExtension_PortalLayout` 加 `List<LayoutOption> options`（layout + genStep + mapGenerator + weight），讓一座電梯在第一次進入時擲骰決定通往哪一種地下設施。本計畫不做。

### 11.8 回報總覽（單座檔案庫）

| 來源 | 期望值 |
|---|---|
| 設施伺服核心 3~4 次 | 科技藍圖 ~1.5、封存技術匣 ≤2、訪問密鑰 ≤2 |
| 磁帶庫 1~2 間 | 零件、鋼、少量醫藥，偶爾一把密鑰或管制級技術匣 |
| 閘門控制台、安全控制台 | 無直接回報（通行用） |
| 數據輔機 | 無（純裝飾）；打爛掉 `killedLeavings` 的鋼渣與零件 |

### 11.9 文本

#### `DMS_ArchiveElevator`

| | 內容 |
|---|---|
| label EN | archive freight elevator |
| label 繁中 | 檔案庫貨運電梯 |
| description EN | The surface lift of a buried facility data archive. The colony fleet kept its records hosts underground, away from temperature swings, tremors and interference, and left only this lift up top. The car still has power, but its controls are locked. Hack it to bring the car up. |
| description 繁中 | 一座地下設施檔案庫的地表電梯。殖民艦隊把文書與資料主機設在地下，以隔絕溫差、震動與電磁干擾，地表只留下這座電梯。車廂仍然有電，但控制系統已經上鎖，必須駭入才能把車廂叫上來。 |

#### `DMS_DataArchive`（MapGeneratorDef label，顯示於地圖分頁）

| EN | 繁中 |
|---|---|
| facility data archive | 設施檔案庫 |

#### 荒廢站點任務描述補句

接在 `questDescription(installation==DMS_SiteStationSite)` 的「Caution」段落之前：

> EN: \n\nOne of the station's freight elevators still runs down to a data archive the fleet buried under it. A facility server core is kept in the sealed server hall at the bottom; it can be hacked several times, and every attempt will bring the archive's defences up from below.
>
> 繁中：\n\n站內還有一座貨運電梯通往艦隊埋在地下的設施檔案庫。最深處的密封伺服機房裡放著一台設施伺服核心，可以駭入數次，但每一次都會讓檔案庫的保全系統從地底湧出。

#### 伺服機房相關訊息

沿用 §7.5 的 `DMS_ServerHack_*` 與 FFF／儲存庫既有的 `DMS_HoleEmerge_Triggered`，不需要新 Keyed。

### 11.10 實作清單

- [ ] D1 `VaultTreasuryUtility`：兩處型別判斷改 `IsTreasuryWorker`；確認軍事儲存庫生成不變
- [ ] D2 `RoomContents_ArchiveServerHall.cs`、`RoomContents_ArchiveTapeStore.cs`
- [ ] D3 `DMS_Archive.xml`：MapGeneratorDef、GenStepDef、StructureLayoutDef、6 個 LayoutRoomDef（含沿用的 2 個不需新增）、4 個 PrefabDef、`DMS_ArchiveShelfLoot`
- [ ] D4 `DMS_Vault_Portal.xml`：抽出 `DMS_ElevatorBase`，新增 `DMS_ArchiveElevator`；確認 `DMS_VaultElevator` 與 `DMS_VaultElevator_Sealed` 的 `unlockedDef` 仍正確
- [ ] D5 `DMS_SiteStation` 版面放入 `DMS_ArchiveElevator`（遊戲內匯出）
- [ ] D6 三語 DefInjected：`ThingDef/DMS_Vault_Portal.xml`（電梯）、`MapGeneratorDef/DMS_Archive.xml`；荒廢站點任務描述補句
- [ ] D7 （視實測）`ModExtension_VaultRoomPlacement`
- [ ] D8 毒氣釋放口：依 §12.7 的清單實作（G4、G5 屬於檔案庫的配置）
- [ ] D9 設施封鎖中控：先完成 §13.9 的 FFF 部分（F1~F7），再做 DMS 部分（L3 包含檔案庫與儲存庫的放置）

### 11.11 測試

| # | 情境 | 預期 |
|---|---|---|
| A1 | 開發模式放 `DMS_ArchiveElevator`，駭入後進入 | 產生 50~60 的程序化結構；出生點在電梯廳；溫度約 8°C |
| A2 | 連續產生 10 張 | 每張都有電梯廳、運維室、伺服機房各 1 間，磁帶庫 1~2 間；log 無 `unreachable` 警告 |
| A3 | 伺服機房 | 核心 × 1、機櫃 ≥ 8 台、開口 × 1；核心互動格未被佔住；門是密封門 |
| A4 | 駭入運維室的安全控制台 | 伺服機房密封門打開 |
| A5 | 伺服機房是唯一通道的地圖 | 依獎勵房規則不封門（log 有說明） |
| A6 | 開始駭入核心 | 開口湧出機兵、FPV 巢起飛；**沒有**空投援軍 |
| A7 | 第三次開始駭入 | 警戒值滿 100；FFF 全圖效果或一條 `no effectWorkers` warning |
| A8 | 生成一座軍事儲存庫 | 獎勵房、控制台行為與改動前相同（D1 回歸） |
| A9 | 荒廢站點任務 | 地表有檔案庫電梯；任務描述出現補句 |
| A10 | 封閉電梯豎井 | 沿用 `CompSealable`，口袋地圖移除 |

---

## 12. 警報建築：毒氣釋放口

> 新的警報反應建築 `DMS_Building_GasVent`（毒氣釋放口），1×1，貼圖 `Things/Building/DMS_Building_GasVent`（已在 `1.6/NewContent/Textures/Things/Building/`）。
> 收到設施警報後，短暫預警、再從地板噴出毒氣。主要配置在設施檔案庫（§11），也可以放進任何室內版面。

### 12.1 設計

| 項目 | 設定 | 理由 |
|---|---|---|
| 觸發 | 聽 `FFF_AlertScanner_Triggered`，`triggerChance 0.8`，可重複 | 與其他警報反應建築同一套訊號 |
| 預警 | **2 秒**（120 tick）嘶聲＋黃色閃爍，之後才噴 | 給玩家反應時間，毒氣不是瞬間秒殺 |
| 釋放量 | `cellsToFill 24`、持續 12 秒 | 約填滿一間 5×5 房或一段走廊 |
| 氣體 | `ToxGas`（原版毒氣） | 視覺、擴散、消散都用原版 gasGrid |
| 存量 | **3 次**，每 1 天回補 1 次 | 不會無限噴；被反覆觸發時會用完 |
| 冷卻 | 釋放結束後 `600` tick 才能再觸發 | 避免同一波警報連噴 |
| 停用 | 斷電、EMP／Stun、休眠 | 由 FFF `AlertBuildingUtility.IsOperational` 統一判定；**釋放中斷電也會立刻停** |
| 敵我 | **不分敵我**，但守軍是機兵 | 機兵對毒氣免疫，對防守方幾乎無害；殖民者要戴防毒面具或避開 |
| 可見性 | 地板格柵，可站立、不擋路 | 玩家看得到位置，能先斷電或打爛 |

> 室外效果差：原版毒氣在無屋頂格會很快消散。設計上它就是室內／地下設施的陷阱，地表版面只放在有屋頂的房間裡。

### 12.2 無 Biotech 時的毒氣

原版 `GasUtility.PawnGasEffectsTickInterval` 只在 `ModsConfig.BiotechActive` 時讓毒氣造成毒素累積，沒有 Biotech 的存檔裡毒氣只是好看。

補救：`MapComponent_DMSToxGasFallback`，只在 **無 Biotech** 且**這張地圖有毒氣釋放口噴過**之後的一段時間內運作，每 50 tick 用與原版相同的公式處理：

```
density = cell 的 ToxGas 濃度 / 255
若 ToxicBuildup 已到最終階段 → density × 0.25
ToxicUtility.DoPawnToxicDamage(pawn, density)
```

- 只做毒素累積，不加 `ToxGasExposure`（該 hediff 屬於 Biotech）。
- 判斷對象沿用 `GasUtility.IsAffectedByExposure`（人形生物、未穿戴防毒裝備）＋動物；機兵的 `ToxicResistance` 讓 `DoPawnToxicDamage` 本來就無效。
- 啟用時間窗：每次噴氣延長到 `now + 2 天`，窗口外完全不跑，不影響一般存檔效能。
- 有 Biotech 時整個 MapComponent 直接 return，交給原版，**不會重複傷害**。

### 12.3 C# 實作

新檔 `_Source/DMS/CompAlertEffector_GasVent.cs`（與其他 `CompAlertEffector_*` 同目錄）。

```csharp
using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DMS
{
    public class CompProperties_AlertEffector_GasVent : CompProperties_AlertEffector
    {
        public GasType gasType = GasType.ToxGas;
        /// <summary>一次噴出的量，以「填滿幾格」計。Amount per release, in cells filled.</summary>
        public float cellsToFill = 24f;
        public float durationSeconds = 12f;

        /// <summary>預警時間。Warning time before gas comes out.</summary>
        public int warmupTicks = 120;
        /// <summary>釋放結束後的冷卻。Cooldown after a release ends.</summary>
        public int cooldownTicks = 600;

        public int maxCharges = 3;
        /// <summary>每回補一次所需 ticks；0 = 不回補。Ticks per recharge; 0 = never.</summary>
        public int rechargeTicks = 60000;

        public SoundDef warmupSound;
        public EffecterDef warmupEffecter;
        public EffecterDef releasingEffecter;

        public CompProperties_AlertEffector_GasVent()
        {
            compClass = typeof(CompAlertEffector_GasVent);
        }
    }

    /// <summary>
    /// 毒氣釋放口：收到警報後預警、再從所在格釋放氣體。存量有限、會慢慢回補；
    /// 斷電或被 EMP 時停止（包括釋放到一半）。
    /// Gas vent: on alarm, warns briefly and then releases gas from its cell. Limited charges that
    /// slowly refill; stops when unpowered or EMP'd, even mid-release.
    /// </summary>
    public class CompAlertEffector_GasVent : CompAlertEffector
    {
        private const int ReleaseInterval = 30;

        private int warmupLeft = -1;
        private int remainingGas;
        private int cooldownUntil = -1;
        private int charges = -1;
        private int rechargeProgress;

        [Unsaved] private Effecter effecter;

        public new CompProperties_AlertEffector_GasVent Props => (CompProperties_AlertEffector_GasVent)props;

        private int TotalGas => Mathf.CeilToInt(Props.cellsToFill * 255f);
        private int GasPerInterval => Mathf.Max(1, Mathf.RoundToInt(TotalGas / (Props.durationSeconds * 60f) * ReleaseInterval));
        private bool Busy => warmupLeft >= 0 || remainingGas > 0;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (charges < 0) charges = Props.maxCharges;
        }

        protected override void DoEffect()
        {
            if (Busy || charges <= 0) return;
            if (Find.TickManager.TicksGame < cooldownUntil) return;

            charges--;
            warmupLeft = Props.warmupTicks;
            Props.warmupSound?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            SwapEffecter(Props.warmupEffecter);
            Messages.Message("DMS_GasVent_Warning".Translate(parent.Named("VENT")),
                parent, MessageTypeDefOf.ThreatSmall, historical: false);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned) return;

            TickRecharge();
            if (!Busy) return;

            // 斷電／EMP／休眠：立刻中止，剩餘量作廢（這一發已經扣掉存量）。
            // Unpowered / EMP'd / dormant: abort at once; the rest of this charge is lost.
            if (!AlertBuildingUtility.IsOperational(parent))
            {
                Stop();
                return;
            }

            // effecter 不存檔，讀檔後在這裡重建。The effecter isn't saved; rebuild it after a load.
            if (effecter == null) SwapEffecter(warmupLeft >= 0 ? Props.warmupEffecter : Props.releasingEffecter);
            effecter?.EffectTick(parent, TargetInfo.Invalid);

            if (warmupLeft >= 0)
            {
                if (--warmupLeft < 0)
                {
                    remainingGas = TotalGas;
                    SwapEffecter(Props.releasingEffecter);
                }
                return;
            }

            if (parent.IsHashIntervalTick(ReleaseInterval))
            {
                int amount = Mathf.Min(remainingGas, GasPerInterval);
                GasUtility.AddGas(parent.Position, parent.Map, Props.gasType, amount);
                remainingGas -= amount;
                if (Props.gasType == GasType.ToxGas)
                {
                    MapComponent_DMSToxGasFallback.Notify_GasReleased(parent.Map);
                }
                if (remainingGas <= 0) Stop();
            }
        }

        private void TickRecharge()
        {
            if (Props.rechargeTicks <= 0 || charges >= Props.maxCharges) return;
            if (++rechargeProgress >= Props.rechargeTicks)
            {
                rechargeProgress = 0;
                charges++;
            }
        }

        private void Stop()
        {
            warmupLeft = -1;
            remainingGas = 0;
            cooldownUntil = Find.TickManager.TicksGame + Props.cooldownTicks;
            SwapEffecter(null);
        }

        private void SwapEffecter(EffecterDef def)
        {
            effecter?.Cleanup();
            effecter = def?.Spawn(parent, parent.Map);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            SwapEffecter(null);
        }

        public override string CompInspectStringExtra()
        {
            string s = "DMS_GasVent_Charges".Translate(charges, Props.maxCharges);
            if (warmupLeft >= 0) s += "\n" + "DMS_GasVent_Arming".Translate().Colorize(ColorLibrary.RedReadable);
            else if (remainingGas > 0) s += "\n" + "DMS_GasVent_Releasing".Translate().Colorize(ColorLibrary.RedReadable);
            return s;
        }

        public override System.Collections.Generic.IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action { defaultLabel = "DEV: Vent now", action = () => { cooldownUntil = -1; if (charges <= 0) charges = 1; DoEffect(); } };
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref warmupLeft, "dms_warmupLeft", -1);
            Scribe_Values.Look(ref remainingGas, "dms_remainingGas", 0);
            Scribe_Values.Look(ref cooldownUntil, "dms_cooldownUntil", -1);
            Scribe_Values.Look(ref charges, "dms_charges", -1);
            Scribe_Values.Look(ref rechargeProgress, "dms_rechargeProgress", 0);
        }
    }
}
```

> `CompInspectStringExtra` 對玩家與非玩家都顯示，讓玩家看得出還剩幾發。
> `IsOperational` 在 FFF 裡是 `AlertBuildingUtility.IsOperational(ThingWithComps)`；動工時確認可見性（若是 internal，改自己判斷 `CompPowerTrader.PowerOn`、`CompStunnable.StunHandler.Stunned`、`CompCanBeDormant.Awake`）。

新檔 `_Source/DMS/MapComponent_DMSToxGasFallback.cs`：

```csharp
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 沒有 Biotech 時，原版毒氣不造成任何傷害。毒氣釋放口噴過之後的一段時間內，
    /// 用與原版相同的公式替它補上毒素累積。有 Biotech 時完全不動作。
    /// Without Biotech, vanilla tox gas does nothing. For a while after a gas vent fires, apply
    /// toxic buildup with vanilla's own formula. Does nothing at all when Biotech is active.
    /// </summary>
    public class MapComponent_DMSToxGasFallback : MapComponent
    {
        private const int Interval = 50;
        private const int ActiveWindowTicks = 2 * GenDate.TicksPerDay;
        private const float ExtremeBuildupFactor = 0.25f;

        private int activeUntil = -1;

        public MapComponent_DMSToxGasFallback(Map map) : base(map) { }

        public static void Notify_GasReleased(Map map)
        {
            if (ModsConfig.BiotechActive || map == null) return;
            MapComponent_DMSToxGasFallback comp = map.GetComponent<MapComponent_DMSToxGasFallback>();
            if (comp != null) comp.activeUntil = Find.TickManager.TicksGame + ActiveWindowTicks;
        }

        public override void MapComponentTick()
        {
            if (ModsConfig.BiotechActive) return;
            int now = Find.TickManager.TicksGame;
            if (now > activeUntil || now % Interval != 0) return;

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!Affected(pawn)) continue;
                byte density = pawn.Position.GasDensity(map, GasType.ToxGas);
                if (density == 0) continue;

                float f = density / 255f;
                Hediff buildup = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup);
                if (buildup != null && buildup.CurStageIndex == buildup.def.stages.Count - 1) f *= ExtremeBuildupFactor;
                ToxicUtility.DoPawnToxicDamage(pawn, f);
            }
        }

        private static bool Affected(Pawn pawn)
        {
            if (pawn.RaceProps.IsMechanoid) return false;
            if (pawn.RaceProps.Humanlike) return GasUtility.IsAffectedByExposure(pawn);   // 防毒面具等 / gas masks etc.
            return pawn.RaceProps.Animal;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref activeUntil, "dms_toxFallbackUntil", -1);
        }
    }
}
```

> `AllPawnsSpawned` 只在窗口內、每 50 tick 走一次，地下口袋地圖的 pawn 數量很少，成本可以忽略。
> 原版 `ToxicUtility.DoPawnToxicDamage` 位於 Core，無 Biotech 也可以呼叫；動工時用 decompiler 再確認一次沒有 `BiotechActive` 閘門。

### 12.4 XML

放在 `1.6/NewContent/Defs/Things_Building/DMS_ExplorationBuildings_Alert.xml`，接在震動傳感器之後。

```xml
<!-- 毒氣釋放口：地板格柵，收到警報後預警 2 秒、再噴 12 秒毒氣。存量 3 發、每天回補 1 發。
     不分敵我，但守軍是機兵，毒氣實際上只傷得到入侵者。斷電或 EMP 會讓它停下，連噴到一半的也一樣。
     Gas vent: a floor grate. On alarm it warns for 2 seconds, then vents tox gas for 12. Three charges,
     one refilled per day. Indiscriminate, but the garrison is mechanical, so in practice only intruders
     choke. Cutting power or an EMP stops it, even mid-release. -->
<ThingDef ParentName="DMS_AlertBuildingBase">
  <defName>DMS_Building_GasVent</defName>
  <label>gas vent</label>
  <description>(見 §12.6)</description>
  <graphicData>
    <texPath>Things/Building/DMS_Building_GasVent</texPath>
    <graphicClass>Graphic_Single</graphicClass>
    <drawSize>(1,1)</drawSize>   <!-- 依貼圖實際大小調整 / tune to the art -->
    <damageData>
      <enabled>false</enabled>
    </damageData>
  </graphicData>
  <size>(1,1)</size>
  <rotatable>false</rotatable>
  <!-- 地板格柵：可站立、不擋路、不擋視線。A floor grate: standable, no path cost, no cover. -->
  <altitudeLayer>FloorEmplacement</altitudeLayer>
  <passability>Standable</passability>
  <pathCost>0</pathCost>
  <fillPercent>0</fillPercent>
  <blockWind>false</blockWind>
  <castEdgeShadows>false</castEdgeShadows>
  <staticSunShadowHeight>0</staticSunShadowHeight>
  <statBases>
    <MaxHitPoints>200</MaxHitPoints>
  </statBases>
  <costList>
    <Steel>40</Steel>
    <ComponentIndustrial>2</ComponentIndustrial>
  </costList>
  <building>
    <isEdifice>false</isEdifice>
    <claimable>false</claimable>
  </building>
  <comps>
    <li Class="CompProperties_Power">
      <compClass>CompPowerTrader</compClass>
      <basePowerConsumption>50</basePowerConsumption>
      <shortCircuitInRain>false</shortCircuitInRain>
    </li>
    <li Class="DMS.CompProperties_AlertEffector_GasVent">
      <listenSignal>FFF_AlertScanner_Triggered</listenSignal>
      <triggerChance>0.8</triggerChance>
      <oneShot>false</oneShot>
      <gasType>ToxGas</gasType>
      <cellsToFill>24</cellsToFill>
      <durationSeconds>12</durationSeconds>
      <warmupTicks>120</warmupTicks>
      <cooldownTicks>600</cooldownTicks>
      <maxCharges>3</maxCharges>
      <rechargeTicks>60000</rechargeTicks>
      <warmupSound>DMS_GasVent_Hiss</warmupSound>          <!-- 無新音效前用 Interact_Ignite 或留空 -->
      <warmupEffecter>FFF_Scanner_Alert</warmupEffecter>    <!-- 沿用掃描器紅光 / reuse the scanner's red flash -->
      <releasingEffecter>DMS_GasVent_Releasing</releasingEffecter>  <!-- 見下 -->
    </li>
  </comps>
</ThingDef>
```

`DMS_AlertBuildingBase` 已提供 `CompProperties_Stunnable`（EMP/Stun）與 `CompProperties_CanBeDormant`，`receivesSignals true`，這裡不用重寫。

**`DMS_GasVent_Releasing`（EffecterDef）**：一個持續噴出的煙霧 mote，可從原版 `ToxGasReleasing` / `Smoke` 系列複製：

```xml
<EffecterDef>
  <defName>DMS_GasVent_Releasing</defName>
  <children>
    <li>
      <subEffecterClass>SubEffecter_SprayerContinuous</subEffecterClass>
      <fleckDef>Fleck_ToxGasPuff</fleckDef>   <!-- 動工時確認原版名稱；沒有就用 AirPuff 著色 -->
      <ticksBetweenMotes>8</ticksBetweenMotes>
      <speed>0.4~0.8</speed>
      <angle>0~360</angle>
      <scale>0.8~1.4</scale>
    </li>
  </children>
</EffecterDef>
```

**音效**：`DMS_GasVent_Hiss` 沒有現成資產前，`warmupSound` 先留空，不要引用不存在的 SoundDef（會在載入時報錯）。

### 12.5 配置

| 位置 | 方式 | 數量 |
|---|---|---|
| 設施檔案庫主走廊（§11.4 `DMS_ArchiveCorridor`） | `ModExtension_VaultSecurity` 新增 `gasVentDef` / `gasVentChance`，由 `RoomContents_VaultMainHall` 在每座哨站前後 3~5 格的走廊中線各擲一次 | 每哨站 0~2 |
| 設施檔案庫伺服機房 | `RoomContents_ArchiveServerHall` 在核心互動格兩側各放 1 個（避開互動格本身） | 2 |
| 設施檔案庫冷卻機房 | `LayoutRoomDef` prefab `DMS_Prefab_GasVentPair` | 0~1 組 |
| 地下大廳（`DMS_UndergroundHall`）三張手工版面 | 選配：遊戲內擺放後重新匯出；並加進 `DMS_UndergroundHallStructure.defenderDefs` | 視版面 |
| SAGE 節點站點 | 選配：機櫃陣列所在的室內區 | 2~4 |
| 軍事儲存庫 | **不放**：保持儲存庫原有的節奏 | 0 |

`ModExtension_VaultSecurity` 新欄位：

```csharp
/// <summary>哨站附近的毒氣釋放口；null = 不放。Gas vents near checkpoints; null = none.</summary>
public ThingDef gasVentDef;
/// <summary>每座哨站的每一側放一個的機率。Chance per checkpoint side.</summary>
public float gasVentChance = 0.5f;
```

`RoomContents_VaultMainHall` 在放完哨站後：

```csharp
if (ext.gasVentDef != null)
{
    foreach (IntVec3 side in CheckpointApproachCells(checkpoint, rect))   // 哨站前後、走廊中線上 3~5 格
    {
        if (!Rand.Chance(ext.gasVentChance)) continue;
        if (!side.Standable(map) || side.GetEdifice(map) != null) continue;
        VaultRoomUtility.SpawnSecurity(ext.gasVentDef, side, map, Rot4.North, defenders, ext.initialBatteryPct);
    }
}
```

> 放在哨站「前方」的走廊上：玩家推進到哨站時警報響，毒氣正好噴在接近路線上；放在哨站後方則用來阻止玩家繞過哨站往深處跑。
> 伺服機房的兩個釋放口讓「駭核心」本身變成一個取捨：戴防毒面具的駭客、先斷電、或拿 EMP 手雷處理。
> 口袋地圖的電力：`RoomContents_VaultMainHall` 沿走廊中線鋪隱藏導線、接到壁掛配電盤，釋放口放在中線上就與哨戒砲共用同一條電路。釋放口與導線都不是 edifice，理論上可同格共存；動工時確認，不行就放在導線旁一格並補一段導線。玩家打爛走廊配電盤，整排毒氣口一起失效，這是刻意保留的解法。

### 12.6 文本

**ThingDef**

| | EN | 繁中 |
|---|---|---|
| label | gas vent | 毒氣釋放口 |
| description | A floor grate wired into the facility's alert network. When the alarm sounds, it hisses for a moment and then floods the area with toxic gas. The facility's mechanical garrison doesn't breathe; anyone else had better have a gas mask. It holds enough for a few releases and slowly refills from a tank below. Cut its power or hit it with an EMP to shut it down, even in the middle of a release. | 安裝在地板上、連接著設施警報網路的格柵。警報響起時，它會先發出短暫的嘶嘶聲，接著朝周圍噴出大量毒氣。設施裡的機械守軍不需要呼吸，其他人最好戴著防毒面具。它的存量足夠釋放數次，並會從下方的儲氣槽慢慢補充。切斷電源或以 EMP 癱瘓就能讓它停下，即使正在噴發中也一樣。 |

**Keyed**（`1.6/NewContent/Languages/<lang>/Keyed/DMS_FacilityServer.xml`，與 §7.5 同檔）

```xml
<!-- EN -->
<DMS_GasVent_Warning>{VENT_definite} is hissing: toxic gas incoming!</DMS_GasVent_Warning>
<DMS_GasVent_Charges>Gas charges: {0} / {1}</DMS_GasVent_Charges>
<DMS_GasVent_Arming>Pressurizing</DMS_GasVent_Arming>
<DMS_GasVent_Releasing>Releasing gas</DMS_GasVent_Releasing>

<!-- 繁中 -->
<DMS_GasVent_Warning>{VENT_definite}發出嘶嘶聲：毒氣即將噴出！</DMS_GasVent_Warning>
<DMS_GasVent_Charges>毒氣存量：{0} / {1}</DMS_GasVent_Charges>
<DMS_GasVent_Arming>正在加壓</DMS_GasVent_Arming>
<DMS_GasVent_Releasing>正在釋放毒氣</DMS_GasVent_Releasing>
```

**設施檔案庫描述補句**（§11.9 荒廢站點補句之後再加一句）

> EN: The archive's corridors are fitted with gas vents; bring masks.
>
> 繁中：檔案庫的走廊裝有毒氣釋放口，最好帶上防毒面具。

### 12.7 實作清單

- [ ] G1 `CompAlertEffector_GasVent.cs`
- [ ] G2 `MapComponent_DMSToxGasFallback.cs`（確認 `ToxicUtility.DoPawnToxicDamage` 無 Biotech 閘門）
- [ ] G3 `DMS_Building_GasVent` ThingDef、`DMS_GasVent_Releasing` EffecterDef
- [ ] G4 `ModExtension_VaultSecurity.gasVentDef/gasVentChance`、`RoomContents_VaultMainHall` 放置邏輯；`DMS_ArchiveCorridor` 填入
- [ ] G5 `RoomContents_ArchiveServerHall` 放 2 個釋放口；`DMS_Prefab_GasVentPair`
- [ ] G6 三語 DefInjected 與 Keyed
- [ ] G7 （選配）地下大廳版面、SAGE 節點版面

### 12.8 測試

| # | 情境 | 預期 |
|---|---|---|
| V1 | 放一個釋放口＋一台監視器，殖民者走進監視範圍 | 警報 → 紅光嘶聲 2 秒 → 噴 12 秒毒氣；訊息提示 |
| V2 | 有 Biotech | 原版毒氣效果（ToxGasExposure＋毒素累積）；fallback 不動作 |
| V3 | 無 Biotech | 毒素累積照樣發生；戴防毒面具者不受影響 |
| V4 | 機兵站在毒氣中 | 無傷害 |
| V5 | 預警中切斷電源 | 不噴氣；存量已扣 1 |
| V6 | 噴到一半丟 EMP 手雷 | 立刻停止；EMP 效果結束後不會自動續噴 |
| V7 | 連續觸發 4 次警報 | 前 3 次噴（間隔 ≥ 冷卻），第 4 次存量不足不噴；1 天後回補 1 發 |
| V8 | 室外無屋頂放置 | 毒氣很快消散（預期行為） |
| V9 | 伺服機房開始駭入核心 | 核心兩側釋放口噴氣，駭客若無面具會累積毒素 |
| V10 | 存讀檔於預警中／釋放中 | 狀態延續，effecter 重建 |

---

## 13. 警報建築：設施封鎖中控

> 新建築 `DMS_Building_LockdownController`（設施封鎖中控），2×1，drawSize 3×3。
> 只放在地下口袋地圖（軍事儲存庫、設施檔案庫，地下大廳選配）。警戒值滿 100% 時啟動，倒數 1 分鐘後封死地下出口。
>
> **這套機制取代 FFF 原本的 `AlertEffectWorker_FacilityLockdown`**：邏輯寫進 Fortified Feature Framework，舊版刪除；DMS 只提供建築 Def、貼圖、配置與 DMS 自己的文本。

### 13.0 歸屬與取代範圍

**FFF 舊版的現況**（`_Fortified-Framework/_Sources/Fortified/StandaloneFunctions/AlertSystem/AlertEffects.cs`）：

- `AlertEffectWorker_FacilityLockdown`：警戒值滿 → 倒數 → 把地圖內玩家 pawn 直接移除 → 摧毀口袋地圖。**沒有任何解鎖手段**。
- 由 `WorldComponent_AlertLockdownDriver` 驅動 tick。
- 關閉入口的部分只把 `FFF_FacilityEntrance` 的陣營設為 null（名為「簡易封閉」），實際上沒有擋住進出；而且 FFF 沒有任何 XML 定義 `FFF_FacilityEntrance`。
- `MapComponent_AlertCounter.effectWorkers` 在整個 FFF 與 DMS 都**沒有任何地方加入項目**：這個 worker 目前是無人使用的死碼，取代它不會改變任何既有遊戲行為。

**分工**

| 放在 FFF（`Fortified` 命名空間） | 放在 DMS |
|---|---|
| `FacilityLockdownState`、`MapComponent_FacilityLockdown` | `DMS_Building_LockdownController` ThingDef 與貼圖 |
| `CompFacilityLockdownController`（＋Props） | 電梯 Def 掛上 `Fortified.CompProperties_FacilityLockdownGate` |
| `CompFacilityLockdownGate`（＋Props） | `ModExtension_FacilityLockdown` 與 `DMS_GenStep_Vault` 的放置邏輯 |
| `JobDriver_LockdownOverride`、JobDef `FFF_LockdownOverride`、`FFF_JobDefOf` 欄位 | 建築的 DefInjected 翻譯、設施描述補句 |
| `FacilityLockdownUtility` | |
| （視 §13.4 實測）`MapDeiniter.Deinit` prefix | |
| Keyed `FFF_Lockdown_*`（三語，取代舊的 `FFF_Alert_FacilityLockdown_*`） | |

**要從 FFF 刪除／改動**

1. `AlertEffects.cs`：刪除 `AlertEffectWorker_FacilityLockdown` 整個類別與檔頭對它的說明註解。
2. `WorldComponent_AlertLockdownDriver`：**保留空殼一個版本**，只剩建構子與 `[Obsolete]` 標記，內容全刪。WorldComponent 會寫進存檔，類別直接消失會讓舊存檔讀取時記一條 `Could not find class` 錯誤。下下個版本再刪。
3. `Languages/<lang>/Keyed/AlertSystem.xml`：刪除 `FFF_Alert_FacilityLockdown_Warning/_Countdown/_Executed` 三個鍵，換成 §13.8 的 `FFF_Lockdown_*`。
4. `FacilityEntrance`（`_Sources/Fortified/Thing/FacilityEntrance.cs`）不需要改：它是 `MapPortal` 子類，任何用它的 ThingDef 加上 `CompProperties_FacilityLockdownGate` 就會套用封鎖。
5. FFF 的 `Docs/`（若有警報系統文件）更新：封鎖改為「中控建築驅動」，不再是 `AlertEffectWorker`。

> 封鎖不再做成 `AlertEffectWorker`：舊設計是「警戒滿時從效果池隨機抽一個」，而中控是一座實體建築，地圖上有它才會封鎖、打爛或駭入它會改變結果。建築驅動讓任何 FFF 模組只要在版面裡放一座中控、在出入口加一個 comp 就能用，不需要註冊 worker。

### 13.1 機制總覽

```
            ┌─────────── 駭入中控（任何階段都可，含警報前）──────────┐
            ▼                                                     │
[待命 Idle] ──警戒值 100%──► [倒數 Countdown 3600 tick] ──► [封鎖 Locked] ──駭入中控──► [解除 Disarmed]
   │                              │   EMP：暫停倒數                 │
   │                              │                                │
   └────────── 中控被摧毀（任何階段）──────────────────────────────┴──► [死鎖 Sealed]
                                                                             │
                                          地表殖民者對入口電梯執行「緊急解鎖」──►[解除 Disarmed]
                                                                             │
                             內外都沒有人能解鎖 → 寬限 1 天 → 仍無解 ──► [失蹤 Lost]
```

| 狀態 | 地下出口 | 地表入口 | 說明 |
|---|---|---|---|
| `Idle` | 可用 | 可用 | 中控待命；已可先駭入把它解除 |
| `Countdown` | 可用 | 可用 | 1 分鐘（3600 tick）倒數，每 10 秒一則訊息；中控被 EMP 癱瘓時倒數暫停 |
| `Locked` | **不可用** | **不可用** | 駭入中控即可解除 |
| `Sealed` | **不可用** | **不可用** | 中控已毀：地下無法解鎖，只能由地表殖民者對入口電梯「緊急解鎖」 |
| `Disarmed` | 可用 | 可用 | 永久解除，不會再封鎖 |
| `Lost` | — | 永久封死 | 困在裡面的玩家 pawn 判定失蹤，口袋地圖移除 |

**摧毀即死鎖**：中控是故障保險（fail-safe）設計，被摧毀的瞬間直接進入 `Sealed`，不論當時在哪個狀態、警報有沒有響。這是描述裡明寫的風險：玩家不能用「先打爛它」來繞過機制，正確解法是**駭入**。

**斷電不影響**：中控用設施的放射性電池自持，沒有電力需求；EMP 只能暫停倒數，無法阻止封鎖。

### 13.2 失蹤判定

`Locked` 或 `Sealed` 期間，每 250 tick 檢查一次「有沒有人能解鎖」：

| 解鎖途徑 | 成立條件 |
|---|---|
| 地下：駭入中控 | 中控仍在，且口袋地圖內有玩家 pawn 可以駭入（`HackUtility.IsCapableOfHacking`、未倒地、能走到中控、滿足智識門檻） |
| 地表：緊急解鎖 | 地表地圖存在，且上面有玩家自由殖民者可以駭入入口電梯 |

- 兩者都不成立 → 發信「受困」，開始 **1 天**（60000 tick）寬限倒數。
- 寬限中任一途徑恢復成立（例如援救隊抵達地表、倒地的駭客醒來）→ 取消寬限。
- 寬限歸零 → `Lost`：
  1. 口袋地圖上每個玩家 pawn（含機兵、動物）：`PawnDiedOrDownedThoughtsUtility.TryGiveThoughts(pawn, null, PawnDiedOrDownedThoughtsKind.Lost)` → `DeSpawn` → 陣營設為 null → `Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever)`（保留在世界上，日後可做救援任務）
  2. 發「失蹤」信件，列出名單
  3. `PocketMapUtility.DestroyPocketMap(map)`；入口電梯保持永久封死
- **地表地圖被移除**（玩家讓商隊離開、站點消失）而地下仍處於 `Locked` / `Sealed`：不等寬限，立即走 `Lost` 流程（見 §13.4 的 Harmony 前置攔截）。
- 口袋地圖內沒有玩家 pawn 時不做任何判定：沒人被困，只是門鎖著。

### 13.3 C# 實作

新資料夾 `_Fortified-Framework/_Sources/Fortified/StandaloneFunctions/AlertSystem/Lockdown/`（命名空間 `Fortified`）。以下程式碼全部屬於 FFF。

#### `MapComponent_FacilityLockdown.cs`：狀態的唯一來源

狀態放在**口袋地圖**的 MapComponent，而不是中控身上：中控被摧毀後狀態仍要存在；地表入口與地下出口也都從這裡讀。

```csharp
using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using Verse;
using Verse.AI;

namespace Fortified
{
    public enum FacilityLockdownState { Idle, Countdown, Locked, Sealed, Disarmed, Lost }

    /// <summary>
    /// 地下設施的封鎖狀態。放在口袋地圖上，所以中控被摧毀後仍然存在；地表入口與地下出口都查這裡。
    /// Lockdown state of an underground facility. Lives on the pocket map, so it outlasts the controller;
    /// both the surface entrance and the underground exit read it.
    /// </summary>
    public class MapComponent_FacilityLockdown : MapComponent
    {
        public const int CountdownTicks = 3600;
        private const int CountdownMessageInterval = 600;
        private const int RescueCheckInterval = 250;
        private const int GraceTicks = GenDate.TicksPerDay;

        private FacilityLockdownState state = FacilityLockdownState.Idle;
        private Thing controller;
        private int countdownLeft = -1;
        private int graceLeft = -1;

        public MapComponent_FacilityLockdown(Map map) : base(map) { }

        public FacilityLockdownState State => state;
        public bool BlocksPortals => state == FacilityLockdownState.Locked
                                  || state == FacilityLockdownState.Sealed
                                  || state == FacilityLockdownState.Lost;
        public int CountdownLeft => countdownLeft;
        public int GraceLeft => graceLeft;
        public Thing Controller => controller;

        public static MapComponent_FacilityLockdown For(Map pocketMap) =>
            pocketMap?.GetComponent<MapComponent_FacilityLockdown>();

        // ── 由中控呼叫 / Called by the controller ──

        public void Register(Thing c) => controller = c;

        public void Notify_AlertFull()
        {
            if (state != FacilityLockdownState.Idle) return;
            state = FacilityLockdownState.Countdown;
            countdownLeft = CountdownTicks;
            Find.LetterStack.ReceiveLetter("FFF_Lockdown_StartedLabel".Translate(),
                "FFF_Lockdown_StartedText".Translate(CountdownTicks.ToStringTicksToPeriod()),
                LetterDefOf.ThreatBig, controller);
        }

        public void Notify_ControllerHacked(Pawn hacker)
        {
            if (state == FacilityLockdownState.Lost) return;
            bool wasLocked = BlocksPortals || state == FacilityLockdownState.Countdown;
            state = FacilityLockdownState.Disarmed;
            countdownLeft = graceLeft = -1;
            Messages.Message((wasLocked ? "FFF_Lockdown_Lifted" : "FFF_Lockdown_Disarmed").Translate(hacker.Named("HACKER")),
                controller, MessageTypeDefOf.PositiveEvent);
        }

        public void Notify_ControllerDestroyed()
        {
            controller = null;
            if (state == FacilityLockdownState.Disarmed || state == FacilityLockdownState.Lost) return;
            state = FacilityLockdownState.Sealed;
            countdownLeft = -1;
            CancelPortalJobs();
            Find.LetterStack.ReceiveLetter("FFF_Lockdown_SealedLabel".Translate(),
                "FFF_Lockdown_SealedText".Translate(), LetterDefOf.ThreatBig, SurfacePortal);
        }

        // ── 由地表入口呼叫 / Called by the surface entrance ──

        public void Notify_SurfaceOverride(Pawn pawn)
        {
            if (!BlocksPortals || state == FacilityLockdownState.Lost) return;
            state = FacilityLockdownState.Disarmed;
            graceLeft = -1;
            Messages.Message("FFF_Lockdown_Overridden".Translate(pawn.Named("PAWN")), SurfacePortal, MessageTypeDefOf.PositiveEvent);
        }

        // ── Tick ──

        public override void MapComponentTick()
        {
            switch (state)
            {
                case FacilityLockdownState.Countdown: TickCountdown(); break;
                case FacilityLockdownState.Locked:
                case FacilityLockdownState.Sealed:
                    if (Find.TickManager.TicksGame % RescueCheckInterval == 0) TickRescue(); break;
            }
        }

        private void TickCountdown()
        {
            // 中控被 EMP 癱瘓時倒數暫停。Countdown pauses while the controller is stunned.
            if (controller?.TryGetComp<CompStunnable>()?.StunHandler.Stunned == true) return;

            if (countdownLeft % CountdownMessageInterval == 0 && countdownLeft > 0)
            {
                Messages.Message("FFF_Lockdown_Countdown".Translate(countdownLeft.ToStringTicksToPeriod()),
                    controller, MessageTypeDefOf.ThreatBig, historical: false);
            }
            if (--countdownLeft > 0) return;

            state = FacilityLockdownState.Locked;
            countdownLeft = -1;
            CancelPortalJobs();
            Find.LetterStack.ReceiveLetter("FFF_Lockdown_LockedLabel".Translate(),
                "FFF_Lockdown_LockedText".Translate(), LetterDefOf.ThreatBig, controller);
        }

        private void TickRescue()
        {
            if (!AnyPlayerPawnInside()) { graceLeft = -1; return; }

            if (CanRescueFromInside() || CanRescueFromOutside())
            {
                if (graceLeft >= 0)
                {
                    graceLeft = -1;
                    Messages.Message("FFF_Lockdown_GraceCancelled".Translate(), MessageTypeDefOf.PositiveEvent);
                }
                return;
            }

            if (graceLeft < 0)
            {
                graceLeft = GraceTicks;
                Find.LetterStack.ReceiveLetter("FFF_Lockdown_TrappedLabel".Translate(),
                    "FFF_Lockdown_TrappedText".Translate(GraceTicks.ToStringTicksToPeriod()),
                    LetterDefOf.ThreatBig);
                return;
            }

            graceLeft -= RescueCheckInterval;
            if (graceLeft <= 0) FacilityLockdownUtility.DeclareLost(map, this);
        }

        public void Notify_Lost() => state = FacilityLockdownState.Lost;

        // ── 判定 / Checks ──

        private bool AnyPlayerPawnInside() => map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Any();

        private bool CanRescueFromInside()
        {
            if (controller == null || controller.Destroyed) return false;
            CompHackable hack = controller.TryGetComp<CompHackable>();
            if (hack == null || hack.IsHacked || hack.LockedOut) return false;
            return map.mapPawns.FreeColonistsSpawned.Any(p => hack.CanHackNow(p).Accepted);
        }

        private bool CanRescueFromOutside()
        {
            Thing portal = SurfacePortal;
            if (portal == null || !portal.Spawned) return false;
            return portal.Map.mapPawns.FreeColonistsSpawned.Any(p =>
                !p.Downed && HackUtility.IsCapableOfHacking(p) && p.CanReach(portal, PathEndMode.Touch, Danger.Deadly));
        }

        /// <summary>通往這張口袋地圖的地表入口。The surface portal leading into this pocket map.</summary>
        public MapPortal SurfacePortal => FacilityLockdownUtility.FindSurfacePortal(map);

        /// <summary>封鎖生效時，取消兩端正在前往電梯的進出工作。Cancel enter jobs at both ends once locked.</summary>
        private void CancelPortalJobs()
        {
            FacilityLockdownUtility.CancelEnterJobs(map);
            MapPortal surface = SurfacePortal;
            if (surface?.Map != null) FacilityLockdownUtility.CancelEnterJobs(surface.Map, surface);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref state, "dms_lockdownState", FacilityLockdownState.Idle);
            Scribe_References.Look(ref controller, "dms_lockdownController");
            Scribe_Values.Look(ref countdownLeft, "dms_lockdownCountdown", -1);
            Scribe_Values.Look(ref graceLeft, "dms_lockdownGrace", -1);
        }
    }
}
```

> `MapComponent` 會被**所有**地圖實例化（含一般殖民地），但 `state` 預設 `Idle` 且沒有中控註冊時，`MapComponentTick` 只走一次 switch，成本可忽略。
> `CompHackable.CanHackNow(Pawn)` 是原版的 public 方法（§4.4 引用過的原始碼可見），已包含可達性、技能門檻、鎖定判斷。

#### `CompFacilityLockdownController.cs`：中控本體

```csharp
namespace Fortified
{
    public class CompProperties_FacilityLockdownController : CompProperties
    {
        /// <summary>多久檢查一次警戒值。How often to poll the alert level.</summary>
        public int checkInterval = 60;
        public EffecterDef countdownEffecter;   // 倒數中的紅燈 / red strobe while counting down
        public EffecterDef lockedEffecter;      // 封鎖中 / while locked

        public CompProperties_FacilityLockdownController() { compClass = typeof(CompFacilityLockdownController); }
    }

    /// <summary>
    /// 設施封鎖中控：盯著地圖警戒值，滿 100% 就通知 MapComponent 開始倒數；被駭入就解除，被摧毀就死鎖。
    /// Lockdown controller: watches the map's alert level and starts the countdown at 100%; hacking it
    /// disarms the lockdown, destroying it seals the facility for good.
    /// </summary>
    public class CompFacilityLockdownController : ThingComp
    {
        [Unsaved] private Effecter effecter;
        public CompProperties_FacilityLockdownController Props => (CompProperties_FacilityLockdownController)props;
        private MapComponent_FacilityLockdown Lockdown => MapComponent_FacilityLockdown.For(parent.MapHeld);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Lockdown?.Register(parent);
        }

        public override void CompTick()
        {
            base.CompTick();
            MapComponent_FacilityLockdown ld = Lockdown;
            if (ld == null) return;

            if (ld.State == FacilityLockdownState.Idle && parent.IsHashIntervalTick(Props.checkInterval))
            {
                MapComponent_AlertCounter counter = parent.Map.GetComponent<MapComponent_AlertCounter>();
                if (counter != null && (counter.IsTriggered || counter.AlertLevelPct >= 1f))
                    ld.Notify_AlertFull();
            }

            UpdateEffecter(ld.State);
        }

        public override void Notify_Hacked(Pawn hacker)
        {
            base.Notify_Hacked(hacker);
            Lockdown?.Notify_ControllerHacked(hacker);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            effecter?.Cleanup();
            // 口袋地圖整張被移除時不算「摧毀」。A whole pocket map being torn down doesn't count.
            if (mode == DestroyMode.Vanish || mode == DestroyMode.WillReplace) return;
            MapComponent_FacilityLockdown.For(previousMap)?.Notify_ControllerDestroyed();
        }

        private void UpdateEffecter(FacilityLockdownState s)
        {
            EffecterDef want = s == FacilityLockdownState.Countdown ? Props.countdownEffecter
                             : s == FacilityLockdownState.Locked ? Props.lockedEffecter : null;
            if (want == null) { effecter?.Cleanup(); effecter = null; return; }
            if (effecter == null || effecter.def != want) { effecter?.Cleanup(); effecter = want.Spawn(parent, parent.Map); }
            effecter.EffectTick(parent, TargetInfo.Invalid);
        }

        public override string CompInspectStringExtra()
        {
            MapComponent_FacilityLockdown ld = Lockdown;
            if (ld == null) return null;
            switch (ld.State)
            {
                case FacilityLockdownState.Countdown:
                    return "FFF_Lockdown_InspectCountdown".Translate(ld.CountdownLeft.ToStringTicksToPeriod()).Colorize(ColorLibrary.RedReadable);
                case FacilityLockdownState.Locked:
                    return "FFF_Lockdown_InspectLocked".Translate().Colorize(ColorLibrary.RedReadable);
                case FacilityLockdownState.Disarmed:
                    return "FFF_Lockdown_InspectDisarmed".Translate();
                default:
                    return "FFF_Lockdown_InspectIdle".Translate();
            }
        }
    }
}
```

> 中控的 `CompHackable` 用原版即可（不需要 §4 的多階段版本），駭入完成時原版會呼叫所有 comp 的 `Notify_Hacked`。
> 已經 `Disarmed` 後再摧毀中控：`Notify_ControllerDestroyed` 直接 return，不會死鎖。

#### `CompFacilityLockdownGate.cs`：掛在兩端電梯上

原版 `MapPortal.IsEnterable` 會逐一詢問每個 comp 的 `CanEnterPortal()`，所以**不需要 Harmony**，一個 comp 同時管地表入口與地下出口：

```csharp
namespace Fortified
{
    public class CompProperties_FacilityLockdownGate : CompProperties
    {
        /// <summary>地表緊急解鎖所需工作量（以 HackingSpeed 推進）。Work for the surface override, driven by HackingSpeed.</summary>
        public float overrideWork = 6000f;
        public int overrideSkillPrerequisite = 6;

        public CompProperties_FacilityLockdownGate() { compClass = typeof(CompFacilityLockdownGate); }
    }

    /// <summary>
    /// 設施封鎖的出入口閘門：封鎖時拒絕進出；在地表那一端、且中控已毀（Sealed）時，提供「緊急解鎖」。
    /// The lockdown gate on both lifts: refuses entry while locked; on the surface end, once the controller
    /// is gone (Sealed), offers an emergency override.
    /// </summary>
    public class CompFacilityLockdownGate : ThingComp
    {
        private float overrideProgress;

        /// <summary>
        /// 失蹤判定後口袋地圖已被移除，狀態查不到了，改由地表端自己記住「永久封死」，否則原版會重新產生一張新地圖。
        /// After a Lost verdict the pocket map is gone and its state with it; the surface end remembers the seal,
        /// or vanilla would happily generate a fresh map.
        /// </summary>
        private bool permanentlySealed;

        public void Notify_PermanentlySealed() => permanentlySealed = true;

        public CompProperties_FacilityLockdownGate Props => (CompProperties_FacilityLockdownGate)props;
        private MapPortal Portal => parent as MapPortal;
        private bool IsSurfaceEnd => !(parent is PocketMapExit);

        /// <summary>地表端看 PocketMap；地下端看自己所在的地圖。Surface end reads its pocket map; the exit reads its own map.</summary>
        public MapComponent_FacilityLockdown Lockdown =>
            MapComponent_FacilityLockdown.For(IsSurfaceEnd ? Portal?.PocketMap : parent.MapHeld);

        public override AcceptanceReport CanEnterPortal()
        {
            if (permanentlySealed) return "FFF_Lockdown_GateLost".Translate();
            MapComponent_FacilityLockdown ld = Lockdown;
            if (ld == null || !ld.BlocksPortals) return true;
            return ld.State == FacilityLockdownState.Lost
                ? "FFF_Lockdown_GateLost".Translate()
                : "FFF_Lockdown_GateLocked".Translate();
        }

        public bool CanOverrideNow => IsSurfaceEnd && Lockdown?.State == FacilityLockdownState.Sealed;
        public float OverrideProgressPct => overrideProgress / Props.overrideWork;

        public void DoOverrideWork(Pawn pawn, float amount)
        {
            overrideProgress += amount;
            if (overrideProgress >= Props.overrideWork)
            {
                overrideProgress = 0f;
                Lockdown?.Notify_SurfaceOverride(pawn);
            }
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (!CanOverrideNow) yield break;
            if (!HackUtility.IsCapableOfHacking(selPawn))
            {
                yield return new FloatMenuOption("FFF_Lockdown_CannotOverride".Translate() + ": " + "IncapableOfHacking".Translate(), null);
                yield break;
            }
            if (selPawn.skills.GetSkill(SkillDefOf.Intellectual).Level < Props.overrideSkillPrerequisite)
            {
                yield return new FloatMenuOption("FFF_Lockdown_CannotOverride".Translate() + ": " +
                    "SkillTooLow".Translate(SkillDefOf.Intellectual.label, selPawn.skills.GetSkill(SkillDefOf.Intellectual).Level, Props.overrideSkillPrerequisite), null);
                yield break;
            }
            yield return new FloatMenuOption("FFF_Lockdown_Override".Translate(parent.Label), () =>
                selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(FFF_JobDefOf.FFF_LockdownOverride, parent), JobTag.Misc));
        }

        public override string CompInspectStringExtra()
        {
            MapComponent_FacilityLockdown ld = Lockdown;
            if (ld == null) return null;
            switch (ld.State)
            {
                case FacilityLockdownState.Countdown:
                    return "FFF_Lockdown_InspectCountdown".Translate(ld.CountdownLeft.ToStringTicksToPeriod()).Colorize(ColorLibrary.RedReadable);
                case FacilityLockdownState.Locked:
                    return "FFF_Lockdown_GateLocked".Translate().Colorize(ColorLibrary.RedReadable);
                case FacilityLockdownState.Sealed:
                    string s = "FFF_Lockdown_GateSealed".Translate().Colorize(ColorLibrary.RedReadable);
                    if (IsSurfaceEnd && overrideProgress > 0f) s += "\n" + "FFF_Lockdown_OverrideProgress".Translate(OverrideProgressPct.ToStringPercent());
                    if (ld.GraceLeft > 0) s += "\n" + "FFF_Lockdown_GraceLeft".Translate(ld.GraceLeft.ToStringTicksToPeriod());
                    return s;
                case FacilityLockdownState.Lost:
                    return "FFF_Lockdown_GateLost".Translate();
                default:
                    return null;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref overrideProgress, "dms_overrideProgress", 0f);
            Scribe_Values.Look(ref permanentlySealed, "dms_permanentlySealed", false);
        }
    }
}
```

#### `JobDriver_LockdownOverride.cs`

照 `JobDriver_Hack` 的結構：走到電梯 → 每 tick `DoOverrideWork(pawn.GetStatValue(StatDefOf.HackingSpeed))`、學智識、進度條、`EffecterDefOf.Hacking`；`FailOn(() => !gate.CanOverrideNow)`。JobDef `FFF_LockdownOverride`（`reportString` 見 §13.6），加進 `DMS_DefOf`。

> 地表緊急解鎖不走 `CompHackable`：地表電梯早就被駭入過（為了進去），同一個 Thing 掛兩個 `CompHackable` 會讓原版 `JobDriver_Hack` 的 `TryGetComp` 取錯。

#### `FacilityLockdownUtility.cs`

```csharp
public static class FacilityLockdownUtility
{
    /// <summary>找通往這張口袋地圖的地表 MapPortal。Find the surface MapPortal whose pocket map this is.</summary>
    public static MapPortal FindSurfacePortal(Map pocketMap)
    {
        Map surface = pocketMap?.Parent is PocketMapParent pmp ? pmp.sourceMap : null;
        if (surface == null) return null;
        return surface.listerThings.AllThings.OfType<MapPortal>().FirstOrDefault(p => p.PocketMap == pocketMap);
    }

    public static void CancelEnterJobs(Map map, MapPortal onlyThis = null)
    {
        foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
        {
            if (p.CurJobDef != JobDefOf.EnterPortal) continue;
            if (onlyThis != null && ((JobDriver_EnterPortal)p.jobs.curDriver).MapPortal != onlyThis) continue;
            p.jobs.EndCurrentJob(JobCondition.Incompletable);
        }
    }

    /// <summary>困在裡面的玩家 pawn 判定失蹤，並移除口袋地圖。Trapped player pawns go missing; the pocket map is removed.</summary>
    public static void DeclareLost(Map pocketMap, MapComponent_FacilityLockdown ld)
    {
        List<Pawn> trapped = pocketMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).ToList();
        ld.Notify_Lost();

        foreach (Pawn pawn in trapped)
        {
            PawnDiedOrDownedThoughtsUtility.TryGiveThoughts(pawn, null, PawnDiedOrDownedThoughtsKind.Lost);
            pawn.DeSpawn(DestroyMode.Vanish);
            pawn.SetFaction(null);
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
        }

        if (trapped.Count > 0)
        {
            Find.LetterStack.ReceiveLetter("FFF_Lockdown_LostLabel".Translate(),
                "FFF_Lockdown_LostText".Translate(trapped.Select(p => p.LabelShort).ToLineList("  - ")),
                LetterDefOf.Death);
        }
        // 先讓地表端記住封死，再移除口袋地圖。Mark the surface end sealed before the pocket map goes.
        FindSurfacePortal(pocketMap)?.TryGetComp<CompFacilityLockdownGate>()?.Notify_PermanentlySealed();
        PocketMapUtility.DestroyPocketMap(pocketMap);
    }
}
```

> `PocketMapParent.sourceMap` 與 `PocketMapUtility.DestroyPocketMap` 是 1.5 起的原版 API，動工時用 decompiler 確認 1.6 的名稱。
> `DestroyPocketMap` 之後地表電梯的 `PocketMap` 會變成 null，`Lockdown` 也就查不到狀態、`CanEnterPortal` 會放行並嘗試**重新產生一張新地圖**。因此 `Lost` 另外記在地表端的 `CompFacilityLockdownGate.permanentlySealed`（見上方程式），`DeclareLost` 在移除地圖前先設好，`CanEnterPortal` 優先檢查它。地表電梯的檢視字串在這種狀態下也要顯示「已永久封死」：`CompInspectStringExtra` 開頭補一行 `if (permanentlySealed) return "FFF_Lockdown_GateLost".Translate();`。

### 13.4 地表地圖被移除時

玩家讓地表的人全部離開時，站點地圖可能連同口袋地圖一起被移除，困在地下的 pawn 會被原版直接處理掉，不經過失蹤流程。

- **先實測**原版在這種情況下的行為：`MapParent.ShouldRemoveMapNow` 是否會因為口袋地圖裡還有玩家 pawn 而保留地圖（1.5 的地下洞穴有類似保護）。
- 若會保留：`TickRescue` 的「地表無人可救」判定會自然走完 1 天寬限 → `Lost`，不需額外處理。
- 若不會：在 FFF 加一個 Harmony prefix 於 `MapDeiniter.Deinit(Map map, ...)`（FFF 的 Patch 資料夾）：當 `map` 是口袋地圖、`MapComponent_FacilityLockdown.BlocksPortals` 為 true、且裡面有玩家 pawn 時，先呼叫 `DeclareLost`（略過 `DestroyPocketMap`，因為正在被移除）。

### 13.5 XML

#### 中控（DMS：`DMS_ExplorationBuildings_Alert.xml`）

```xml
<!-- 設施封鎖中控：警戒值滿 100% 時倒數 1 分鐘，封死地下出入口。駭入即解除（警報前也可以先駭）。
     被摧毀時直接死鎖設施，只能由地表的人對入口電梯做緊急解鎖。自帶電池，斷電無效；EMP 只能暫停倒數。
     Lockdown controller: at 100% alert it counts down one minute, then seals the lifts. Hacking it
     lifts the lockdown (and can be done before any alarm). Destroying it seals the facility for good,
     leaving only an emergency override from the surface. Self-powered; EMP only pauses the countdown. -->
<ThingDef ParentName="DMS_BuildingBase">
  <defName>DMS_Building_LockdownController</defName>
  <label>lockdown controller</label>
  <description>(見 §13.6)</description>
  <category>Building</category>
  <selectable>true</selectable>
  <useHitPoints>true</useHitPoints>
  <tickerType>Normal</tickerType>
  <graphicData>
    <texPath>Things/Building/AlertCaller/LockdownController</texPath>   <!-- 貼圖待補 / art pending -->
    <graphicClass>Graphic_Multi</graphicClass>
    <drawSize>(3,3)</drawSize>
  </graphicData>
  <size>(1,2)</size>
  <rotatable>true</rotatable>
  <hasInteractionCell>true</hasInteractionCell>
  <interactionCellOffset>(0,0,-1)</interactionCellOffset>
  <fillPercent>0.7</fillPercent>
  <statBases>
    <MaxHitPoints>500</MaxHitPoints>
    <Flammability>0</Flammability>
  </statBases>
  <building>
    <claimable>false</claimable>
    <deconstructible>false</deconstructible>
  </building>
  <comps>
    <li Class="CompProperties_Stunnable">
      <affectedDamageDefs>
        <li>EMP</li>
      </affectedDamageDefs>
    </li>
    <li Class="CompProperties_Hackable">
      <defence>4000</defence>
      <effectHacking>HackingTerminal</effectHacking>
      <intellectualSkillPrerequisite>6</intellectualSkillPrerequisite>
      <lockoutDurationHoursRange>2~4</lockoutDurationHoursRange>
      <notHackedInspectString>FFF_Lockdown_HackPrompt</notHackedInspectString>
      <hackedInspectString>FFF_Lockdown_HackedLabel</hackedInspectString>
    </li>
    <li Class="Fortified.CompProperties_FacilityLockdownController">
      <checkInterval>60</checkInterval>
      <countdownEffecter>FFF_Scanner_Alert</countdownEffecter>
      <lockedEffecter>FFF_Scanner_Trigger</lockedEffecter>
    </li>
    <li Class="CompProperties_Glower">
      <glowRadius>3</glowRadius>
      <glowColor>(255,60,40,0)</glowColor>
    </li>
  </comps>
</ThingDef>
```

> 不繼承 `DMS_AlertBuildingBase`：那個父類帶 `CompCanBeDormant`，休眠中的中控會讓封鎖行為難以預期；中控永遠清醒。
> 鎖定（lockout）只有 2~4 小時：玩家駭入失手被鎖時仍有機會在 1 天寬限內解開，不會因為運氣直接判定失蹤。
> 貼圖：需要 `LockdownController_north/_south/_east`（缺 west 則以 east 翻轉），放在 `1.6/NewContent/Textures/Things/Building/AlertCaller/`。

#### 電梯加上閘門 comp

`DMS_ElevatorBase`（§11.4 抽出的抽象父）與 `DMS_VaultElevatorExit` 各加一行：

```xml
<li Class="Fortified.CompProperties_FacilityLockdownGate">
  <overrideWork>6000</overrideWork>
  <overrideSkillPrerequisite>6</overrideSkillPrerequisite>
</li>
```

`DMS_VaultElevatorExit` 的父類是原版 `PocketMapExit`，它的 `<comps>` 會與父類合併，不會覆蓋。

#### JobDef（FFF：`_Fortified-Framework/1.6/Defs/JobDef.xml`）

```xml
<JobDef>
  <defName>FFF_LockdownOverride</defName>
  <driverClass>Fortified.JobDriver_LockdownOverride</driverClass>
  <reportString>overriding lockdown on TargetA.</reportString>
  <casualInterruptible>false</casualInterruptible>
</JobDef>
```

### 13.6 配置

每張地下地圖**恰好一座**中控：

| 地下設施 | 放在哪 | 方式 |
|---|---|---|
| 設施檔案庫 | 運維室（§11.4 `DMS_ArchiveOperations`） | `LayoutRoomDef` 掛 `ModExtension_FacilityLockdown`，由 `DMS_GenStep_Vault` 在結構生成後放置 |
| 軍事儲存庫 | 指揮室；沒有指揮室則動力室，再沒有就任一非電梯廳房間 | 同上 |
| 地下大廳（手工版面） | 選配：遊戲內擺放後匯出 | 版面元素 |

```csharp
/// <summary>掛在 StructureLayoutDef 上：要放哪種中控、依序偏好哪些房間。On the layout: which controller, and room preference order.</summary>
public class ModExtension_FacilityLockdown : DefModExtension
{
    public ThingDef controllerDef;
    public List<LayoutRoomDef> preferredRooms = new List<LayoutRoomDef>();
}
```

`DMS_GenStep_Vault.Generate` 在 `Spawn` 之後：依 `preferredRooms` 順序找第一間存在的房間，用 `RoomGenUtility.FillWithPadding(controllerDef, 1, room, map, …, contractedBy: 1)` 放置；全部找不到就挑一間非 `DMS_VaultEntranceHall` 的房間。放置後 `SetDefenderFaction`（讓它顯示為敵方設施、玩家無法直接操作）。

> **不要**放在電梯廳：電梯廳是出生點，玩家一進來就能把它駭掉，機制等於不存在。
> 伺服機房（密封）裡也不要放：中控被關在密封門後，玩家在封鎖時可能根本走不到。

### 13.7 取代 FFF 舊版封鎖

見 §13.0。重點回顧：

- 舊的 `AlertEffectWorker_FacilityLockdown` 刪除；`WorldComponent_AlertLockdownDriver` 留空殼一個版本。
- 舊版目前沒有任何地方註冊，所以刪除不影響任何現存地圖；不需要存檔遷移。
- 新版不經過 `MapComponent_AlertCounter.effectWorkers`：中控自己盯 `AlertLevelPct` / `IsTriggered`。因此即使日後有人往 `effectWorkers` 加了別的效果，也與封鎖互不干擾。
- 其他模組（DMS 子模組、Fortification 系列）若要使用封鎖：版面放一座掛 `Fortified.CompProperties_FacilityLockdownController` 的建築，出入口的 `MapPortal` / `PocketMapExit` 掛 `Fortified.CompProperties_FacilityLockdownGate`。

### 13.8 文本

**ThingDef**

| | EN | 繁中 |
|---|---|---|
| label | lockdown controller | 設施封鎖中控 |
| description | An emergency control unit that coordinates the blast gates at a buried facility's entrances. Once it completes a lockdown, the lifts in and out stop working. The unit is built to fail safe: if it is destroyed, the facility seals itself completely, and the doors can then only be opened with help from outside.\n\nIt engages when the facility's alert level reaches its maximum and seals the exits one minute later. Hacking it lifts the lockdown, and it can be hacked ahead of time. It runs on the facility's radioisotope cells, so cutting power does nothing; an EMP will only pause the countdown. | 一個用於協調地下設施封鎖出入口閘門的應急控制裝置，一旦完成封鎖就會使出入口無法使用。這類安全裝置在設計上會在自身被摧毀後完全封鎖設施，此時就必需要有人從外面幫助才能開門。\n\n設施的警戒值達到上限時它就會啟動，並在一分鐘後封死出入口。駭入它就能解除封鎖，也可以事先駭入讓它失效。它使用設施的放射性電池運作，切斷電源沒有作用；EMP 只能暫停倒數。 |

**JobDef reportString**（FFF 的 DefInjected `JobDef/JobDef.xml`）：EN `overriding lockdown on TargetA.`／繁中 `正在對TargetA執行緊急解鎖。`

**Keyed**（FFF：`_Fortified-Framework/Languages/<lang>/Keyed/AlertSystem.xml`，取代舊的三個 `FFF_Alert_FacilityLockdown_*`）

```xml
<!-- EN -->
<FFF_Lockdown_HackPrompt>Hack to disarm the lockdown.</FFF_Lockdown_HackPrompt>
<FFF_Lockdown_HackedLabel>Disarmed</FFF_Lockdown_HackedLabel>
<FFF_Lockdown_InspectIdle>Standing by</FFF_Lockdown_InspectIdle>
<FFF_Lockdown_InspectCountdown>Lockdown in {0}</FFF_Lockdown_InspectCountdown>
<FFF_Lockdown_InspectLocked>Facility locked down</FFF_Lockdown_InspectLocked>
<FFF_Lockdown_InspectDisarmed>Disarmed</FFF_Lockdown_InspectDisarmed>
<FFF_Lockdown_StartedLabel>Facility lockdown</FFF_Lockdown_StartedLabel>
<FFF_Lockdown_StartedText>The facility's alert level has maxed out and the lockdown controller has engaged. In {0}, the blast gates will close and the lift out will stop working.\n\nGet out now, or hack the lockdown controller to stop it. Do not destroy it: it is built to seal the facility completely if it goes down.</FFF_Lockdown_StartedText>
<FFF_Lockdown_Countdown>Facility lockdown in {0}.</FFF_Lockdown_Countdown>
<FFF_Lockdown_LockedLabel>Facility locked down</FFF_Lockdown_LockedLabel>
<FFF_Lockdown_LockedText>The blast gates have closed. Nobody is getting in or out until someone hacks the lockdown controller.</FFF_Lockdown_LockedText>
<FFF_Lockdown_SealedLabel>Facility sealed</FFF_Lockdown_SealedLabel>
<FFF_Lockdown_SealedText>The lockdown controller has been destroyed, and the facility has sealed itself completely. It cannot be opened from inside anymore.\n\nSomeone on the surface has to work the entrance lift's emergency override to get the doors open.</FFF_Lockdown_SealedText>
<FFF_Lockdown_Disarmed>{HACKER_nameDef} has disarmed the lockdown controller. It will no longer seal the facility.</FFF_Lockdown_Disarmed>
<FFF_Lockdown_Lifted>{HACKER_nameDef} has hacked the lockdown controller. The blast gates are opening.</FFF_Lockdown_Lifted>
<FFF_Lockdown_Overridden>{PAWN_nameDef} has forced the entrance lift open from the surface. The facility is accessible again.</FFF_Lockdown_Overridden>
<FFF_Lockdown_GateLocked>Locked down</FFF_Lockdown_GateLocked>
<FFF_Lockdown_GateSealed>Sealed: needs an emergency override from the surface</FFF_Lockdown_GateSealed>
<FFF_Lockdown_GateLost>Permanently sealed</FFF_Lockdown_GateLost>
<FFF_Lockdown_Override>Emergency override on {0}</FFF_Lockdown_Override>
<FFF_Lockdown_CannotOverride>Cannot override</FFF_Lockdown_CannotOverride>
<FFF_Lockdown_OverrideProgress>Override progress: {0}</FFF_Lockdown_OverrideProgress>
<FFF_Lockdown_GraceLeft>Survivors below will be lost in {0}</FFF_Lockdown_GraceLeft>
<FFF_Lockdown_TrappedLabel>Trapped below</FFF_Lockdown_TrappedLabel>
<FFF_Lockdown_TrappedText>Our people are sealed inside the facility, and nobody can get them out: no one below can hack the lockdown controller, and no one on the surface can override the lift.\n\nIf nothing changes within {0}, they will be given up as lost.</FFF_Lockdown_TrappedText>
<FFF_Lockdown_GraceCancelled>Someone can reach the lockdown again. The people trapped below still have a chance.</FFF_Lockdown_GraceCancelled>
<FFF_Lockdown_LostLabel>Lost in the facility</FFF_Lockdown_LostLabel>
<FFF_Lockdown_LostText>Nobody came. The facility stays sealed, and everyone trapped inside is now counted as missing:\n\n{0}</FFF_Lockdown_LostText>

<!-- 繁中 -->
<FFF_Lockdown_HackPrompt>駭入以解除封鎖。</FFF_Lockdown_HackPrompt>
<FFF_Lockdown_HackedLabel>已解除</FFF_Lockdown_HackedLabel>
<FFF_Lockdown_InspectIdle>待命中</FFF_Lockdown_InspectIdle>
<FFF_Lockdown_InspectCountdown>{0}後封鎖</FFF_Lockdown_InspectCountdown>
<FFF_Lockdown_InspectLocked>設施已封鎖</FFF_Lockdown_InspectLocked>
<FFF_Lockdown_InspectDisarmed>已解除</FFF_Lockdown_InspectDisarmed>
<FFF_Lockdown_StartedLabel>設施封鎖</FFF_Lockdown_StartedLabel>
<FFF_Lockdown_StartedText>設施的警戒值已達上限，封鎖中控已經啟動。{0}後，出入口的閘門將會關閉，電梯也將無法使用。\n\n立刻撤離，或是駭入封鎖中控阻止它。不要摧毀它：這類裝置在被摧毀時會將整座設施完全封死。</FFF_Lockdown_StartedText>
<FFF_Lockdown_Countdown>設施將在{0}後封鎖。</FFF_Lockdown_Countdown>
<FFF_Lockdown_LockedLabel>設施已封鎖</FFF_Lockdown_LockedLabel>
<FFF_Lockdown_LockedText>閘門已經關閉。在有人駭入封鎖中控之前，任何人都無法進出。</FFF_Lockdown_LockedText>
<FFF_Lockdown_SealedLabel>設施完全封死</FFF_Lockdown_SealedLabel>
<FFF_Lockdown_SealedText>封鎖中控已被摧毀，設施自行進入了完全封鎖，從內部再也無法開啟。\n\n必須有人在地表對入口電梯執行緊急解鎖，才能打開閘門。</FFF_Lockdown_SealedText>
<FFF_Lockdown_Disarmed>{HACKER_nameDef}解除了封鎖中控，它不會再封鎖設施了。</FFF_Lockdown_Disarmed>
<FFF_Lockdown_Lifted>{HACKER_nameDef}駭入了封鎖中控，閘門正在開啟。</FFF_Lockdown_Lifted>
<FFF_Lockdown_Overridden>{PAWN_nameDef}從地表強制開啟了入口電梯，設施可以再次進出了。</FFF_Lockdown_Overridden>
<FFF_Lockdown_GateLocked>封鎖中</FFF_Lockdown_GateLocked>
<FFF_Lockdown_GateSealed>完全封死：需要從地表執行緊急解鎖</FFF_Lockdown_GateSealed>
<FFF_Lockdown_GateLost>已永久封死</FFF_Lockdown_GateLost>
<FFF_Lockdown_Override>對{0}執行緊急解鎖</FFF_Lockdown_Override>
<FFF_Lockdown_CannotOverride>無法執行緊急解鎖</FFF_Lockdown_CannotOverride>
<FFF_Lockdown_OverrideProgress>解鎖進度：{0}</FFF_Lockdown_OverrideProgress>
<FFF_Lockdown_GraceLeft>受困者將在{0}後判定失蹤</FFF_Lockdown_GraceLeft>
<FFF_Lockdown_TrappedLabel>受困地下</FFF_Lockdown_TrappedLabel>
<FFF_Lockdown_TrappedText>我們的人被封在設施裡，而且沒有任何人能救他們出來：地下沒有人能駭入封鎖中控，地表也沒有人能對電梯執行緊急解鎖。\n\n如果{0}內情況沒有改變，他們將被判定為失蹤。</FFF_Lockdown_TrappedText>
<FFF_Lockdown_GraceCancelled>已經有人能處理封鎖了，受困的人還有機會。</FFF_Lockdown_GraceCancelled>
<FFF_Lockdown_LostLabel>失蹤於設施中</FFF_Lockdown_LostLabel>
<FFF_Lockdown_LostText>沒有人來。設施維持封死，所有受困其中的人都被判定為失蹤：\n\n{0}</FFF_Lockdown_LostText>
```

**設施描述補句**（設施檔案庫 §11.9、軍事儲存庫所在站點的任務描述）

> EN: A lockdown controller watches the facility's alert level; if it maxes out, the lifts will be sealed a minute later.
>
> 繁中：設施內有一座封鎖中控在監視警戒值；一旦警戒值達到上限，電梯將在一分鐘後被封死。

### 13.9 實作清單

**FFF**

- [ ] F1 `Lockdown/`：`FacilityLockdownState`、`MapComponent_FacilityLockdown`、`CompFacilityLockdownController`、`CompFacilityLockdownGate`（含 `permanentlySealed`）、`JobDriver_LockdownOverride`、`FacilityLockdownUtility`
- [ ] F2 `FFF_JobDefOf.FFF_LockdownOverride`；`1.6/Defs/JobDef.xml` 加 JobDef
- [ ] F3 刪除 `AlertEffectWorker_FacilityLockdown`；`WorldComponent_AlertLockdownDriver` 改為空殼並標 `[Obsolete]`
- [ ] F4 Keyed：刪 `FFF_Alert_FacilityLockdown_*`，加 `FFF_Lockdown_*`（三語）；JobDef reportString 翻譯
- [ ] F5 確認 `PocketMapParent.sourceMap`、`PocketMapUtility.DestroyPocketMap` 的 1.6 名稱
- [ ] F6 §13.4：實測地表地圖移除行為，必要時加 `MapDeiniter.Deinit` prefix
- [ ] F7 build FFF，更新 `1.6/Assemblies/Fortified.dll`；更新 FFF 文件與 changelog

**DMS**（依賴 F1~F7 完成後的 FFF）

- [ ] L1 `DMS_Building_LockdownController` ThingDef；貼圖 `LockdownController_*`
- [ ] L2 `DMS_ElevatorBase`、`DMS_VaultElevatorExit` 加 `Fortified.CompProperties_FacilityLockdownGate`
- [ ] L3 `ModExtension_FacilityLockdown` + `DMS_GenStep_Vault` 放置；`DMS_MilitaryVault`、`DMS_DataArchive` 兩張 LayoutDef 填入
- [ ] L4 DefInjected（中控 ThingDef）三語；設施描述補句

### 13.10 測試

| # | 情境 | 預期 |
|---|---|---|
| K1 | 進入檔案庫，除錯把警戒值拉到 100 | 信件＋每 10 秒倒數訊息；中控紅燈；1 分鐘後兩端電梯「封鎖中」、進出選項灰掉 |
| K2 | 倒數中有殖民者正在走向出口 | 封鎖瞬間工作被取消 |
| K3 | 倒數中丟 EMP | 倒數暫停，EMP 結束後繼續 |
| K4 | 倒數中駭入中控 | `Disarmed`，不封鎖 |
| K5 | 警報前先駭入中控 | `Disarmed`；之後警戒值滿也不封鎖 |
| K6 | `Locked` 時駭入中控 | 兩端恢復可用 |
| K7 | `Idle` 時打爛中控 | 立刻 `Sealed`，兩端封死；地表電梯出現「緊急解鎖」 |
| K8 | `Sealed` 時地表殖民者執行緊急解鎖 | 進度條完成後兩端恢復 |
| K9 | `Sealed` 且地表無人、地下有人 | 「受困」信件，1 天寬限；寬限中有殖民者抵達地表 → 取消寬限 |
| K10 | 寬限歸零 | 地下玩家 pawn 失蹤（心情「殖民者失蹤」）、信件列名單；口袋地圖移除；地表電梯永久封死，不會生成新地圖 |
| K11 | `Locked` 且地下唯一駭客倒地、地表無人 | 進入寬限；駭客醒來 → 取消寬限 |
| K12 | 地下駭中控被鎖定（lockout） | 鎖定 2~4 小時內視為「無法從內部解鎖」→ 可能進寬限，鎖定結束後取消 |
| K13 | `Disarmed` 後打爛中控 | 不封鎖 |
| K14 | 存讀檔於倒數／封鎖／寬限中 | 狀態、剩餘時間、解鎖進度延續 |
| K15 | 地表所有人組商隊離開，地下仍封鎖 | 依 §13.4 的實測結果：保留地圖並走寬限，或立即失蹤；**不得**無聲消失 |
| K16 | 一般殖民地地圖 | `MapComponent_FacilityLockdown` 永遠 `Idle`，無任何效果 |
| K17 | 讀取更新前的存檔 | 無 `Could not find class` 錯誤（空殼 WorldComponent 仍在）；地下地圖照常 |
| K18 | 只裝 FFF、不裝 DMS | 載入無錯誤；沒有任何建築掛中控，機制不會出現 |
