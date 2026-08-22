# DMS 可增加 HistoryEvent / Tale 的位置盤點

盤點範圍：`Dead-Man-Switch`（1.6 全部 Defs + `_Source/DMS`），並順帶檢查 `_Fortified-Framework` 中 DMS 會用到的掛點。

---

## 一、現況

**XML：完全沒有。** `1.6/**` 與 `Defs/**` 中沒有任何 `HistoryEventDef`、`TaleDef` 定義，也沒有任何欄位引用它們。

**C#：只有 3 處，全是引用原版 def。**

| 檔案 | 行 | 內容 |
|---|---|---|
| `Royalty/QuestNode_Root_PromotionCeremony.cs` | 129 | `HistoryEventDefOf.QuestPawnLost`（派系好感原因） |
| `Royalty/QuestNode_Root_PromotionCeremony.cs` | 154 | `HistoryEventDefOf.ShuttleDestroyed`（同上） |
| `SurgicalApparel/Recipe_RemoveSurgicalApparel.cs` | 70 | `TaleDefOf.DidSurgery` |

也就是說，DMS 目前所有**自製的重大事件**（空中支援、Boss 召喚、軍法審判、完訓晉升、蛙人孵化、地下設施探索、死人開關觸發）都不會留下任何 Tale，藝術品與回憶系統對這個模組是完全「無感」的。

---

## 二、動手前要先確認的兩件事

### HistoryEventDef 本身「沒有效果」

`HistoryEventDef` 只是一個標籤。要產生實際遊戲效果，必須有東西監聽它：

- `PreceptComp_SelfTookMemoryThought`（自己做了 → 自己得 memory）
- `PreceptComp_KnowsMemoryThought`（同信仰的人知道了 → 全體得 memory）
- `PreceptComp_UnwillingToDo`（拒絕執行）
- `PreceptComp_DevelopmentPoints`（流動信仰成長點）

**DMS 目前的 Ideology 內容只有 `CultureDef` 與 `StyleCategoryDef`，沒有任何 meme / precept。** 所以純粹加 HistoryEventDef 不會有效果，除非同時做下列其中之一：

1. 加一組 DMS 自己的 meme／precept（例如「軍事紀律」「服從鏈」「不信任血肉」），讓它們監聽新事件；
2. 走**不需要 precept 也有效**的兩條路：
   - `QuestScriptDef.successHistoryEvent` / `failedOrExpiredHistoryEvent` → 自動記錄，且會餵流動信仰的成長點（`IdeoDevelopmentUtility`）；
   - `QuestPart_FactionGoodwillChange.historyEvent` / `Faction.TryAffectGoodwillWith(reason:)` → 在派系關係頁顯示「為什麼好感變動」，DMS 的軍務任務很吃這個。
3. Patch 原版 precept 的 `comps`，把 DMS 的事件塞進去（例如把 `DMS_UsedAirSupport` 加進和平主義相關 precept）。

### TaleDef 要成立需要一整包東西

- `taleClass`：`Tale_SinglePawn` / `Tale_SinglePawnAndDef` / `Tale_SinglePawnAndThing` / `Tale_DoublePawn` / `Tale_DoublePawnAndDef` / `Tale_DoublePawnKilledBy` / `Tale_*AndTrader`，要跟 `RecordTale` 傳入的參數數量對齊，否則會噴 error。
- `type`：`Volatile`（會被新事件擠掉）/ `Expirable` / `Permanent`。DMS 的「一生一次」事件（首次進入地下設施、死人開關觸發）適合 `Permanent` 或 `Expirable`。
- `rulePack`：至少要有 `tale_noun->`、`image->`、`desc_sentence->`，否則做成藝術品時描述會是空的。
- `defSymbol` / `firstPawnSymbol` / `secondPawnSymbol` 要跟 rulePack 內用的符號一致。
- **三語同步**：`rulesStrings` 屬於可翻譯內容，需要在 `Languages/{English,ChineseTraditional,ChineseSimplified}/DefInjected/TaleDef/*.xml` 各補一份（繁→簡照慣例用 tw2s）。這是最大的工作量，不是 def 本身。

---

## 三、純 XML 就能掛的位置（零 C#，優先做）

原版提供這些可直接填 def 的欄位：

| 欄位 | 觸發時機 | DMS 可用對象 |
|---|---|---|
| `VerbProperties.colonyWideTaleDef` | `Verb_Spawn` / `Fortified.Verb_Deploy` 部署成功時 | **`Defs/Things_Apparel/Misc.xml:115`、`:275`** 兩個 `Fortified.Verb_Deploy` 目前欄位空著 → 這是全案最便宜的一個掛點 |
| `HediffDef.taleOnVisible` | hediff 首次變可見 | `DMS_MechlinkOverload`、`DMS_MechlinkRejection`、`DMS_SyntheticRejection`、`DMS_BionicShock` |
| `HediffStage.tale` | 進入該 stage | `DMS_WeaponInprint` 升到最高階（「這具 MG-1 終於跟它的槍長在一起了」）、`DMS_Hediff_ERAPlating` 被打爆的階段 |
| `MentalStateDef.tale` | 玩家方 pawn 進入該精神狀態 | DMS 若之後加自訂精神狀態（目前無） |
| `IncidentDef.tale` / `IncidentCategoryDef.tale` | 事件發生 | `DMS_RaidWaveBeacon_PeacekeepingOperators` 引發的波次、Boss 事件 |
| `JobDef.taleOnCompletion` | job 正常完成 | `DMS_ProcessQuestWorkable`（處理軍務文件）、駭入地下設施電梯的 job |
| `ThoughtDef.taleDef` | 產生該 thought 時 | DMS 自訂 thought（若有） |
| `IngestibleProperties.ateEvent` | 吃下去（HistoryEvent） | `DMS_CombatRation`、`DMS_OverEat`、`Neuroglue` → 例如 `DMS_AteSyntheticRation`，給「拒絕人造蛋白」的 precept 用 |
| `GeneDef.deathHistoryEvent` | 帶此基因者死亡 | 蛙人／`MechBirthExtension` 掛的發育導向基因 |
| `QuestScriptDef.successHistoryEvent`<br>`QuestScriptDef.failedOrExpiredHistoryEvent` | 任務成功／失敗 | **全部 12 個 DMS 任務都沒填**（見下） |

### 目前完全沒有 successHistoryEvent 的 QuestScriptDef

`DMS_Boss`、`DMS_PromotionCeremony`、`DMS_ProcessDocumentQuest`、`DMS_SupplyChain`、`DMS_SupplyChain_Delivery`、`DMS_DecoySiteAssault`、`DMS_PirateAssemblyRaid`、`DMS_Stele`、`DMS_SteleKey_ImpactSite`、`DMS_CourtMartial`、`DMS_OfficerTraining`

建議至少分三組事件：
- `DMS_QuestSuccess_Military`（突襲、Boss、誘餌據點、海盜集結地）
- `DMS_QuestSuccess_Logistics`（補給鏈、文件處理）
- `DMS_QuestSuccess_Ceremony`（授銜、完訓、軍法無罪）

---

## 四、需要少量 C# 的位置（一行 `RecordTale` / `RecordEvent`）

依「畫面感 × 改動成本」排序：

### 1. 空中支援 — 最該做的一個
`_Fortified-Framework/_Sources/Fortified/StandaloneFunctions/AirSupport/`
（`CompAirSupportSummoner`、`RoyalTitlePermitWorker_CallAirSupport`、`GameComponent_CAS`）

呼叫近距空中支援是 DMS 最有代表性的動作，目前不留任何紀錄。
- Tale：`DMS_Tale_CalledAirSupport`（`Tale_SinglePawnAndDef`，def 用 `AirSupportDef`）
- HistoryEvent：`DMS_CalledAirSupport` — 這個非常適合掛 `PreceptComp_KnowsMemoryThought`（和平主義者反感／軍國主義者振奮）

### 2. Boss 召喚與擊殺
`_Source/DMS/CompUseEffect_SummonRaid.cs:22`（`DoEffect`）
- `DMS_Tale_SummonedBossgroup`（`Tale_SinglePawnAndDef`，def 用 `BossgroupDef`）
- 擊殺側原版已有 `TaleDefOf.KilledMajorThreat`，可確認 DMS 的 boss `ThingDef` 有沒有被判定為 major threat；若沒有，補一個 `DMS_Tale_DefeatedBossgroup`

### 3. 軍法審判判決
`_Source/DMS/Quests/CourtMartial/QuestPart_CourtVerdict.cs`，`Notify_QuestSignalReceived` 的兩個分支
- 無罪：`DMS_Tale_Acquitted`
- 有罪降階：`DMS_Tale_CourtMartialed`（`Tale_SinglePawn`，`Permanent`）
- HistoryEvent：`DMS_MemberCourtMartialed` — 同時可當作對簽發派系的好感變動理由

### 4. 完訓晉升
`_Source/DMS/Quests/OfficerTraining/QuestPart_TrainingGraduation.cs`，第 4 步發信件的位置
- `DMS_Tale_Graduated`（`Tale_SinglePawnAndDef`，def 用 `RoyalTitleDef`）
- 授銜典禮（`QuestNode_Root_PromotionCeremony`）同理

### 5. 蛙人／二次發育體孵化 — 這裡有既有缺口
`_Source/DMS/Frogman/MechBirth.cs`，`Patch_ApplyBirthOutcome_MechBirth.Prefix`

這個 Prefix 直接 `return false` 攔掉原版流程，**所以連原版的 `TaleDefOf.GaveBirth` 都不會被記錄**。成功／失敗兩個分支各補一個：
- `DMS_Tale_MechGestationSuccess`（`Tale_DoublePawn`？代孕者＋產物是建築，實務上用 `Tale_SinglePawnAndDef` 較穩）
- `DMS_Tale_MechGestationFailed`
- HistoryEvent：`DMS_GestatedMech` — 「不信任人造生命」類 precept 的天然掛點

### 6. 首次進入地下設施
`_Source/DMS/Vault/Building_VaultElevator.cs`（`MapPortal` 的進入流程）
- `DMS_Tale_EnteredVault`（`Tale_SinglePawn`，`Permanent`）
- 駭入成功也可獨立記一筆

### 7. Occultech 取得
`_Source/DMS/Quests/Stele/OccultechKey.cs`、`DMS_SolitaryZPM` / `DMS_OccultechKey` 拾取或使用
- `DMS_Tale_RecoveredOccultech`
- HistoryEvent：`DMS_UsedOccultech`，對應原版 `RelicHuntSuccess` 的位階

### 8. 一次性重武器與熱熔破障
- `Defs/Things_Weapon/MilitaryGun_OneUse.xml` 的武器發射（`CompApparelReloadable` 打完即毀那組）
- `DMS_ThermalBreacher`（`Defs/Things_Building/DMS_Misc.xml`）引爆破牆
兩者都很有畫面，但需要在 verb / comp 上加 hook。

### 9. 核心過載自毀
`DMS_NuclearOverload`（`Defs/Abilities/Common.xml:239`）— 機兵自爆
- `DMS_Tale_FissionOverload`（`Tale_SinglePawn`）
- 若之後做「機兵有人格」路線，這是最強的一個回憶素材

---

## 五、建議的實作批次

**第一批（純 XML，半天內可完成）**
1. 補上兩處 `Fortified.Verb_Deploy` 的 `colonyWideTaleDef`
2. 12 個 QuestScriptDef 補 3 組 `successHistoryEvent` / `failedOrExpiredHistoryEvent`
3. `DMS_CombatRation` / `DMS_OverEat` 補 `ateEvent`
4. 3～4 個 hediff 補 `taleOnVisible` / stage `tale`

**第二批（各一行 C#）**
空中支援 → Boss 召喚 → 軍法判決 → 完訓晉升 → 蛙人孵化

**第三批（要設計）**
一組 DMS meme / precept 來消費這些 HistoryEvent。沒有這一步，第一、二批的 HistoryEvent 只是資料；Tale 則不受影響，本來就會直接進藝術品與回憶。

**每一批都別忘了三語 DefInjected**（`TaleDef` 的 `rulePack.rulesStrings` 逐條、`HistoryEventDef` 的 `label`）。
