# DMS 脫序者接納任務實作計畫

更新日期：2026-10-16（同日依 D1–D6 定案修訂，並補完第一版第 7 節的八項查證）
目標版本：DMS 2／RimWorld 1.7（FFF 1.7）
文件狀態：**已實作並編譯通過（2026-10-16），尚未在遊戲中驗證**（第 5 節的驗收矩陣全部待跑）。第 2 節標「已查證」者為對照 1.7 `Assembly-CSharp`、FFF 1.7 原始碼與 DMS 原始碼的結果。D1–D6 皆已定案（第 6 節）。實作與本計畫不同之處見第 9 節。

## 1. 目標與範圍

遊戲第 15 天提供一次性任務「脫序者接納」（`DMS_DeviantRefuge`）。玩家接受後，一名殖民艦隊脫序者加入殖民地；加入滿 5 天時檔案還原完成，艦隊同時得知並要求引渡。**選擇交給玩家**（D7，3.11）：交出脫序者換取好感度與（皇權 DLC）艦隊榮譽，檔案被沒收；或拒絕引渡（期限內不答覆亦同），此時：

1. 取得 `DMS_Occultech_QubitDecryption`（量子位元破譯，II 級封存檔案）與 2 項隨機的 I 級封存檔案（`knowledgeCategory` 為 `DMS_Restricted`）。前置未完成者先「待授予」，前置完成後自動解譯（D1）。
2. 與殖民艦隊（`DMS_Army`）敵對；脫序者在殖民地期間鎖定敵對（D4）。
3. 以 FFF Huntdown 啟動追殺 `DMS_DeviantHunt`，綁定該脫序者。

本次範圍另含：

- SAGE 節點造成的追殺中斷（真空期）同樣適用於脫序者追殺（使用者於 D3 附註要求）。
- 軍事法庭在脫序者受庇護期間指名脫序者為被告；審判結案即結束追殺（3.7，理由見 2.3 的更正）。隱匿級永久敵對同時存在時分開審判（D6-B）。

不在本次範圍：追殺部隊鎖定脫序者本人的專屬 AI（只做地圖層級的週期襲擊）、與 DMSE「叛逃者」開局整合、任務視窗中的研究獎勵圖示（描述文字已說明）。

### 1.1 參數一覽

| 參數 | 值 | 位置 |
|---|---|---|
| 提供時間 | `minDaysPassed=14`（第 15 天開始，`TicksGame` 840000） | 說書人 comp（3.8） |
| 任務接受期限 | 3~5 天 | `QuestScriptDef.expireDaysRange` |
| 還原時間 | 5 天（300000 tick），自加入起算；之後發出引渡要求 | `QuestPart_Delay` |
| 引渡答覆期限 | 2 天，逾時視為拒絕 | `extraditionDecisionDays` |
| 交出的獎勵 | 艦隊好感度 +25；皇權 DLC 另有 10 點榮譽（由玩家指定一名殖民者）；殖民者得到「放逐殖民者」心情懲罰 | `extraditionGoodwill`／`extraditionHonor`／`extraditionMoodPenalty` |
| 授予 | qubit ＋ 2 項 I 級 | `QuestPart_DeviantHandover` |
| 追殺警報／襲擊 | 交付當下起右側倒數警報（不發預警信件），襲擊 5~8 天，重複 | `DMS_DeviantHunt` |
| 追殺點數 | 威脅點數 ×1.0，下限 300 | `DMS_DeviantHunt.waves` |
| 追殺伴隨任務 | 軍事法庭 100%、SAGE 節點 35% | `questsOnWave` |

## 2. 已確認的現況與限制

### 2.1 研究完成（已查證，1.7 `ResearchManager`）

- `FinishProject(proj, doCompletionDialog, researcher, doCompletionLetter)` 開頭對每個 `prerequisites` 中未完成者**遞迴呼叫 `FinishProject`**（不含 `hiddenPrerequisites`），每一層都照樣發完成信件。
- 一般研究寫入 `progress`；知識型（`baseCost == 0`、`knowledgeCost > 0`）只在 Anomaly 啟用時寫入 `anomalyKnowledge`（並送 `ThingStudied` 訊號）。**無 Anomaly 時不寫任何進度。**
- 之後依序：`ReapplyAllMods()`、`recalculatePower`、完成對話框（`doCompletionDialog`）、`discoveredLetterTitle` 信件（`doCompletionLetter`）、清 `currentProj` 與 Anomaly 目前知識專案、`Notify_UnlockedByResearch()`、全域訊號 `ResearchCompleted`。
- `ResearchProjectDef.IsFinished => ProgressReal >= Cost`，只看進度。`PrerequisitesCompleted` 檢查 `prerequisites` 與 `hiddenPrerequisites`。
- `ApplyKnowledge(ResearchProjectDef, float, out float)`（Anomaly）：**不檢查前置就寫入 `anomalyKnowledge`**；只有前置完成且已滿時才 `FinishProject(proj, doCompletionDialog: true, null, doCompletionLetter: false)`：彈出對話框、**不發研究信件**。
- FFF `Patch_ResearchManager_GetProgress`：無 Anomaly 時，知識型專案的進度改讀 `GameComponent_KnowledgeStore`。**只把 store 填滿就會讓 `IsFinished` 變 true，卻不經 `FinishProject`**：沒有信件、沒有制裁、沒有任務線派發。
- FFF `KnowledgeUtility.AddProgressTo`：前置未完成時照樣填滿 store（觸發上一點的靜默完成）；Anomaly 時走 `ApplyKnowledge`（對話框、無信件、前置未完成也會靜默完成）。**因此本任務不使用 `AddProgressTo`，改自行呼叫 `FinishProject`（3.2）。**

前置鏈（已查證）：

| 專案 | 等級 | 前置 | 遞迴完成會連帶送出 |
|---|---|---|---|
| `DMS_Occultech_QubitDecryption` | II（`DMS_Sealed`） | `MicroelectronicsBasics`(3000) ← `Electricity`(1600) | 最多 4600 點常規研究 |
| `DMS_Occultech_BionicSuitHeavy` | I | `DMS_Artifuscle`(750) ← `SterileMaterials`、`DMS_Plasfiber` | 數項 |
| `DMS_Occultech_GestaltEngine` | I | `DMS_Mechlink`(1500) ← `DMS_AdvancedControl`(1500) ← `DMS_MechBasis`…、`SterileMaterials` | 整段機兵樹 |
| `DMS_Occultech_Biosynthesis` | I | `DMS_Artifuscle` | 同上 |
| `DMS_Occultech_Subsonic` | I | `MicroelectronicsBasics` | 同 qubit |
| `DMS_Occultech_RemnantPatterns` | I | `DMS_AdvancedControl` | 數項 |
| `DMS_Occultech_HydraulicFracturing` | I | `DeepDrilling`(1000) ← `MicroelectronicsBasics`；`ToxifierGenerator`(500, Biotech) | 多項 |

### 2.2 封存科技制裁（已查證，DMS）

- `Patch_ResearchManager_FinishProject`（postfix）依序呼叫 `OccultechSanctionUtility.Notify_ProjectFinished(proj)` 與 `ResearchQuestUtility.Notify_ProjectFinished(proj)`。
- 制裁以 `GameComponent_OccultechSanction.MarkSanctioned(proj)` 去重（public，回傳 false 表示已標記）。**事先標記的專案不會再結算。**
- 封存級（qubit）：`SetFleetHostile` ＋ `TryFireFleetRaid(1.25, 400)` ＋ ThreatBig 信件；之後 `IsAllyLocked`（持有未放棄的封存級）使關係最高只到中立。准將（seniority 1000）以上豁免。
- 限制級（I 級）：每項 −80 好感並發信；准尉（200）以上豁免。
- qubit 掛 `ModExtension_ResearchQuest`，完成時派發石碑任務線 `DMS_Stele`（終生一次）。**保留**：取得解碼技術就該開啟石碑線。
- `OccultechSanctionUtility.SetFleetHostile(fleet)` 以 FFF `FactionRelationUtility.SetGoodwillAndKind(-100, Hostile)` 繞過自家守衛，`reason` 固定為 `DMS_OccultechResearched`。
- 守衛：`Patch_Faction_CanChangeGoodwillFor` 與 `Patch_FactionRelation_CheckKindThresholds` 只在 `PermanentHostile`（且無進行中的軍事法庭）或 `IsAllyLocked` 時介入；`EnforceFleetRelation()` 每 2000 tick 兜底。
- 通訊台「放棄封存級技術」選項：`PermanentHostile` 時停用（`DMS_Occultech_RenounceBlockedPermanent`），否則可用。

### 2.3 FFF Huntdown 與軍事法庭（已查證）

- 一個 `HuntdownDef` 同時只有一個 `HuntdownInstance`，所以脫序者追殺必須是**新的 Def**，不能共用 `DMS_OccultechHunt`。`HuntdownDef` 是一般 `Def`，可掛 `modExtensions`。
- `HuntdownUtility.Start(def, map, initial, source, boundPawn)`：已啟動時只補地圖與綁定。`HuntdownInstance.boundPawn` 的註解即以「the deviant」為例。
- `GameComponent_Huntdown` 每 250 tick：`hasBoundPawn && stopWhenBoundPawnLost && worker.BoundPawnLost(pawn)` 則 `Stop`；暫停中（`suspendedUntilTick > now`）的 instance 整個略過；暫停結束時所有地圖 `Reschedule` 並呼叫 `worker.OnResumed`。
- `HuntdownUtility.Suspend(def, ticks)`：`until = max(now, until) + ticks`，第一次進入暫停時呼叫 `OnSuspended`。
- `retargetPlayerHome`：沒有追蹤地圖時改追 `Find.AnyPlayerHomeMap`。`CanFireWave` 回 false 時整張地圖重新排程。`postponeWhileNotHostile` 預設 true。
- `HuntdownWorker.TryOfferQuest(offer, map)` 為 protected static，同 root 待接或進行中時不重複。
- ConfigErrors：`raidDelayRange.min < warningDelayRange.max` 會報錯。
- **更正（第一版計畫有誤）**：`QuestPart_CourtTruce` 在被告歸還（`inSignalSuccess`）與無罪（`inSignalAcquitted`）時都會呼叫 `ResolveBoundHuntdowns()` → `HuntdownUtility.StopAllBoundTo(defendant)`，並發訊息 `DMS_CourtMartial_HuntdownEnded`。軍事法庭本來就是綁定追殺的出口。
- `QuestPart_CourtTruce.QuestPartTick()` 每 250 tick 檢查：休戰期間艦隊轉為敵對即送 `truceBroken`，任務以失敗結束並發「協議破裂」信件。
- `QuestNode_Root_CourtMartial.FindDefendant` 取官階最高者，否則取市場價值最高的自由殖民者（排除任務借住者）；描述以常數 `defendantRanked`（`True`／`False`）分流；`QuestPart_CourtVerdict` 以 `demoted` 選 `verdictLetterText`／`verdictLetterNoDemotionText`。

### 2.4 研究探明（已查證，FFF）

- 封存科技分頁掛 `ModExtension_ResearchDiscovery`（`hideUntilDiscovered`），前置未完成的專案顯示為未知研究，且不接受知識注入。
- `ResearchDiscoveryUtility.ForceDiscover(proj, suppressLetter: true)` 永久探明，存檔。

### 2.5 第 15 天觸發（已查證，1.7）

- `StorytellerComp_SingleOnceFixed`：只在 `TicksGame / 1000 == fireAfterDaysPassed × 60` 那一個間隔檢查；中途加入模組或當下 TestRun 失敗就**永遠不會再觸發**。原版 `Intro_Deserter` 用此法（第 26 天）。
- `StorytellerComp_RefiringUniqueQuest`：任務管理器中沒有同 root 任務（含已結束者）時，新遊戲在 `minDaysPassed × 60 + 1` 間隔觸發；`Initialize()` 時已超過門檻（中途加入、讀舊檔）則設 `generateSkipped`，之後每個間隔都嘗試到成功。先檢查 `Worker.CanFireNow`。`refireEveryDays < 0` 時失敗或過期後不補發。
- 新遊戲剛好在觸發間隔 TestRun 失敗時，要到下次讀檔才會再試（`generateSkipped` 只在 `Initialize` 設定）。可接受。
- 「第 15 天」＝已過 14 天＝`TicksGame ≥ 840000`（D5-A）。
- 所有原版說書人與 DMS 的格蕾西亞都繼承 `BaseStoryteller`，`<comps>` 未設 `Inherit="False"`（Tutor 例外）。patch `BaseStoryteller` 的 comps 即可涵蓋。
- `IncidentDef.questScriptDef` 指向的任務必須 `rootSelectionWeight = 0`（原版 ConfigErrors）。
- `ModContentPack.LoadPatches()` → `DirectXmlLoader.XmlAssetsInModFolder(mod, "Patches/")` 走訪 `foldersToLoadDescendingOrder` 的每個資料夾，所以 `1.7/NewContent/Patches/` 會被載入。同一相對路徑以優先序較高的資料夾為準：檔名必須不與 `1.7/Patches/` 重複。

### 2.6 任務節點與 pawn 訊號（已查證，1.7）

- `QuestNode_GeneratePawn.RunInt()`：`PawnGenerator.GeneratePawn(request)`（`ForbidAnyTitle` 可設）→ `QuestGen.AddToGeneratedPawns(pawn)` → `Find.WorldPawns.PassToWorld(pawn)`。以 slate key 存入後，描述可用 `[key_nameFull]` 等 pawn 規則。
- `QuestNode_PawnsArrive.RunInt()` 建立 `QuestPart_PawnsArrive`（`pawns`、`arrivalMode` 預設 `EdgeWalkIn`、`joinPlayer`、`mapParent` 取 slate 的 `map`、`spawnNear` 取 `walkInSpot`、`customLetterLabel/Text/Def`）。`isSingleReward` 另加 `QuestPart_Choice` 與 `Reward_Pawn`，任務視窗會把該 pawn 列為獎勵。**沒有 DLC 閘控。**
- `QuestPart_PawnsArrive.Notify_QuestSignalReceived`：收到 `inSignal` 時先 `SetFaction(Faction.OfPlayer)`（joinPlayer），再 `Arrive`，並發信件（有自訂文字時取代原版文字）。**加入與接受在同一 tick**；地圖失效時改投 `TryFindNewSuitableMapParentForRetarget()`，仍找不到就靜默返回（不加入）。
- `Thing.SetFaction` 對任務 tag 送出 `ChangedFaction`，以及 `ChangedFactionToPlayer` 或 `ChangedFactionToNonPlayer`（新派系為 null 也算後者）。
- `KidnappedPawnsTracker.Kidnap` 送出 `Kidnapped`；**被綁架者的 `Faction` 不變**，以 `PawnUtility.IsKidnappedPawn(pawn)` 判斷。
- `PawnBanishUtility.Banish` 送出 `Banished`。`QuestUtility` 另有 `Killed`、`Destroyed`、`Arrested`、`Recruited`、`Enslaved`、`LeftMap` 等常數；原版任務 XML 以 `asker.Destroyed`、`lodgers.Destroyed` 處理 pawn 死亡。
- 結論：死亡訊號的確切來源不必依賴。脫序者是否「失去」改用同一個輪詢判定（3.5 的 `IsLost`），任務與追殺共用。

### 2.7 派系關係（已查證）

- `DMS_Army` 未覆寫自然好感，沿用 `FactionBase` 預設。
- 原版 `Faction.CheckReachNaturalGoodwill()`：好感低於「自然好感 −50」時累加計時器，滿 3000000 才 `TryAffectGoodwillWith(+≤10, reason: ReachNaturalGoodwill)`。**經過 `CanChangeGoodwillFor`，所以 DMS 守衛擋得住。**
- 艦隊船員 PawnKind：`DMS_CrewMember`（`DMS_CivilianBase`，`defaultFactionType=DMS_Army`，背景 `FleetChild`／`Mili_Standard`／`Mili_Expert`）。

### 2.8 SAGE 追殺中斷（已查證，DMS）

- `CompHuntBreaker.PostDestroy`（只認 `KillFinalize`）→ `OccultechSanctionUtility.TrySuspendHunt(props, source, map)`。
- 目前只看 `PermanentHostile`：不成立時只發 `DMS_HuntBreaker_NoHunt` 訊息；成立時以 `props.RollTicks(wasSuspended)`（首次 `suspendYears`，疊加 `stackDaysWhileSuspended`）暫停 `DMS_OccultechHunt`，發 `DMS_HuntBreaker_SuspendedLabel/Text` 信件（內文最後一段提到「永久敵對未解除」）或 `Extended` 訊息。
- `HuntdownWorker_Occultech.OnResumed`：發 `DMS_HuntBreaker_ResumedLabel/Text`（內文通用）並補發軍事法庭。
- `CompHuntBreaker.CompInspectStringExtra` 與 SAGE 任務 `QuestNode_Root_DMS_FleetNetworkSite.HuntRunning()` 都只看 `PermanentHostile && !HuntSuspended`。

### 2.9 查證結案紀錄

| 項目（第一版第 7 節） | 結論位置 | 摘要 |
|---|---|---|
| `QuestNode_PawnsArrive`／`GeneratePawn` 簽章與 DLC 閘控 | 2.6 | 無閘控；接受同 tick 加入；`isSingleReward` 顯示 pawn 獎勵 |
| pawn 任務訊號名稱 | 2.6 | `Banished`、`Kidnapped`、`ChangedFactionToNonPlayer` 等；死亡改用輪詢判定 |
| `ApplyKnowledge(proj)` 完成行為 | 2.1 | 對話框、無信件、前置未完成也寫入；不採用 |
| 自然好感回復 | 2.7 | 經 `TryAffectGoodwillWith`，守衛有效 |
| 被綁架者派系 | 2.6 | 派系不變，用 `PawnUtility.IsKidnappedPawn` |
| 艦隊船員 PawnKind | 2.7 | `DMS_CrewMember` |
| `NewContent/Patches/` 載入 | 2.5 | 會載入 |
| 歷史事件 reason | 3.4 | 新增 `DMS_HarboredDeviant` |

## 3. 設計

### 3.1 任務流程

```
[第 15 天] RefiringUniqueQuest 觸發 GiveQuest → 任務可接受（3~5 天）
   │ 拒絕／過期 → 結束；終生不再提供
   ▼ 接受（inSignal）
QuestPart_PawnsArrive：脫序者 SetFaction(玩家) → 從地圖邊緣步行進場，發抵達信件
   │ 送出 deviant.ChangedFactionToPlayer（sigJoined）
   │ 保險：接受後 1 天內未加入 → 任務失敗（地圖失效、無法進場；由 Watch 輪詢判定）
   ▼ sigJoined
QuestPart_DeviantWatch（每 250 tick，直到做出選擇）：IsLost(脫序者) → 依原因（死亡／離開／被綁架）發失敗信件，任務失敗
QuestPart_Delay 5 天（任務頁倒數）
   ▼ sigDemand
QuestPart_DeviantExtraditionDemand：選擇信件（交出／拒絕／稍後），2 天期限，逾時＝拒絕
   │ 先送 sigDecided（停用 Watch）
   ├─ 交出 ► QuestPart_DeviantExtradition（3.11）：心情懲罰、帶走脫序者、好感度、
   │          皇權 DLC 選人信件 → QuestPart_GiveRoyalFavor → 任務結束（Success）
   ▼ 拒絕 ─────────────────────────────────────────────┐
QuestPart_DeviantHandover                            │
   1. 再驗一次 IsLost（失去則改走失敗）               │
   2. 授予：qubit ＋ 挑 2 項 I 級（3.2、3.3）          │
   3. 撤銷待接受、被告不是脫序者的軍事法庭傳票（3.7） │
   4. 軍事法庭進行中？──是──► 延後：每 250 tick 檢查， │
      │                        審判結束後才做 5～6     │
      ▼ 否                                            │
   5. SetFleetHostile(reason: DMS_HarboredDeviant)    │
   6. Start(DMS_DeviantHunt, 脫序者所在據點, bound)   │
      ＋ 承接 SAGE 真空期（3.6）                       │
   7. ThreatBig 信件：實際取得的專案清單              │
   ▼                                                  │
任務結束（Success）。之後由 Huntdown 與關係鎖接手。 ◄─┘
```

追殺結束的出口：脫序者死亡、離開派系（放逐、被逮捕、被招募走）、被綁架，或在軍事法庭受審結案（3.7）。

### 3.11 引渡要求（D7-A）

脫序要由玩家自己實踐：第 5 天不再自動交付，改由艦隊發出引渡要求，玩家在 `ChoiceLetter_DeviantExtradition`（LetterDef `DMS_DeviantExtradition`，不能右鍵關閉）中選擇。

| 選擇 | 結果 |
|---|---|
| 拒絕引渡（或 2 天內不答覆） | 原本的交付：授予研究、敵對、追殺（3.1 的 `QuestPart_DeviantHandover`） |
| 交出脫序者 | 原版「放逐殖民者」心情懲罰（須在改派系前給）→ 從商隊或地圖移出、解除床位、改屬艦隊、交給世界 pawn → 艦隊好感度 +25（理由 `DMS_ExtraditedDeviant`，仍受關係守衛限制）→ 引渡完成信件 → 皇權 DLC 且艦隊有官階：原版 `ChoiceLetter_ChoosePawn` 由玩家指定領受者，`QuestPart_GiveRoyalFavor` 給 10 點榮譽，選完才結束任務；否則立即結束。檔案沒收，不授予任何研究 |
| 稍後再決定 | 信件留在信件欄；任務頁顯示「引渡答覆期限」倒數 |

- 選擇前脫序者失去（死亡、離開、被綁架）→ 任務失敗，信件隨任務結束消失。
- 榮譽量 10 約等於艦隊官階一級（下士 10、中士 12）。
- 交出之後脫序者以艦隊派系的世界 pawn 存在；不建立軍事法庭、追殺或關係鎖。

### 3.2 授予研究（D1-A）

新增 `DMS.DeviantTechUtility`：

```
Grant(proj):
  proj.IsFinished → AlreadyHeld
  GameComponent_OccultechSanction.CompSafe.MarkSanctioned(proj)        // D3-A：通用制裁不再結算
  ResearchDiscoveryUtility.ForceDiscover(proj, suppressLetter: true)
  !proj.PrerequisitesCompleted → GameComponent_DeviantLegacy.pending.Add(proj) → Pending
  Complete(proj) → Granted

Complete(proj):
  無 Anomaly：GameComponent_KnowledgeStore.CompSafe.SetStored(proj, proj.knowledgeCost)   // 先寫進度，postfix 讀得到 IsFinished
  Find.ResearchManager.FinishProject(proj, doCompletionDialog: false, null, doCompletionLetter: true)
```

- 前置完成才呼叫 `FinishProject`，不會遞迴送出常規研究；Anomaly 下 `FinishProject` 自己寫 `anomalyKnowledge`。
- 待授予的專案**不寫進度**，避免 2.1 的靜默完成。
- 觸發點：在既有 `Patch_ResearchManager_FinishProject` postfix 末端加 `DeviantTechUtility.Notify_ProjectFinished(proj)`。走訪 `pending` 的複本：已完成者移除（玩家自行研究完）；前置已齊者**先移除再** `Complete` 並發 `DMS_DeviantRefuge_PendingDecoded*` 信件。巢狀的 `FinishProject` 會再進 postfix，靠「先移除」與重入旗標避免重複。`LoadedGame()` 補檢一次。
- qubit 完成時照常派發 `DMS_Stele`（`ResearchQuestUtility` 不受 `MarkSanctioned` 影響）。
- 信件中待授予的專案列出尚缺的前置（`prerequisites` 與 `hiddenPrerequisites` 中未完成者的 `LabelCap`）。

### 3.3 I 級專案挑選（D2-A）

候選：`knowledgeCategory == DMS_Restricted`、未完成、不在 `pending`。先從 `PrerequisitesCompleted` 者以 `InRandomOrder()` 取，不足 2 項再從其餘候選補。挑選在交付當下執行（任務描述不洩漏未探明專案名稱）。使用 `Verse.Rand`（quest tick 內，Multiplayer 一致）。候選不足時只給現有的，信件照實列出；一項都沒有時用 `DMS_DeviantRefuge_NothingGranted`。其他模組加入 `DMS_Restricted` 的專案同樣入選。

### 3.4 敵對與關係鎖（D4-A）

- 新增 `HistoryEventDef DMS_HarboredDeviant`；`SetFleetHostile(Faction fleet, HistoryEventDef reason = null)` 加 reason 參數，預設維持 `DMS_OccultechResearched`。
- `DeviantHuntUtility.HarboredDeviant`：`DMS_DeviantHunt` 有 instance、`hasBoundPawn` 且 `!IsLost(boundPawn)` 時回傳該 pawn。O(追殺數)，不需快取。
- `OccultechSanctionUtility.HostilityLocked` ＝ `(PermanentHostile || HarboredDeviant != null) && !CourtMartialOngoing`。下列三處改用它：`Patch_Faction_CanChangeGoodwillFor`、`Patch_FactionRelation_CheckKindThresholds`、`EnforceFleetRelation()`（後者照舊用不走快取的審判判定）。`EnforceFleetRelation` 因脫序者而把關係打回敵對時發 `DMS_DeviantHunt_GoodwillBlocked` 訊息。
- 通訊台放棄選項：脫序者受庇護時停用，理由 `DMS_Occultech_RenounceBlockedDeviant`（艦隊不受理）。
- 追殺結束只解除鎖，不自動回復好感；仍持有 qubit 時 `IsAllyLocked` 照舊卡在中立。

### 3.5 追殺 Def 與 Worker

```xml
<Fortified.HuntdownDef>
  <defName>DMS_DeviantHunt</defName>
  <label>deviant pursuit</label>
  <workerClass>DMS.HuntdownWorker_Deviant</workerClass>
  <faction>DMS_Army</faction>
  <followGravship>true</followGravship>
  <retargetPlayerHome>true</retargetPlayerHome>
  <repeatWhileStaying>true</repeatWhileStaying>
  <stopWhenBoundPawnLost>true</stopWhenBoundPawnLost>
  <warningDelayRange>0~0</warningDelayRange>           <!-- 立即「預警」：倒數警報從交付起就顯示 -->
  <sendWarningLetter>false</sendWarningLetter>         <!-- 交付信件已說明追殺 -->
  <raidDelayRange>300000~480000</raidDelayRange>        <!-- 5~8 天 -->
  <criticalAlertLeadTicks>60000</criticalAlertLeadTicks>
  <waves>
    <li><pointsMultiplier>1.0</pointsMultiplier><minPoints>300</minPoints></li>
  </waves>
  <questsOnWave>
    <li><quest>DMS_CourtMartial</quest></li>
    <li><quest>DMS_FleetNetworkSite</quest><chance>0.35</chance></li>
  </questsOnWave>
  <modExtensions>
    <li Class="DMS.ModExtension_FleetTracking" />   <!-- SAGE 真空期適用（3.6） -->
  </modExtensions>
  <!-- letterLabel / letterText / alert* 見第 8 節 -->
</Fortified.HuntdownDef>
```

共用判定 `DeviantHuntUtility.IsLost(Pawn p, out LostReason reason)`：`p == null || p.Dead || p.Destroyed || p.Discarded` → `Died`；`PawnUtility.IsKidnappedPawn(p)` → `Kidnapped`；`p.Faction != Faction.OfPlayer` → `Left`（放逐、逮捕、被招募走）。被軍事法庭借走的被告派系不變，不算失去。

| `HuntdownWorker_Deviant` 覆寫 | 規則 |
|---|---|
| `BoundPawnLost` | `IsLost`；成立時記下 pawn 與原因供 `OnStopped` 使用 |
| `StillHunted` | `base && map.IsPlayerHome` |
| `CanTrackMap` | `base && map.IsPlayerHome && !Dormant`；`Dormant` ＝隱匿級追殺令進行中或軍事法庭審理中（讓位，避免同期雙重襲擊；休戰期間不追蹤） |
| `TryFireWave` | 以 `DMSMusicEventBridge.FireAndRegisterHuntdown` 包住，沿用追殺選曲 |
| `OnStopped` | 只有 `BoundPawnLost` 記下原因時才發信（`EndedDied`／`EndedLeft`）；軍事法庭結案由 `QuestPart_CourtTruce` 自己發訊息 |
| `OnResumed` | 隱匿級追殺令不在進行時，發 `DMS_HuntBreaker_ResumedLabel/Text`；同時進行時由追殺令的 worker 發（避免兩封） |

### 3.6 SAGE 真空期共用（使用者附註）

- 新增空的 `DMS.ModExtension_FleetTracking`，掛在 `DMS_OccultechHunt` 與 `DMS_DeviantHunt`：表示「依賴艦隊 SAGE 追緝網路」。
- 新增 `DMS.FleetTrackingUtility`：
  - `ActiveTracked()`：進行中且帶此擴充的 instance。
  - `BlackoutTicksLeft`：上列 instance 中 `SuspendedTicksLeft` 的最大值；`InBlackout` 為其 > 0。
  - `SuspendAll(int ticks)`：先算共同結束點 `end = max(now, 各 instance.suspendedUntilTick 的最大值) + ticks`，再對每個 instance `Suspend(def, end − max(now, 自己的 until))`，讓所有追殺同時恢復。
  - `InheritBlackout(HuntdownDef def)`：追殺啟動時若 `InBlackout`，立即 `Suspend(def, BlackoutTicksLeft)`。在 `QuestPart_DeviantHandover` 與 `GameComponent_OccultechSanction.BeginPermanentHostility()` 的 `Start` 之後呼叫。
- `TrySuspendHunt` 改為：沒有任何 `ActiveTracked` → 照舊發 `NoHunt`；否則 `wasSuspended = InBlackout`，`SuspendAll(props.RollTicks(wasSuspended))`。信件依進行中的追殺分流：只有隱匿級 → 原 `SuspendedText`；只有脫序者 → `DMS_HuntBreaker_SuspendedTextDeviant`；兩者 → 原 `SuspendedText` ＋ `DMS_HuntBreaker_SuspendedDeviantLine`。疊加時照舊發 `Extended` 訊息。
- `CompHuntBreaker.CompInspectStringExtra` 與 `QuestNode_Root_DMS_FleetNetworkSite.HuntRunning()` 改用「有 `ActiveTracked` 且不在真空期」。`GameComponent_OccultechSanction.HuntSuspended`／`HuntResumeTicksLeft` 保留 API，內部改讀 `FleetTrackingUtility`。
- `SAGE` 節點只暫停追殺，不解除敵對或關係鎖（與隱匿級一致）。

### 3.7 軍事法庭指名脫序者（D4 附註的修正）

第一版計畫把「指名脫序者為被告」延到第二版。依 2.3 的更正，結案即結束綁定追殺的機制早已存在；而在 D4-A 的關係鎖下，若被告不是脫序者，審判結案回到中立後會立刻被鎖打回敵對，玩家白白交出一名殖民者。因此移入本版：

**審判種類**（`QuestNode_Root_CourtMartial.RunInt` 生成時決定，存在 `QuestPart_CourtTruce` 上）：

| 種類 | 條件（依序） | 被告 | 刑度 | 結案效果 |
|---|---|---|---|---|
| 脫序者審判 | `HarboredDeviant != null` | 只能是脫序者 | 一般（官階曲線，無軍銜即 60 天；可無罪） | 結束 `DMS_DeviantHunt`；關係見下 |
| 隱匿級審判 | `PermanentHostile` | 既有 `FindDefendant`（官階最高，否則市場價值最高） | 60 天、不得無罪、不得升盟友（不變） | 解除永久敵對與追殺令（不變） |
| 一般審判 | 其他 | 既有 `FindDefendant` | 不變 | 不變 |

隱匿級永久敵對與脫序者同時存在時採 D6-B（分開審判）：先審脫序者，結案後才輪到隱匿級審判。

- `TryFindMapAndDefendant`：脫序者審判的被告必須是某玩家據點上已生成、非任務借住者的自由殖民者；否則沒有被告，TestRun 失敗（追殺下一波會再試）。
- 描述：常數 `defendantRanked` 增加值 `Deviant`，三則 `questDescription(defendantRanked==Deviant)` 結尾接 `[killOrderNote]`；另加常數 `killOrder`（生成時的 `PermanentHostile`），規則 `killOrderNote(killOrder==True)` 說明追殺令仍在、需另審官階最高者，`killOrderNote(killOrder==False)` 說明結案後回到中立。兩則都有內容，不依賴空字串規則。原版 `Rule_String` 支援以逗號分隔多個條件（已查證），本設計只用單一條件。
- `QuestPart_CourtTruce` 新增 `deviantTrial`：`AllyBlocked` 改為 `occultechTrial || deviantTrial || IsAllyLocked`，盟友擲骰機率設 0；無罪判定照常保留（基礎機率＋社交）。
- 脫序者審判結案（刑滿或無罪）：既有 `ResolveBoundHuntdowns()` 結束 `DMS_DeviantHunt`。接著**以結案當下**的 `PermanentHostile` 分流（審判期間可能有人研究完隱匿級技術）：
  - 不成立：回到中立，信件沿用 `returnNeutralLetterLabel/Text`；無罪用 `acquitDeviantLetterText`。
  - 成立：直接設回敵對（−100），不經過中立，避免守衛下一輪才翻回去；信件 `returnDeviantKillOrderLetterLabel/Text`，無罪用 `acquitDeviantKillOrderLetterText`。之後登記傳票整理，下一個檢查週期補發隱匿級傳票（時序見第 7 節第 5 點）。
- `QuestPart_CourtVerdict`：被告為脫序者時改用 `verdictLetterDeviantText`。
- **傳票整理** `CourtMartialOfferUtility.Refresh()`（取代並泛化 `WithdrawOrdinaryCourtMartialOffers`）：算出此刻應有的審判種類，撤銷種類不同的待接受傳票（`quest.End(InvalidPreAcceptance, sendLetter: false)`），有撤銷且艦隊仍敵對時呼叫 `EnsureCourtMartialOffered()`。一律以 `RequestRefresh()` 登記、由 `GameComponent_DeviantLegacy` 在下一個 250 tick 檢查時執行（理由見第 7 節第 5 點；`OnStopped` 可能正處於軍事法庭的訊號處理中）。登記點：交付生效時、`DMS_DeviantHunt` 停止時（`HuntdownWorker_Deviant.OnStopped`）、`ApplyOcculted`（原本立即撤銷的位置改為登記）、脫序者審判結案時。
- 交付時已有審判進行中：先授予研究，敵對與追殺延後到審判結束（3.1 第 4 步）。交付信件改用 `DMS_DeviantRefuge_HandoverTextDeferred`；生效時發 `DMS_DeviantRefuge_HostilityLabel/Text`，並 `Refresh()`。

### 3.8 第 15 天觸發（D5-A）

- `IncidentDef DMS_GiveQuest_DeviantRefuge`（`ParentName="GiveQuestBase"`、`questScriptDef=DMS_DeviantRefuge`、`baseChance=0`）。
- `1.7/NewContent/Patches/DMS_DeviantRefuge_Storyteller.xml`：`PatchOperationAdd`，xpath `Defs/StorytellerDef[@Name="BaseStoryteller"]/comps`，加入 `StorytellerCompProperties_RefiringUniqueQuest`（`incident=DMS_GiveQuest_DeviantRefuge`、`minDaysPassed=14`、`refireEveryDays=-1`、`allowedTargetTags: World`）。
- `QuestScriptDef DMS_DeviantRefuge`：`rootSelectionWeight=0`、`isRootSpecial=true`、`expireDaysRange=3~5`、`defaultChallengeRating=3`、`successHistoryEvent`／`failedOrExpiredHistoryEvent` 用 `DMS_QuestSuccess_Occultech`／`DMS_QuestFailed_Occultech`。

### 3.9 任務根節點 TestRun

- 艦隊派系存在、未被消滅、`HasGoodwill`。
- 玩家派系不是 `DMS_Deviants`（叛逃者開局本身就是脫序者）。
- `DMS_DeviantHunt` 未在進行。
- 有玩家據點地圖，且能找到步行進場點（同原版 `QuestNode_GetWalkInSpot`）。
- 不要求艦隊非敵對：已敵對時任務照常，交付時的敵對設定不改變現狀，但關係鎖與追殺照樣生效。

### 3.10 脫序者 pawn

- `PawnGenerationRequest(DMS_CrewMember, faction: 艦隊, NonPlayer, tile)`，`ForbidAnyTitle = true`，成年、必須能使用暴力（`mustBeCapableOfViolence`）。
- 比照 `QuestNode_GeneratePawn`：`QuestGen.AddToGeneratedPawns` ＋ `PassToWorld`；slate key `deviant`。
- 任務 tag `deviant`（`QuestGenUtility.HardcodedTargetQuestTagWithQuestID`），`sigJoined = deviant.ChangedFactionToPlayer`。
- `QuestPart_PawnsArrive`：`joinPlayer=true`、`EdgeWalkIn`、`spawnNear=walkInSpot`、自訂信件 `DMS_DeviantRefuge_ArrivedLabel/Text`；加 `QuestPart_Choice`＋`Reward_Pawn`（不隱藏細節），任務視窗可檢視脫序者。

## 4. 程式架構與檔案

| 類型 | 路徑 | 內容 |
|---|---|---|
| C# 新增 | `_Source/DMS/Quests/Deviant/QuestNode_Root_DeviantRefuge.cs` | 3.9、3.10；組裝 parts 與訊號 |
| C# 新增 | `_Source/DMS/Quests/Deviant/QuestPart_DeviantWatch.cs` | 交付前每 250 tick 的 `IsLost` 檢查與失敗信件 |
| C# 新增 | `_Source/DMS/Quests/Deviant/QuestPart_DeviantExtraditionDemand.cs`（含 `ChoiceLetter_DeviantExtradition`） | 3.11 的引渡要求與選擇信件 |
| C# 新增 | `_Source/DMS/Quests/Deviant/QuestPart_DeviantExtradition.cs` | 3.11 的交出流程 |
| C# 新增 | `_Source/DMS/Quests/Deviant/QuestPart_DeviantHandover.cs` | 3.1 交付流程（含延後敵對）；存 `deviant`、`fleet`、`deferred`、授予結果 |
| C# 新增 | `_Source/DMS/Quests/Deviant/DeviantTechUtility.cs` | 3.2、3.3 |
| C# 新增 | `_Source/DMS/Quests/Deviant/GameComponent_DeviantLegacy.cs` | `pending` 存檔、`LoadedGame` 補檢、傳票整理的延後執行 |
| C# 新增 | `_Source/DMS/Quests/CourtMartial/CourtMartialOfferUtility.cs` | 審判種類判定、`RequestRefresh()`／`Refresh()` |
| C# 新增 | `_Source/DMS/Quests/Deviant/DeviantHuntUtility.cs` | `IsLost`、`HarboredDeviant` |
| C# 新增 | `_Source/DMS/Quests/Deviant/HuntdownWorker_Deviant.cs` | 3.5 |
| C# 新增 | `_Source/DMS/Occultech/FleetTrackingUtility.cs`（含 `ModExtension_FleetTracking`） | 3.6 |
| C# 修改 | `Occultech/OccultechSanctionUtility.cs` | `HostilityLocked`、`SetFleetHostile` reason、`TrySuspendHunt`、`EnforceFleetRelation`；`WithdrawOrdinaryCourtMartialOffers` 改由 `CourtMartialOfferUtility` 取代 |
| C# 修改 | `Occultech/Patch_OccultechSanction.cs` | 守衛改用 `HostilityLocked`；postfix 加待授予檢查；放棄選項停用條件 |
| C# 修改 | `Occultech/GameComponent_OccultechSanction.cs` | `BeginPermanentHostility` 承接真空期；`HuntSuspended` 改讀共用工具 |
| C# 修改 | `Occultech/HuntdownWorker_Occultech.cs` | 不變（`OnResumed` 照舊發信） |
| C# 修改 | `FacilityServer/CompHuntBreaker.cs`、`Quests/QuestNode_Root_DMS_FleetNetworkSite.cs` | 改看任一艦隊追殺 |
| C# 修改 | `Quests/CourtMartial/QuestNode_Root_CourtMartial.cs`、`QuestPart_CourtVerdict.cs`、`QuestPart_CourtTruce.cs` | 3.7 |
| C# 修改 | `DMS_DefOf.cs` | `DMS_DeviantRefuge`、`DMS_DeviantHunt`、`DMS_OccultechHunt`、`DMS_Restricted`、`DMS_HarboredDeviant` |
| C# 修改 | `Occultech/DebugActions_Occultech.cs` | 三個開發者動作（第 5 節） |
| XML 新增 | `1.7/NewContent/Defs/Quests/DMS_DeviantRefuge.xml` | QuestScriptDef、IncidentDef、HuntdownDef |
| XML 新增 | `1.7/NewContent/Patches/DMS_DeviantRefuge_Storyteller.xml` | 3.8 |
| XML 修改 | `1.7/Defs/Misc/DMS_Huntdowns.xml` | `DMS_OccultechHunt` 掛 `ModExtension_FleetTracking` |
| XML 修改 | `1.7/Defs/Misc/DMS_HistoryEvents.xml` | `DMS_HarboredDeviant` |
| XML（文本已寫） | `1.7/Defs/Quests/CourtMartial.xml` 與三語 DefInjected | 3.7 的描述與判決信件 |
| 語系（文本已寫） | `1.7/NewContent/Languages/{English,ChineseTraditional,ChineseSimplified}/Keyed/DMS_DeviantRefuge.xml` | 第 8 節 |

## 5. 流程驗證

開發者動作（Debug actions → DMS）：「Deviant: 立即發出任務 / Offer deviant quest now」「Deviant: 立即交付檔案 / Hand over archive now」（把還原倒數直接走完）「Deviant: 狀態 / Dump status」。另沿用 FFF 的 Huntdown 動作（立即下一波、暫停／恢復、狀態）與既有的 Occultech 動作（重放制裁、暫停追殺一年、立即結束暫停、派發 SAGE 任務）。

| # | 情境 | 預期 |
|---|---|---|
| V1 | 新遊戲，過完第 14 天 | `TicksGame` 進入 840000 後的下一個 1000 tick 間隔出現任務 |
| V2 | 第 30 天的舊存檔首次載入 | 之後第一個間隔出現任務，只出現一次 |
| V3 | 任務過期或拒絕 | 不再出現；讀檔亦不補發 |
| V4 | 叛逃者開局（`DMS_Deviants`） | 不出現 |
| V5 | 艦隊派系不存在 | TestRun 失敗，不出現，無錯誤紀錄 |
| V6 | 接受 | 脫序者同 tick 加入並步行進場，發抵達信件；任務頁顯示 5 天倒數 |
| V7 | 交付前脫序者死亡／被放逐／被逮捕／被綁架 | 250 tick 內任務失敗並發對應信件；無研究、無敵對、無追殺 |
| V8 | 交付，前置全齊 | qubit＋2 項 I 級完成；各自研究信件＋一封交付信件；石碑任務出現；**無**通用制裁信件與立即襲擊；艦隊敵對；追殺啟動並綁定脫序者 |
| V9 | 交付，前置都未完成 | 3 項皆待授予：已探明、未完成、未送出任何常規研究；信件列出缺少的前置；艦隊照樣敵對 |
| V10 | V9 之後完成 `MicroelectronicsBasics` | qubit 自動完成並發解譯信件與研究信件、派發石碑線；無制裁信件；`Subsonic` 若也在待授予中同時完成 |
| V11 | 待授予期間玩家以石碑知識自行研究完成 | 從 `pending` 移除，不重複發信 |
| V12 | I 級候選只剩 1 項或 0 項 | 只授予現有的，信件照實列出，不報錯 |
| V13 | 殖民地有准將 | 仍然敵對（任務後果不受豁免影響） |
| V14 | 追殺期間送禮、好感事件、自然好感回復 | 好感不上升；關係維持敵對 |
| V15 | 追殺週期 | 交付後 250 tick 內右側出現「脫序者追殺」倒數警報（最後一天轉紅），5~8 天襲擊玩家據點，之後重複且警報持續；不發預警信件；襲擊登記為追殺選曲 |
| V16 | 隱匿級追殺令同時進行 | 脫序者追殺不追蹤任何地圖：沒有倒數警報、不出兵；追殺令結束後重新排程，警報立即出現（襲擊 5~8 天） |
| V17 | 只有脫序者追殺時摧毀 SAGE | 脫序者追殺暫停 1~2 年；信件為脫序者版本 |
| V18 | 兩種追殺同時進行時摧毀 SAGE | 兩者暫停到同一 tick；原信件＋脫序者附註；恢復時只發一封恢復信件 |
| V19 | 隱匿級追殺令暫停中才交付脫序者 | 脫序者追殺啟動後立即承接剩餘真空期，同時恢復 |
| V20 | 脫序者追殺暫停中才觸發隱匿級 | 追殺令承接剩餘真空期 |
| V21 | 沒有任何艦隊追殺時摧毀 SAGE | 照舊只發 `NoHunt` 訊息 |
| V22 | 只有脫序者追殺時 | SAGE 節點任務可被提供；SAGE 檢視字串顯示中斷狀態 |
| V23 | 追殺期間的軍事法庭 | 被告必為脫序者；脫序者不在據點時不提供 |
| V24 | 脫序者審判結案（刑滿或無罪），無追殺令 | 追殺結束，訊息 `DMS_CourtMartial_HuntdownEnded`；關係回到中立（不會升為盟友），鎖已解除；脫序者歸隊；判決與無罪信件為脫序者版本；描述結尾為「結案後回到中立」 |
| V24a | 同 V24，但隱匿級追殺令進行中 | 脫序者追殺結束；關係直接回到敵對（不經中立）；信件為追殺令仍在的版本；隨即出現被告為官階最高者的隱匿級傳票；描述結尾為「追殺令仍在」 |
| V24b | 脫序者審判期間才研究完隱匿級技術 | 審判照常進行；結案時依當下狀態走 V24a 的分支 |
| V24c | 追殺令進行中、隱匿級傳票待接受時交付脫序者 | 隱匿級傳票被撤銷，改發脫序者傳票 |
| V25 | 交付時已有被告不是脫序者的待接受傳票 | 傳票被撤銷；立即補發指名脫序者的新傳票（無法補發時等下一波追殺） |
| V25a | 脫序者傳票待接受時脫序者死亡 | 追殺停止時撤銷該傳票；追殺令進行中時補發隱匿級傳票 |
| V26 | 交付時已有審判進行中 | 研究立即授予；敵對與追殺在審判結束後生效並發信 |
| V27 | 追殺期間開啟通訊台 | 「放棄封存級技術」停用並顯示理由 |
| V28 | 脫序者死亡或離開派系 | 250 tick 內追殺結束並發信；關係鎖解除，好感不自動回復 |
| V29 | 存讀檔：還原中、待授予中、延後敵對中、追殺中、真空期中 | 狀態完整保留 |
| V30 | 重力船帶著脫序者降落新地圖 | 追殺跟到新地圖 |
| V31 | Anomaly 啟用 | V8～V11 結果一致；無研究完成對話框 |
| V32 | 無 Royalty | 流程完整；豁免判定一律不成立；被告挑選不受影響 |
| V33 | 接受後找不到可進場的地圖 | 1 天後任務失敗，不卡在進行中 |
| V34 | 還原倒數 | 任務頁顯示「檔案還原：剩餘時間」，提示顯示完成日期 |
| V35 | 第 5 天 | 出現「艦隊引渡要求」選擇信件（交出／拒絕／稍後）；任務頁顯示「引渡答覆期限」倒數；信件不能右鍵關閉 |
| V36 | 選擇拒絕 | 與 V8／V9 相同（授予、敵對、追殺、倒數警報） |
| V37 | 2 天內不答覆 | 訊息「艦隊視之為拒絕」，之後同 V36；選擇信件消失 |
| V38 | 選擇交出，無皇權 DLC | 殖民者得到放逐心情懲罰；脫序者從地圖消失；艦隊好感度 +25；引渡完成信件；任務成功；沒有研究、沒有敵對、沒有追殺 |
| V39 | 選擇交出，有皇權 DLC | 同 V38，另出現選人信件；選定者得到 10 點榮譽，任務隨即成功結束 |
| V40 | 脫序者在商隊中時選擇交出 | 物品移交給商隊其他人，脫序者離開商隊，其餘同 V38 |
| V41 | 交出時已有封存級技術或永久敵對 | 好感度依既有守衛限制（盟友上限或擋下），榮譽照給 |
| V42 | 引渡要求期間脫序者死亡 | 任務失敗，選擇信件消失 |

## 6. 決策紀錄

| # | 定案 | 內容 |
|---|---|---|
| D1 | A | 待授予：資料立即探明，前置齊全後自動解譯（3.2） |
| D2 | A | 優先前置已完成者，不足再補（3.3） |
| D3 | A | 授予前 `MarkSanctioned`，不結算通用制裁、不立即襲擊（3.2）。附註：SAGE 真空期同樣適用於脫序者追殺（3.6） |
| D4 | A | 脫序者在殖民地期間鎖定敵對（3.4）。附註「軍事法庭延到第二版」已依 2.3 的更正改為本版實作（3.7） |
| D5 | A | `minDaysPassed=14` |
| D6 | B | 隱匿級永久敵對與脫序者同時存在時分開審判：先審脫序者（一般刑度），結案只結束脫序者追殺、關係直接回到敵對；之後另發隱匿級傳票，被告為官階最高者（3.7） |
| D7 | A | 脫序要由玩家自己實踐：第 5 天改為艦隊引渡要求，選擇交給玩家；皇權 DLC 下交出有官階獎勵（艦隊榮譽）（3.11） |

## 7. 實作時的注意事項

1. `QuestPart_DeviantHandover` 與 `QuestPart_DeviantWatch` 都在 quest tick 內執行，亂數一律用 `Verse.Rand`。
2. `Patch_ResearchManager_FinishProject` 的待授予檢查必須排在制裁與任務線之後，並包在獨立的 try/catch 裡（比照現有兩段）。
3. `HostilityLocked` 會在外交結算中被頻繁呼叫；`HarboredDeviant` 只查一個 Def 的 instance，不掃描地圖。
4. 撤銷傳票一律經 `CourtMartialOfferUtility.Refresh()`，以 `quest.End(InvalidPreAcceptance, sendLetter: false)` 結束，比照原 `WithdrawOrdinaryCourtMartialOffers`。
5. 脫序者審判在追殺令下結案時，`QuestPart_CourtTruce` 設回敵對必須包在 `WithGuardSuppressed` 裡（與現有 `SetGoodwillAndRelation` 相同），且排在 `ResolveBoundHuntdowns()` 之後。隱匿級傳票不能在同一個訊號處理中補發：`QuestPart_CourtTruce` 比 `QuestPart_QuestEnd` 先加入，收到 `sigReturned` 時任務仍是 Ongoing，且 `CourtMartialOngoing` 有 60 tick 快取，`EnsureCourtMartialOffered` 會直接返回。做法：只登記「待整理」，由 `GameComponent_DeviantLegacy` 在下一個 250 tick 檢查時先 `InvalidateCourtCache()` 再 `Refresh()`。追殺令每波的 `questsOnWave` 仍是後備。
5. 交付延後敵對期間，任務維持 Ongoing；任務頁說明欄顯示「敵對將在審判結束後生效」（`DMS_DeviantRefuge_DeferredInfo`）。
6. `HuntdownWorker_Deviant` 是每個 Def 一個實例；`BoundPawnLost` 記下的原因必須在 `OnStopped` 用完後清除。

## 8. 文本

已寫好（English／繁體中文／簡體中文）：

- `1.7/NewContent/Languages/*/Keyed/DMS_DeviantRefuge.xml`：抵達、還原倒數、交付（含待授予、延後敵對）、解譯、失敗、追殺結束、關係鎖、放棄選項停用、SAGE 脫序者分支。
- `1.7/NewContent/Languages/*/DefInjected/`：`QuestScriptDef/DMS_DeviantRefuge.xml`（任務名稱與描述）、`IncidentDef/DMS_DeviantRefuge.xml`、`Fortified.HuntdownDef/DMS_DeviantRefuge.xml`（label 與預警信件、警報）。原先放在 Keyed 註解區塊與 `DMS_DeviantHunt_Letter*／Alert*` 的草稿已搬入此處。
- `1.7/Languages/*/DefInjected/HistoryEventDef/DMS_HistoryEvents.xml`：`DMS_HarboredDeviant.label`。
- `1.7/Defs/Quests/CourtMartial.xml` 與三語 `DefInjected/QuestScriptDef/CourtMartial.xml`：`questDescription(defendantRanked==Deviant)` 三則。
- 同上兩處：`killOrderNote(killOrder==True)`／`killOrderNote(killOrder==False)`，接在三則脫序者描述的結尾。
- 同檔的 `DMS_CourtMartialLetters` 與三語 `DefInjected/RulePackDef/CourtMartial.xml`：`verdictLetterDeviantText`、`acquitDeviantLetterText`、`acquitDeviantKillOrderLetterText`、`returnDeviantKillOrderLetterLabel`、`returnDeviantKillOrderLetterText`。
- 這些規則只在 `QuestNode_Root_CourtMartial` 判定為脫序者審判時被選中。

slate 變數：`[deviant_nameFull]`、`[deviant_nameDef]`、`[deviant_pronoun]`、`[deviant_objective]`、`[deviant_possessive]`、`[fleetName]`、`[map_definite]`。Keyed 參數：`{DEVIANT_*}`、`{FLEET}`、`{DAYS}`、`{GRANTED}`、`{PROJECT}`、`{PREREQS}`、`{NODE_definite}`、`{DURATION}`。

## 9. 實作紀錄（2026-10-16）

狀態：`dotnet build -c Release` 0 錯誤（新增 4 個警告，皆為新 DefOf 欄位的 CS0649，與既有 DefOf 欄位相同）；`1.7/Assemblies/DMS.dll` 已重建。26 個新增或修改的 XML 全部可解析，三語 Keyed 鍵與 DefInjected 鍵一致。**遊戲內尚未驗證**，第 5 節 V1～V33 全部待跑。

與第 3 節設計不同之處：

| 項目 | 計畫 | 實作 | 理由 |
|---|---|---|---|
| 脫序者追殺讓位與休戰 | `CanFireWave` 回 false | `CanTrackMap`／`StillHunted` 在 `Dormant`（追殺令進行中或審判中）時回 false，計時直接移除，之後由 `retargetPlayerHome` 重新排程 | `CanFireWave` 回 false 會讓地圖重新排程並再發一次預警信件，玩家每 5~8 天收到一封不會兌現的預警 |
| 接受後未加入的逾時 | 另一個 `QuestPart_Delay`（`inSignalDisable = sigJoined`） | 併入 `QuestPart_DeviantWatch` 輪詢 | 加入訊號在接受訊號的處理中巢狀送出，Delay 的停用訊號可能早於它被啟用，順序不可靠 |
| 進場地圖 | 任一玩家據點 | 排除太空層（`map.Tile.LayerDef.isSpace`），比照 `QuestGen_Get.GetMap` 的預設 | 軌道地圖沒有可走進來的地圖邊緣 |
| `ModExtension_FleetTracking` | 獨立檔案 | 與 `FleetTrackingUtility` 同檔 | 空類別，放在一起較好找 |
| 開發者動作名稱 | Keyed `DMS_DeviantRefuge_Debug*` | `DebugAction` 屬性上的中英文字串（比照既有 Occultech 動作），Keyed 已移除 | 屬性參數必須是編譯期常數，無法翻譯 |
| `DMS_DeviantRefuge_PendingTip` | 研究頁提示 | 未使用，已移除 | 需要額外 patch 研究頁，本版不做 |
| 待授予的解譯檢查 | postfix 末端走訪一次 | 重複掃描到沒有進展為止，並以重入旗標擋下巢狀呼叫 | 完成一項可能讓另一項的前置也齊了 |

新增或修改的檔案：

- C# 新增：`Quests/Deviant/`（`QuestNode_Root_DeviantRefuge`、`QuestPart_DeviantWatch`、`QuestPart_DeviantHandover`、`DeviantTechUtility`、`DeviantHuntUtility`、`GameComponent_DeviantLegacy`、`HuntdownWorker_Deviant`）、`Quests/CourtMartial/CourtMartialOfferUtility.cs`、`Occultech/FleetTrackingUtility.cs`。
- C# 修改：`DMS_DefOf`、`OccultechSanctionUtility`、`Patch_OccultechSanction`、`GameComponent_OccultechSanction`、`DebugActions_Occultech`、`CompHuntBreaker`、`QuestNode_Root_DMS_FleetNetworkSite`、`QuestNode_Root_CourtMartial`、`QuestPart_CourtTruce`、`QuestPart_CourtVerdict`。
- XML：`1.7/NewContent/Defs/Quests/DMS_DeviantRefuge.xml`、`1.7/NewContent/Patches/DMS_DeviantRefuge_Storyteller.xml`、`1.7/Defs/Misc/DMS_Huntdowns.xml`（標記）、`1.7/Defs/Misc/DMS_HistoryEvents.xml`、`1.7/Defs/Quests/CourtMartial.xml`，以及第 8 節列出的語系檔。

### 9.1 第一輪遊戲內測試（2026-10-16）

回報：交付後「沒有出現 Huntdown 與永久敵對」。查 `Player.log` 與 Autosave-4（tick 60000）：任務已在 tick 57472 成功結束，艦隊 goodwill −100／Hostile，`DMS_DeviantHunt` 已啟動並綁定脫序者、追蹤 Map_0（warningTick 253047、raidTick 437254）。功能正常，問題在於**看不見**：

- FFF 的倒數警報只在預警之後出現，原設定預警 3~4 天，交付後頭幾天畫面上沒有任何追殺跡象。改為 `warningDelayRange 0~0`＋`sendWarningLetter false`：警報從交付起就倒數，預警信件與其翻譯（`letterLabel`／`letterText`）移除。
- Occultech 狀態的 `permanentHostile` 只屬於隱匿級追殺令，脫序者的關係鎖（D4-A）不會讓它變 true。狀態輸出另加一行 `harbored deviant / deviantHunt / hostilityLocked`。

已存在的追殺計時不受 Def 修改影響（舊存檔要等到原本的 warningTick 才出現警報）；重測請重新跑一次任務。

回報：SAGE 節點任務無法正常觸發（`Grammar unresolvable. Root 'questDescription'`，`fleetName → UNRESOLVABLE`）。**既有缺陷，與脫序者實作無關**：`QuestNode_Root_DMS_FleetNetworkSite` 只把 `fleetName` 加成語法常數（`AddQuestDescriptionConstants`），常數只能給規則條件判斷，描述裡的 `[fleetName]` 沒有對應規則，三語（含英文退回）都會失敗。過去只有隱匿級追殺令期間才會派發，脫序者追殺讓它更容易出現。修正：另以 `AddQuestNameRules`／`AddQuestDescriptionRules` 提供 `Rule_String("fleetName", …)`。已掃過其他 DMS 任務根節點：`steleMass` 已是規則，`installation`、`retry`、`defendantRanked`、`killOrder` 只用於條件，沒有同類問題。

### 9.2 引渡要求（D7-A，2026-10-16）

新增 `QuestPart_DeviantExtraditionDemand`（含 `ChoiceLetter_DeviantExtradition`）與 `QuestPart_DeviantExtradition`；`QuestNode_Root_DeviantRefuge` 改接線；`QuestPart_DeviantWatch` 改在做出選擇時停用；失敗信件抽成 `DeviantHuntUtility.SendFailedLetter`。XML：`LetterDef DMS_DeviantExtradition`、`HistoryEventDef DMS_ExtraditedDeviant`、根節點新增 `extradition*` 參數。開發者動作「立即交付檔案」改名為「立即發出引渡要求」。

附帶修正：任務頁的還原倒數原本不顯示剩餘時間。原版 `QuestPart_Delay` 以剩餘時間代入 `expiryInfoPart` 的 `{0}`、以日期代入提示的 `{0}`，但 `DMS_DeviantRefuge_HandoverInfo` 沒有 `{0}`，提示則在生成時就被 pawn 參數格式化掉了。現在兩者都改用 `{0}`，提示不再帶 pawn 參數。

文本：任務描述的警告句改為「得知……並要求引渡。若我們拒絕，艦隊便會展開敵對行動……」（三語）；最後一句「只有在你準備好與艦隊為敵時才接受」保留原樣。

建議的第一輪遊戲內驗證順序：V6 → V34 → V35 → V36（或 V37）→ V8／V9 → V10 → V15 → V17 → V23／V24／V24a → V7 → V29；另開一局跑 V38／V39。開發者動作「Deviant: 立即發出任務」與「Deviant: 立即發出引渡要求」可跳過第 15 天與 5 天的等待。
