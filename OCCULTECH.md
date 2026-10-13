# 封存科技 / Occultech

> Dead Man's Switch 2.0 的獨立研究系統。截至 `DMS2-Dev` 分支（2026-09-23）的實裝狀態整理。
> 底層機制由 **Fortified Feature Framework** 的 `Fortified/Research/` 提供，本模組只負責內容 Def。

---

## 目錄

- [概念](#概念)
- [知識類別與分頁](#知識類別與分頁)
- [探明機制（Discovery）](#探明機制discovery)
- [制裁機制（Sanctions）](#制裁機制sanctions)
- [研究專案](#研究專案)
- [知識取得管道](#知識取得管道)
- [任務線：石碑與密鑰](#任務線石碑與密鑰)
- [意識形態：封存科技議題](#意識形態封存科技議題)
- [檔案索引](#檔案索引)
- [框架 API 對照](#框架-api-對照)
- [待辦與已知缺口](#待辦與已知缺口)

---

## 概念

封存科技（Occultech / Expeditional Knowledge）是殖民艦隊握有、但**不對外流通**的技術。設定上，這些技術因為危險性、或對地方世界可能造成不可逆的深遠影響而被列管封存；每個專案的描述結尾都是一段「經 ███ 及 ██ 之審批，列為 I 級封存技術」的公文口吻。

玩法上它是一條**獨立於常規研究樹之外**的知識線：

- 不消耗研究桌的研究點，而是消耗**知識點（knowledge）**，類似 Anomaly 的暗黑知識分頁。
- 專案預設**隱藏**，必須先「探明」才看得到、才能推進。
- 知識點的來源與研究員無關，而是靠**解密資料匣**與**石碑萃取**。
- 每完成一項都會被殖民艦隊記在帳上：依管制等級觸發不同程度的**制裁**。

---

## 知識類別與分頁

`ResearchTabDef: DMS_ExpeditionalKnowledge`（標籤：Occultech／封存科技）
掛 `Fortified.ModExtension_UniqueResearchTab`，以三個 `KnowledgeCategoryDef` 作為直欄，由左至右排列：

| defName | EN | 繁中 | 顏色 | 溢流目標 | 制裁 |
|---|---|---|---|---|---|
| `DMS_Restricted` | restricted | 管制 | 黃褐 `(0.6, 0.6, 0.4)` | 無 | 每項 −80 好感度 |
| `DMS_Sealed` | sealed | 封存 | 橘 `(1, 0.5, 0.1)` | `DMS_Restricted` | 立即敵對＋襲擊，盟友上鎖 |
| `DMS_Occulted` | occulted | 禁忌 | 紅 `(1, 0, 0)` | `DMS_Sealed` | 永久敵對＋追殺＋連坐 |

> 三個類別的 `modExtensions` 各掛一份 `DMS.ModExtension_OccultechSanction`，制裁參數全部寫在那裡。
> 沒掛這個擴充的知識類別（例如其他模組的分頁）完全不受制裁機制影響。

**溢流規則**：知識點注入某一類別後，若該類別已無可推進的專案，會依分頁的類別順序往後溢流（Restricted → Sealed → Occulted）。溢流順序由 `ModExtension_UniqueResearchTab.categories` 的排列決定，`KnowledgeCategoryDef.overflowCategory` 為原版欄位。

> 無 Anomaly DLC 時，原版的 `ResearchManager.GetProgress` 對知識型專案永遠回傳 0。框架以 `GameComponent_KnowledgeStore` 自行保存進度，並於無 Anomaly 時用 `Patch_ResearchManager_GetProgress` 接回。啟用 Anomaly 時該組件完全不介入。

---

## 探明機制（Discovery）

分頁掛了 `Fortified.ModExtension_ResearchDiscovery`：

```xml
<li Class="Fortified.ModExtension_ResearchDiscovery">
  <hideUntilDiscovered>true</hideUntilDiscovered>
  <discoveryLetter>true</discoveryLetter>
</li>
```

規則：

- **前置研究全部完成之前**，專案視為「未探明」，透過 patch 接回原生 `ResearchProjectDef.IsHidden`，因此沿用原版的隱藏表現：顯示為「（未知研究）」、不可點選、不可開始、快速搜尋排除、書籍不給進度。
- 可在**單一專案**上掛同一個擴充覆寫分頁設定（例如把入口專案設成 `hideUntilDiscovered=false` 讓它永遠可見）。目前 DMS 未使用專案級覆寫。
- **強制探明**：解封物品／事件／任務獎勵可呼叫 `ResearchDiscoveryUtility.ForceDiscover`，永久蓋過前置推導，狀態存進 `GameComponent_ResearchDiscovery.forced`。
- 新專案被探明時發信通知（`announced` 集合去重）。舊存檔首次載入會**靜默種子化**當下已探明的專案，避免開局灌一整串信件。
- 全部失敗路徑 **fail-open**（判定為已探明）。隱藏失效只是多顯示東西；誤判成隱藏會讓整棵研究樹永久不可研究。

---

## 制裁機制（Sanctions）

殖民艦隊會追蹤每一次解封。制裁在**研究完成的那一刻**結算，每個專案終生只結算一次（紀錄存在 `GameComponent_OccultechSanction.sanctionedProjects`，讀檔與重複 `FinishProject` 都不會重扣）。

參數全部寫在各 `KnowledgeCategoryDef` 的 `DMS.ModExtension_OccultechSanction` 裡，見 `1.6/Defs/Research/DMS_OccultechResearchTab.xml`。

### 總覽

| | 限制級 Restricted | 封存級 Sealed | 隱匿級 Occulted |
|---|---|---|---|
| **封存理由** | 環境與人倫因素，或對使用者的永久性影響 | 擴散即構成跨星系軍備競賽或非人道武器升級 | 知道它存在本身就是危害 |
| **完成效果** | 每項 **−80** 艦隊好感度 | 每項**立即敵對**＋**立即襲擊** | **永久敵對**＋**追殺**＋**連坐三個艦隊友好派系** |
| **關係上限** | 無 | 未放棄前最高**中立** | 永久敵對，解除後最高**中立** |
| **豁免** | 殖民地有**准尉**（`DMS_WarrantOfficer`, seniority 200）以上 | 殖民地有**准將**（`DMS_Brigadier`, seniority 1000）以上 | **無** |
| **解除途徑** | 無 | 通訊台向艦隊**申報放棄** | **軍事法庭**：官階最高者服刑一年 |

### 豁免：臨時解禁令

`OccultechSanctionUtility.IsExempt` 掃描 `PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists`（含商隊與運輸途中），找持有艦隊官階且 `seniority` 達標的自由殖民者。達標即**完全免除**該次制裁，只發一封「臨時解禁令」通知信。

- 豁免門檻以 `exemptSeniority`（整數）表示，**不是**直接引用 `RoyalTitleDef`，因為軍銜 Def 位於 `1.6/Royalty/` 之下，無 Royalty DLC 時不存在，直接跨引用會炸掉 Def 解析。
- 無 Royalty DLC 時沒有軍銜，等於永遠無法豁免。
- 豁免是**逐次判定**的：軍官死了或離隊之後研究下一項，就沒有解禁令可簽了。

### 限制級：好感度扣減

`fleet.TryAffectGoodwillWith(player, -80, reason: DMS_OccultechResearched)`。好感度跌破原版的 −75 門檻時仍會依常規轉為敵對，這是原版行為，不是額外制裁。

### 封存級：敵對、襲擊與盟友上鎖

1. 好感度打到 −100 並直接設為敵對。
2. 立刻執行一次 `IncidentDefOf.RaidEnemy`（`points = max(400, 當前威脅點數 × 1.25)`，`forced = true`）。沒有玩家主地圖時靜默略過。
3. **盟友上鎖**：只要還持有任何**已完成且未放棄**的封存級研究，與艦隊的關係就回不到盟友。

上鎖由三層實作，任一層失效還有下一層兜底：

| 層 | 位置 | 作用 |
|---|---|---|
| 好感度封鎖 | `Patch_Faction_CanChangeGoodwillFor` | 好感度已達 74 時擋下所有正向變動 |
| 關係校正 | `Patch_FactionRelation_CheckKindThresholds` | 原生剛判定為盟友時立刻壓回中立 |
| 週期兜底 | `GameComponent.GameComponentTick` → `EnforceFleetRelation()` | 每 2000 ticks 檢查一次，處理繞過前兩層的來源 |

### 放棄封存級技術

通訊台呼叫殖民艦隊 → 對話選項「**放棄封存級技術**」（`Patch_FactionDialogMaker_FactionDialogFor`）。確認後 `OccultechSanctionUtility.RenounceAll()`：

- 所有已完成的封存級專案進度歸零（同時清 `ResearchManager.progress`、`anomalyKnowledge` 與 FFF 的 `GameComponent_KnowledgeStore`，三個容器依 Anomaly 是否啟用各自生效）。
- 解鎖的建築、配方、服裝重新上鎖（`researchPrerequisites` 是即時查詢的，不需額外處理）。
- 清除制裁紀錄：**重新研究會再觸發一次完整制裁**，這是刻意的：重新解封就是重新犯一次。
- 刻意**不**取消「已探明」狀態：玩家已經知道這項技術存在，抹除認知反而更奇怪。

> 限制：原生的 `researchMods` 只有 `Apply` 沒有 `Unapply`，被放棄的專案若掛了 `researchMods`，效果要到重新載入存檔才消失。目前四個封存級專案都沒有用到。

### 隱匿級：永久敵對、追殺與連坐

1. 艦隊打到 −100 並設為敵對，`GameComponent_OccultechSanction.permanentHostile = true`。
2. **所有**對艦隊的正向好感度變動被擋下（不只是盟友門檻）。
3. **追殺**：每 `4~7` 遊戲日降一波艦隊襲擊（`points = max(600, 當前威脅點數 × 1.25)`）。
4. **連坐**：隨機三個派系轉為敵對。優先選與艦隊實際為盟友者，不足時從其他非隱藏、目前尚未與玩家敵對的派系補滿。這三者**不鎖好感**，玩家可以自行修復關係。

### 暫停追殺：摧毀 SAGE

殖民艦隊的追殺靠的是 SAGE 網路（`DMS_SageCore`，賢者）。打爛一台 SAGE 就能讓追殺暫停，但**不會**解除永久敵對：好感度封鎖、連坐全部照舊，軍事法庭仍是唯一出路。

| 狀態 | 摧毀 SAGE 的效果 |
|---|---|
| 永久敵對、未暫停 | 追殺暫停 `1~2` 年（60~120 天），發信 |
| 永久敵對、已暫停 | 每座再疊加 **15 天**，接在目前的結束時間之後 |
| 未處於永久敵對 | 無效果，只發中性訊息 |

- 只有打爛（`KillFinalize`）才算；SAGE 不可拆除。站點伺服主機與設施伺服核心被摧毀**不影響**追殺。
- 暫停期間完成新的隱匿級研究：制裁照常結算，但暫停不受影響，追殺要等暫停結束才重新排程。
- 暫停結束：發「追緝網路恢復」信件，依 `huntIntervalDays` 重新排程追殺，並補一次軍事法庭傳票（暫停期間沒有追殺，也就沒有人補發）。
- 軍事法庭服刑期滿（`EndPermanentHostility`）時一併清除暫停。
- **SAGE 節點任務**（`DMS_FleetNetworkSite`）：只在追殺進行中且未暫停時出現，同時最多一份；每次追殺觸發時也有 35% 機率主動派發（`OccultechSanctionUtility.TryOfferNetworkSite`）。站點守軍為先鋒殘餘。

實作：`_Source/DMS/FacilityServer/CompHuntBreaker.cs`、`GameComponent_OccultechSanction.SuspendHunt` / `huntSuspendedUntilTick`、`OccultechSanctionUtility.TrySuspendHunt`。完整設計見 `FACILITY_SERVER_PLAN.md`。

### 解除永久敵對：軍事法庭

沿用既有的 `DMS_CourtMartial`（`1.6/Defs/Quests/CourtMartial.xml` + `_Source/DMS/Quests/CourtMartial/`），但在永久敵對下改寫三項參數：

| | 一般審判 | 隱匿級審判 |
|---|---|---|
| 刑期 | 依官階 seniority 反比（5~60 天） | 固定 **60 天**（`occultedDetentionDays`） |
| 無罪釋放 | 機率由官階與社交決定 | **0%** |
| 結案關係 | 擲骰決定中立／盟友 | **一律中立** |

流程與一般審判相同：接受 → 休戰 → 穿梭機接走被告 → 調查 → 宣判（降一級）→ 服刑 → 空投歸還。服刑期滿歸還時 `QuestPart_CourtTruce` 呼叫 `EndPermanentHostility()`，追殺停止、關係回到中立。**連坐派系的敵對不會一併恢復。**

**被告的挑選**（`QuestNode_Root_CourtMartial.FindDefendant`）：

1. 優先取持有艦隊官階且 `seniority` 最高者。
2. 一個持銜者都沒有時，改取**市場價值最高**的殖民者：艦隊在名冊上找不到人，就直接向殖民地索討他們最看重的那一個。這個 fallback 是永久敵對的保險：軍事法庭是唯一出路，若因為當下沒有持銜者而生不出任務，存檔就會永遠卡在敵對。
3. 兩條路徑都排除任務借住者（quest lodger）：他們屬於別的任務，借出去會讓兩邊的 QuestPart 互相打架。

無銜被告的 `seniority` 以 0 代入刑期曲線，也就是**最長刑期**：沒有軍銜就沒有從輕的餘地。

**文本分流**：既有的任務敘述與判決信件通篇圍繞「我們官階最高的軍官」與「至少降階一級」寫成，對無銜被告整段語意都對不上，因此兩邊各備一組：

| | 位置 | 分流方式 |
|---|---|---|
| 任務敘述 | `questDescriptionRules`（三個隨機變體 × 2） | grammar 常數 `questDescription(defendantRanked==True/False)`，由 `QuestGen.AddQuestDescriptionConstants` 注入 |
| 判決信件 | `DMS_CourtMartialLetters` | `verdictLetterText` / `verdictLetterNoDemotionText`，由 `QuestPart_CourtVerdict` 選用 |

兩者的判定條件完全一致（`title != null && title.seniority > 0`），所以敘述裡承諾的降階一定會發生。無銜版本的文案改成「艦隊向殖民地本身索討最看重的那個人」，正好對上 `FindDefendant` 的市場價值 fallback。

**不需要 Royalty DLC**：本任務原本位於 `1.6/Royalty/`，無 DLC 時連 Def 都不載入，等於隱匿級的永久敵對沒有出路。現已搬到 `1.6/Defs/Quests/`，翻譯同步搬到 `1.6/Languages/<lang>/DefInjected/{QuestScriptDef,RulePackDef}/`，`TestRunInt` 的 `ModsConfig.RoyaltyActive` 判斷也移除了。任務引用的其他 Def（`DMS_Army`、`DMS_Ship_TransportShuttle`、`DMS_Tale_*`、`DMS_MemberCourtMartialed`、`DMS_QuestSuccess_Ceremony`）本來就全部在核心資料夾，搬移不會產生懸空的跨引用。

> 順帶修掉一個潛在錯誤：`DMS_DefOf.DMS_CourtMartial` 是上一步為了永久敵對判定而新增的，若任務 Def 仍留在 Royalty 資料夾，無 DLC 的存檔會在 DefOf 解析時報錯。

> 副作用：`TestRunInt` 不再要求「有持銜殖民者」，因此一般的軍事法庭任務在與艦隊敵對時會比以前更容易出現。若只想讓 fallback 在隱匿級審判時生效，把 `FindDefendant` 的第二段包在 `GameComponent_OccultechSanction.CompSafe?.PermanentHostile` 條件裡即可。

兩個必要的細節：

- **審判期間暫停校正**：`EnforceFleetRelation()` 與兩個 patch 在有進行中的 `DMS_CourtMartial` 時全部讓路，否則接受任務的下一秒就會被自己的永久敵對打回敵對。判定用「有沒有進行中的任務」而非存檔旗標，任務失敗／被刪／讀舊檔都會自動歸位。
- **主動發傳票**：軍事法庭是唯一出路，不能只靠說書人隨機抽中。每次追殺觸發時 `EnsureCourtMartialOffered()` 會確認玩家手上有一份可接的傳票，沒有就補發一份。
- 審判失敗（被告死亡／休戰破裂／逾時未登機）→ 恢復敵對，永久敵對狀態**保留**，下次追殺會再補一張傳票。

### 除錯

`_Source/DMS/Occultech/DebugActions_Occultech.cs` 提供七個 DebugAction（分類 `DMS`）：重放任一專案的制裁、解除永久敵對、放棄封存級技術、輸出目前狀態、暫停追殺一年、立即結束追殺暫停、派發 SAGE 節點任務。

---

## 研究專案

`1.6/Defs/Research/DMS_OccultechProjects.xml`

### 實裝專案

蛙人是 Sealed 封存級，其餘六個都是 Restricted 管制級。Occulted 禁閉級目前沒有任何專案。

#### 1. `DMS_Occultech_Frogman`：directed chassis gestation／定向機體孕育

- 知識成本 1000，類別 Sealed，前置 `DMS_Artifuscle`（常規樹：Artificial Muscle，胚胎在人造肌肉艙製作），座標 (3, 3)
- **解鎖**：`DMS_Make_FrogmanEmbryo`（人造肌肉艙製造胚胎，智識 8）→ 成長艙或代孕者孕育 → 生物機兵幼體 → 孕育艙 5 天 → XM132 Frogman
- 設定：AMO（人造軍用生物）在地球紀元的重大事故後被永久禁止，理由是自體發育的軍用生物對當地社會與生產能力危害極大；禁令的制裁由艦隊親自執行，不假手當地政權
- 相關檔：`1.6/Biotech/Defs/DMS_Frogman_Gestation.xml`

#### 2. `DMS_Occultech_BionicSuitHeavy`：enclosed assault frame／全閉式突擊骨架

- 知識成本 750，前置 `DMS_Artifuscle`（常規樹：Artificial Muscle），座標 (1, 3)
- **解鎖**：`DMS_Apparel_BionicSuitHeavy`（人造肌肉艙製作，工藝 10，`UnfinishedTechArmor`）
- 掛 `DMS.CompProperties_SurgicalApparel`：全閉合才能出力，穿上後無法以常規手段脫除，必須動手術（`DMS_RemoveBionicSuitHeavy`），且不能從活人身上剝除；穿著前會跳確認視窗
- 設定：因無法自行解除，戰爭法上被歸類為「對其穿戴者本身的拘束器具」
- 相關檔：`1.6/Defs/Things_Apparel/Military.xml`

#### 3. `DMS_Occultech_GestaltEngine`：cluster command／集群司令

- 知識成本 750，前置 `DMS_Mechlink`（常規樹：Cluster Transceiver），座標 (1, 4)
- **解鎖**：`DMS_GestaltEngine`（中央指揮核心，5000W，無需機械師即可全圖指揮機兵、自帶頻寬與指揮群）與其擴充設施 `DMS_Gestalt_CommandArray`、`DMS_Gestalt_Transmitter`
- 設施均為 `tradeability: None`（可拆卸但不進貿易池）
- 相關檔：`1.6/NewContent/Defs/Things_Building/DMS_Buildings_Gestalt.xml`

#### 4. `DMS_Occultech_Biosynthesis`：synthetic biology／合成生物學

- 知識成本 750，前置 `DMS_Artifuscle`，座標 (1, 5)
- **解鎖**三個配方（`1.6/Defs/Things_Item/DMS_Recipe.xml`）：
  - `DMS_Make_Neutroamine`：由肉類與化石油合成純合成藥劑 x5
  - `DMS_Make_ArtifuscleFromProtein`：以人工蛋白＋鎢鋼培養人造肌肉 x5（免黃金與純合成藥劑）
  - 第三個配方（`DMS_Recipe.xml:972` 附近）同屬本專案
- 設定：封存理由不是武器，而是人工合成技術大規模擴散後，相關產業長期下來會出現極其嚴重的結構性失業

#### 5. `DMS_Occultech_Subsonic`：infrasonics／次聲波基礎

- 知識成本 750，前置 `MicroelectronicsBasics`（原版：微電子基礎），座標 (1, 6)
- 脈衝邏輯全部走 `DMS.SubsonicUtility.DoPulse`：作用於所有**非機械體且聽覺 > 0** 的單位（`SubsonicUtility.HearingFactor`，全聾／無耳者免疫）、預設穿牆；每次脈衝疊加 `DMS_SubsonicTrauma`，嚴重度 = `severity × 聽覺 ÷ max(1, 體型)`（約兩小時消退，最高段疼痛足以休克），並以 `panicChance × 聽覺 × (1 − FFF_FearResistance)` 觸發 PanicFlee。聽覺可超過 100%（仿生耳），效果隨之放大；創傷本身會降低聽覺，連續疊加會自然遞減
- **聽覺器官傷害**：每次脈衝另對每個 `HearingSource` 部位（耳朵）各造成一次 `DMS_InfrasoundRupture` 傷害（armorCategory `Blunt`、`DamageWorker_AddInjury`），頭盔與天生鈍器護甲先抵擋；傷口可能留疤成永久聽力損失。每耳傷害：模塊 3、次聲波塔 2、手雷 5（`pulse.earDamage` / `earDamageDef` / `earArmorPenetration`）
- **解鎖**：
  - `DMS_SubsonicModule`（次聲波改裝模塊，機械列印機製作）→ 掛載後取得技能 `DMS_SubsonicBurst`：以自身為中心 7.9 格、只打敵對、冷卻 2500 ticks（`CompAbilityEffect_SubsonicPulse`）
  - `DMS_Building_SubsonicEmitter`（次聲波塔，1×1、繪製 2×2、250W）：範圍 9.9 格，每 900~1500 ticks 間歇脈衝，範圍內沒有敵人時不發射（`CompSubsonicEmitter`）
  - `DMS_Weapon_GrenadeSubsonic`（次聲波手雷）：半徑 3.9、**不分敵我**、無視牆壁，無爆炸傷害（`Projectile_SubsonicGrenade` + `ModExtension_SubsonicPulse`）
  - `DMS_Building_BlastScanner`（爆破探勘裝置，1×1、繪製 2×2）：裝填一發 `Shell_HighExplosive`（`CompRefuelable`）→ 點燃引信 240 ticks → 探明 2~3 處深層礦脈，80% 機率在裝置 6~16 格內生成蟲巢隧道（威脅點數 × 0.6，每 220 點一巢，1~4 巢），冷卻 2 天；無基岩的生態域不可用（`CompBlastScanner`）
- 相關檔：`1.6/NewContent/Defs/Things_Item/DMS_Item_Subsonic.xml`、`1.6/NewContent/Defs/Things_Building/DMS_Buildings_Subsonic.xml`、`_Source/DMS/Subsonic/`
- 模塊與手雷目前暫用 `ComponentCCC` 與原版 EMP 手雷貼圖（XML 內有 TODO）

#### 6. `DMS_Occultech_RemnantPatterns`：garrison arsenal／駐屯兵器

- 知識成本 750，類別 Restricted，前置 `DMS_AdvancedControl`（常規樹：先進控制），座標 (1, 7)
- **解鎖**三個機械組裝平臺（`DMS_MechGestatorSmall`）配方，各 3 個培育週期：
  - `DMS_Make_BullHound`：鬥牛犬（霰彈型），鋼 250、鎢鋼 30、零件 8、先進零件 2
  - `DMS_Make_BullHound_Flamer`：鬥牛犬（噴火型），同上再加化合燃料 30
  - `DMS_Make_Lanius`：伯勞鳥（原本掛在常規樹的先進控制底下，現改掛本專案）
- 鬥牛犬的背掛武裝寫在種族上，培育出來就帶著，配方不消耗武器
- 復活：兩種鬥牛犬與伯勞鳥都歸在中型機兵復活（`DMS_ResurrectMediumMech`，鋼 50）；鬥牛犬雖是重型，仍刻意放在中型
- 設定：專為先鋒殖民行動設計、用於長期駐屯的自監管警備兵器，控制核心寫有備援指令，會持續執行最後一道常備命令直到機體損毀；戰爭法上與布雷區歸為同類。星球上的餘械就是這些型號失去創造者之後的樣子
- 相關檔：`1.7/Biotech/Defs/DMS_Recipes_MechGestator_{BullHound,Lanius}.xml`

#### 7. `DMS_Occultech_QubitDecryption`：qubit decryption／量子位元破譯

- 知識成本 750，類別 Restricted，前置 `MicroelectronicsBasics`（原版：微電子基礎），座標 (1, 2)
- **解鎖**：`DMS_OccultechExtractor`（解碼器）
- **派發石碑任務線**：專案掛 `DMS.ModExtension_ResearchQuest`（`quest = DMS_Stele`），研究完成時派發主線。研究完成前石碑任務線不會出現；一局只派發一次（任務管理器裡有過同 root 的任務就不再派發，含已結束者）
- 設定：直接量測全像儲存刻錄片晶格位點的量子態、接受塌縮來讀取資料，每次讀取都會永久破壞被讀取的區段；由脫序者流出
- 翻譯：`Languages/<lang>/DefInjected/ResearchProjectDef/DMS_OccultechProjects_QubitDecryption.xml`

---

## 知識取得管道

### A. 封存資料匣（Data Cache）：一次性解封物

`1.6/NewContent/Defs/Things_Item/DMS_Item_Occultech.xml`，共用抽象父 `DMS_BaseDataCache`（Spacer、`stackLimit 5`、不可燃、`CompProperties_Usable` 解密 1200 ticks、用畢 `CompProperties_UseEffectDestroySelf`）。

掛 `Fortified.CompProperties_UseEffect_DiscoverResearch`：使用後從 `researchProjects` 中隨機抽一個尚未解封的專案，**永久探明**並灌入進度；清單內專案全部研究完畢時則退回給智識經驗，不會白白吃掉物品。

| defName | 標籤 | 對應類別 | 市價 | 進度 | 退回經驗 | 解封清單 | 取得 |
|---|---|---|---|---|---|---|---|
| `DMS_DataCache_Restricted` | restricted technical data cache | 管制 | 300 | 150 | 1500 | QubitDecryption、BionicSuitHeavy、GestaltEngine、Biosynthesis、Subsonic、RemnantPatterns | `RewardStandardMidFreq`、物資箱（權重 3） |
| `DMS_DataCache_Sealed` | sealed data cache | 封存 | 700 | 250 | 2000 | Frogman | `RewardStandardCore`、物資箱（權重 1） |
| `DMS_DataCache_Occulted` | occulted data cache | 禁忌 | 1400 | 400 | 3000 | Frogman（暫代，見待辦） | `RewardStandardLowFreq`，**不進物資箱**，只走任務獎勵 |

> ⚠️ 資料匣依知識類別分配解封清單。新增 Occultech 專案時，記得把 defName 補進對應類別的 `researchProjects` 清單，否則永遠抽不到。清單不可為空，否則載入時報 ConfigError。

物資箱掉落設定見 `1.6/NewContent/Defs/Things_Building/DMS_ExplorationBuildings_Lootbox.xml`。

### B. 石碑 + 解碼器：持續萃取

| Def | 標籤 | 說明 |
|---|---|---|
| `DMS_OccultTechFont` | Stele／石碑 | 3×3，6000 HP，不可通行。掛 `Fortified.CompProperties_OccultechSource`，`totalReserve 4000`。本體不需電力、不主動 tick，只被動提供 `TryExtract`。抽乾後保留為可拆除空殼。放在 `FFF_DevCategory`，玩家不可建造，只能從石碑任務線取得。可微縮（500 kg）、`tradeability None`，剩餘儲量隨本體保存 |
| `DMS_OccultechExtractor` | Decoder／解碼器 | 1×2，可微縮搬運重裝。300W（待機 50W），掛 `Fortified.CompProperties_OccultechExtractor`，`knowledgePerDay 50`，起始類別 `DMS_Restricted`。需研究 `DMS_Occultech_QubitDecryption` |

運作條件三者皆需滿足：**有電** + **正面緊貼未耗盡的知識源** + **該分頁尚有可推進的專案**。任一不滿足即轉待機，不消耗源儲量、只吃待機電。知識透過 `KnowledgeUtility.AddKnowledge` 注入起始類別並依分頁順序向後溢流。**不需要研究員**。

設定上，解碼器是破壞性解碼：以高能雷射穿透石碑內部的全像儲存板、分析散射光譜讀出資料，是艦隊以外勢力存取石碑內容的唯一方法。據說這是艦隊叛離者外流的技術，使用它可能招來殖民艦隊的敵意。

相關檔：`1.6/Defs/Things_Building/DMS_Occultech.xml`

### C. 固態零點能（周邊資源）

- `DMS_SolitaryZPM`（固態零點能）：只能由 `DMS_ZeroPointGenerator`（零點能發生器核心）產出，無法以其他方式取得，預設在遊戲中隱藏。`stackLimit 25`，市價 200，`DeteriorationRate 10`
- 發生器以 `Shell_AntigrainWarhead` 為激發觸媒（`CompProperties_Refuelable`，容量 5、每日耗 0.25），5000W，透過 `Fortified.CompProperties_FueledSpawner` 每 120000~144000 ticks 產出 1 個
- 嚴格說它屬於「超凡技術造物」的能源側，與研究分頁無直接掛勾
- 相關檔：`1.6/NewContent/Defs/Things_Building/DMS_ExplorationBuildings_Occultech.xml`

---

## 任務線：石碑與密鑰

`1.7/NewContent/Defs/Quests/DMS_Stele.xml` + `_Source/DMS/Quests/Stele/`

流程：研究量子位元破譯 → 主線派發撞擊地點 → 帶回 4 枚密鑰 → 密鑰解讀後用掉、派發石碑封藏庫 → 佔領、拆卸石碑並帶回據點 → 主線成功。

**「帶回」的判定**（`SteleQuestUtility.IsHeldByPlayer`）：物品在玩家據點地圖上（地上、被搬運、在倉庫或微縮箱裡都算），或在玩家商隊裡。運輸艙飛行途中不算，落地後才算。

**「留在站點上」的判定**：原生移除地圖時不會對地上的物品呼叫 `Destroy()`，而是在 `Thing.Notify_MyMapRemoved` 把它們標成 `Destroyed`。因此站點的 `MapRemoved` 訊號到達時，物品還沒 `Destroyed` 就代表被帶走了（商隊、運輸艙、被搬運都算）。

### `DMS_Stele`（主線，`isRootSpecial` / `autoAccept`，挑戰評級 4）

`DMS.QuestNode_Root_Stele` + `QuestPart_SubquestGenerator_Stele`（`Main.cs`）：

- **觸發**：研究完成 `DMS_Occultech_QubitDecryption` 時由 `ResearchQuestUtility.Notify_ProjectFinished` 派發（接在 `Patch_ResearchManager_FinishProject` 的制裁之後），一局只派發一次。說書人不會抽到它（`isRootSpecial`）
- 同時只允許一條進行中（`TestRunInt` 檢查）
- `keysCount = 4`、`maxActiveSubquests = 3`、`vaultQuestDef = DMS_SteleVault`
- **階段一：收集密鑰**（`Stage.CollectingGlyphs`）
  - 首個撞擊地點 3~5 天後出現（MTB 1 天），之後每 6~12 天一個（MTB 3 天）
  - 只在「已帶回 + 進行中的撞擊地點 < 4」時派發，不會多發；子任務失敗（密鑰遺失）後自然補發
  - 任務視窗顯示「已取得密鑰：X / 4」（`ExpiryInfoPart`），密鑰的檢視面板也會顯示
  - 每 250 ticks 檢查一次；帶回的密鑰達 4 枚即進入階段二
- **階段二：石碑封藏庫**（`Stage.Vault`）
  - 銷毀剛好 4 枚已帶回的密鑰（設定：量子位元破譯讀取時量子態塌縮），派發 `DMS_SteleVault`
  - 封藏庫成功 → 主線成功（`Stage.Done`）
  - 封藏庫失敗 → 3~5 天後再派一座（描述走 `retry==True` 分支）；找不到地塊時 6 小時後重試
- 除錯視窗：顯示階段與密鑰數，可「強制下一個撞擊地點」「跳到封藏庫階段（保留密鑰）」「立即再派一座封藏庫」
- 舊存檔：`keysCount` / `vaultQuestDef` 缺值時從 Def 補上；舊的 `signalKeysFound` 欄位已移除

### `DMS_SteleKey_ImpactSite`（撞擊地點子任務，挑戰評級 2）

`DMS.QuestNode_Root_OccultechKey_Site`（`Sub_Site.cs`，共用基底 `QuestNode_Root_SteleSite`）+ `QuestPart_SteleKeySite`（`CommonParts.cs`）：

- 在距殖民地 20~40 格、`Flat`~`SmallHills` 的地塊生成 `DMS_Legacy` 站點；找不到時走 desperate query 放寬條件
- 站點 `DMS_SteleKey_ImpactSite`（`Sites/Stele_ImpactSite.xml`）：`GenStep_FFFGarrisonedStructure`，依威脅點數生成先鋒殘餘守軍、鋼渣與殘骸污垢，版面隨機旋轉
- 版面 `DMS_ImpactSite_A`（tag `DMS_SteleImpactSite`）：15×15，正中央是先鋒搭在墜落點上的 7×7 鎢鋼掩體（四面自動門），密鑰在掩體中央；外圍兩塊密鑰艙殘骸（原版 `ShipChunk`）
- 地圖生成時記下站點上的密鑰（版面沒放到就在中央附近補生成一枚）
- **成功**：密鑰被帶回（每 250 ticks 檢查），或離開站點時密鑰被帶走 → 主線發「已取得密鑰（X / 4）」訊息
- **失敗**：離開時密鑰留在站點上 → 「密鑰遺失」信件，主線之後補發
- 抵達時發「密鑰艙殘骸」信件（`questContentRules` 的 `letterLabelMapGenerated` / `letterTextMapGenerated`）

### `DMS_SteleVault`（石碑封藏庫，最終站點，挑戰評級 3）

`DMS.QuestNode_Root_SteleVault` + `QuestPart_SteleVault`（`Sub_Vault.cs`）：

- 距殖民地 8~20 格（石碑要運回來），守軍點數 ×1.25（`pointsFactor`）
- 站點 `DMS_SteleVault`：同樣是 `GenStep_FFFGarrisonedStructure` 守軍；允許重力艦降落，給有重力艦的玩家一條運回來的路
- 版面 `DMS_SteleVault_A`（tag `DMS_SteleVault`）：17×17，13×13 鎢鋼外牆圍出有屋頂的 11×11 封藏室，石碑面朝南站在正中央，南北各一扇門，南門外一排沙包
- 石碑以站點派系（先鋒殘餘）生成：玩家要先**佔領**才能**拆卸**（原生 `Designator_Uninstall` 只接受玩家或無派系的建築）
- **成功**：石碑（含微縮箱裡的）到了玩家據點或商隊，或離開站點時被帶走 → 「石碑到手」信件，主線成功
- **失敗**：離開時石碑留在站點上，或在站點上被摧毀 → 「石碑遺失」信件，主線 3~5 天後再派一座
- 描述以語法常數 `retry` 分流首次與重派；`[steleMass]` 由石碑的 Mass 填入
- 只記失敗事件（`DMS_QuestFailed_Occultech`）；成功事件交給同一刻結束的主線

### `DMS_OccultechKey`（`_Source/DMS/Quests/Stele/OccultechKey.cs`）

繼承 `ThingWithComps`。`PostPostMake` 時自動尋找進行中的 `DMS_Stele` 任務並向 `QuestPart_SubquestGenerator_Stele.TryAddKey` 註冊（只在收集階段接受）；`Destroy` 時反註冊。檢視面板顯示收集進度。不可交易、不可販售、`stackLimit 1`。

### 文本

- 任務文本：`NewContent/Languages/<lang>/DefInjected/QuestScriptDef/DMS_Stele.xml`
- 信件與訊息：`NewContent/Languages/<lang>/Keyed/DMS_Stele.xml`
- 站點：`Languages/<lang>/DefInjected/SitePartDef/Stele_ImpactSite.xml`（兩個站點都在這份）

---

## 意識形態：封存科技議題

`1.6/Ideology/Defs/DMS_Precepts_SealedTech.xml`（僅 Ideology DLC 啟用時載入）

`IssueDef: DMS_Issue_SealedTech`（sealed technology），消費 `DMS_QuestSuccess_Occultech` 事件：

| PreceptDef | 標籤 | 影響 | 效果 |
|---|---|---|---|
| `DMS_SealedTech_Venerated` | venerated（崇尚） | Medium | 心情 +8（15 天）+ 發展點數 8 |
| `DMS_SealedTech_Acceptable` | acceptable（可接受，**預設**） | Low | 發展點數 3 |
| `DMS_SealedTech_Forbidden` | forbidden（禁忌） | High | 心情 −8（15 天） |

> 設計註記：Forbidden 這一檔跟模組現有的封存科技敘述立場一致：那些 Def 的收尾本來就是審批公文，做一個站在審批者那一邊的信仰是自洽的。

`HistoryEventDef`（`1.6/Defs/Misc/DMS_HistoryEvents.xml`）：

- `DMS_QuestSuccess_Occultech`：sealed technology recovered
- `DMS_QuestFailed_Occultech`：sealed technology lost

兩者由 `DMS_Stele` 與 `DMS_SteleKey_ImpactSite` 的 `successHistoryEvent` / `failedOrExpiredHistoryEvent` 觸發；`DMS_SteleVault` 只觸發失敗事件。

---

## 檔案索引

### DMS 本體

| 路徑 | 內容 |
|---|---|
| `1.6/Defs/Research/DMS_OccultechResearchTab.xml` | 分頁、三個知識類別 |
| `1.6/Defs/Research/DMS_OccultechProjects.xml` | 全部 7 個專案 |
| `1.6/Defs/Things_Building/DMS_Occultech.xml` | 石碑、解碼器 |
| `1.6/NewContent/Defs/Things_Item/DMS_Item_Occultech.xml` | 固態零點能、密鑰、三階資料匣 |
| `1.6/NewContent/Defs/Things_Building/DMS_ExplorationBuildings_Occultech.xml` | 零點能發生器 |
| `1.6/NewContent/Defs/Things_Building/DMS_ExplorationBuildings_Lootbox.xml` | 資料匣的物資箱掉落 |
| `1.7/NewContent/Defs/Quests/DMS_Stele.xml` | 石碑主線、撞擊地點、石碑封藏庫三個任務 |
| `1.7/NewContent/Defs/Sites/Stele_ImpactSite.xml` | 撞擊地點與石碑封藏庫的站點、GenStep |
| `1.7/NewContent/Defs/GenStructures/SteleQuests/ImpactSite_A.xml` | 撞擊地點版面 |
| `1.7/NewContent/Defs/GenStructures/SteleQuests/SteleVault_A.xml` | 石碑封藏庫版面 |
| `1.6/Defs/Misc/DMS_HistoryEvents.xml` | 任務結算事件、`DMS_OccultechResearched`（制裁的好感度變動理由） |
| `1.6/Languages/<lang>/Keyed/DMS_Occultech.xml` | 制裁信件與通訊台對話文本（三語） |
| `_Source/DMS/Occultech/ModExtension_OccultechSanction.cs` | `OccultechTier` 與制裁參數擴充 |
| `_Source/DMS/Occultech/OccultechSanctionUtility.cs` | 制裁判定與執行、豁免、放棄、關係控制、襲擊、發傳票 |
| `_Source/DMS/Occultech/GameComponent_OccultechSanction.cs` | 制裁存檔狀態、追殺排程、週期校正 |
| `_Source/DMS/Occultech/Patch_OccultechSanction.cs` | 四個 Harmony patch（研究完成、好感度封鎖、關係校正、通訊台對話） |
| `_Source/DMS/Occultech/DebugActions_Occultech.cs` | 除錯入口 |
| `_Source/DMS/Occultech/ModExtension_ResearchQuest.cs` | 研究完成派發任務線（量子位元破譯 → 石碑） |
| `_Source/DMS/FacilityServer/CompHuntBreaker.cs` | SAGE 被摧毀時暫停追殺 |
| `_Source/DMS/Quests/QuestNode_Root_DMS_FleetNetworkSite.cs` | SAGE 節點任務（追殺期間限定） |
| `1.6/NewContent/Defs/Quests/DMS_FleetNetworkQuest.xml` | SAGE 節點站點與任務 |
| `1.6/Ideology/Defs/DMS_Precepts_SealedTech.xml` | 議題、三個教義、兩個心情 |
| `_Source/DMS/Quests/Stele/Main.cs` | `QuestNode_Root_Stele`、`QuestPart_SubquestGenerator_Stele`（兩階段主線） |
| `_Source/DMS/Quests/Stele/Sub_Site.cs` | `QuestNode_Root_SteleSite`（共用基底）、`QuestNode_Root_OccultechKey_Site` |
| `_Source/DMS/Quests/Stele/Sub_Vault.cs` | `QuestNode_Root_SteleVault`、`QuestPart_SteleVault` |
| `_Source/DMS/Quests/Stele/CommonParts.cs` | `SteleQuestUtility`、`QuestPart_SteleKeySite`、舊存檔用的 `QuestPart_SteleSubQuestEnd` |
| `_Source/DMS/Quests/Stele/OccultechKey.cs` | `OccultechKey` ThingClass |

### 翻譯

English / 繁體中文 / 简体中文 **三語皆已補齊**：

- `Languages/<lang>/DefInjected/ResearchTabDef/DMS_OccultechResearchTab.xml`
- `Languages/<lang>/DefInjected/KnowledgeCategoryDef/DMS_OccultechResearchTab.xml`
- `Languages/<lang>/DefInjected/ResearchProjectDef/DMS_OccultechProjects_{QubitDecryption,Frogman,BionicSuitHeavy,GestaltEngine,Biosynthesis,Subsonic,RemnantPatterns}.xml`
- `Languages/<lang>/DefInjected/ThingDef/DMS_Occultech.xml`
- `NewContent/Languages/<lang>/DefInjected/ThingDef/DMS_Item_Occultech.xml`
- `Languages/<lang>/DefInjected/HistoryEventDef/DMS_HistoryEvents.xml`
- `Ideology/Languages/<lang>/DefInjected/{IssueDef,PreceptDef,ThoughtDef}/DMS_Ideology.xml`

---

## 框架 API 對照

全部位於 `_Fortified-Framework/_Sources/Fortified/Research/`，命名空間 `Fortified`。

| 類型 | 檔案 | 用途 |
|---|---|---|
| `ModExtension_UniqueResearchTab` | `ModExtension_UniqueResearchTab.cs` | 把分頁變成 Anomaly 式多類別知識分頁，類別順序＝溢流順序 |
| `ModExtension_ResearchDiscovery` | `ModExtension_ResearchDiscovery.cs` | 探明機制的 opt-in 開關（可掛分頁或單一專案） |
| `ResearchDiscoveryUtility` | `ResearchDiscoveryUtility.cs` | 探明判定唯一入口，含 `ForceDiscover`、fail-open 與重入保護 |
| `GameComponent_ResearchDiscovery` | `GameComponent_ResearchDiscovery.cs` | 保存 `announced` / `forced`，每 600 ticks 掃描發信 |
| `GameComponent_KnowledgeStore` / `KnowledgeUtility` | `KnowledgeStore.cs` | 無 Anomaly 時的知識進度儲存與 `AddKnowledge` 溢流路由 |
| `CompProperties_OccultechSource` / `CompOccultechSource` | `CompOccultechSource.cs` | 有限知識源。`totalReserve`、`requiredCategory`、`destroyOnDepleted` |
| `CompProperties_OccultechExtractor` / `CompOccultechExtractor` | `CompOccultechExtractor.cs` | 自動萃取器。`knowledgePerDay`、`knowledgeCategory`、`requireFacing`、`requiresPower` |
| `CompProperties_UseEffect_DiscoverResearch` | `CompProperties_UseEffect_DiscoverResearch.cs` | 解封物品。`researchProjects`、`progressAmount`、`skipCompletedProjects`、`includeDiscoveredProjects`、`fallbackSkill` / `fallbackXpAmount`、`sendLetter` / `letterDef` |
| `Patch_CustomDualResearchTab` | `Patch_CustomDualResearchTab.cs` | 分頁 UI 繪製 |
| `Patch_ResearchProgress` / `Patch_ResearchDiscovery` | 同名檔 | 接回原生 `GetProgress` / `IsHidden` |

### 本模組新增（`_Source/DMS/Occultech/`，命名空間 `DMS`）

| 類型 | 用途 |
|---|---|
| `ModExtension_OccultechSanction` | 掛在 `KnowledgeCategoryDef` 上的制裁規則。`tier`、`goodwillPenalty`、`exemptSeniority`、`hostileOnComplete`、`raidOnComplete`／`raidPointsFactor`／`minRaidPoints`、`permanentHostility`、`huntIntervalDays`／`huntPointsFactor`、`hostileAllyCount`、`renounceable` |
| `OccultechSanctionUtility` | 制裁的唯一判定與執行入口，並提供 `RenounceAll`、`EndPermanentHostility`、`IsAllyLocked`、`EnsureCourtMartialOffered` |
| `GameComponent_OccultechSanction` | `sanctionedProjects` / `permanentHostile` / `collateralFactions` / 追殺排程 |
| `Patch_ResearchManager_FinishProject` | 研究完成 → 觸發制裁 |
| `Patch_Faction_CanChangeGoodwillFor` | 擋下對艦隊的正向好感度變動 |
| `Patch_FactionRelation_CheckKindThresholds` | 關係等級的即時校正 |
| `Patch_FactionDialogMaker_FactionDialogFor` | 通訊台的「放棄封存級技術」選項 |
| `ModExtension_ResearchQuest` / `ResearchQuestUtility` | 掛在 `ResearchProjectDef` 上，研究完成時派發 `quest` 指定的任務線（一局一次）。目前用於量子位元破譯 → `DMS_Stele` |

> `OccultechSanctionUtility.RenounceAll` 需要把單一研究的進度歸零，而原生只提供 `ResetAllProgress()`。
> 因此 `DMS.csproj` 額外 Publicize 了 `ResearchManager.progress` 與 `ResearchManager.anomalyKnowledge`。

---

## 待辦與已知缺口

- 石碑任務線尚未在遊戲內實測（站點生成、守軍強度、500 kg 的運輸體驗）
- Occulted 禁閉級目前沒有任何專案。`DMS_DataCache_Occulted` 暫時解封封存級的蛙人（清單不可為空），加入禁閉級科技後要換掉

### 制裁機制的已知限制

- **晉升典禮（`DMS_PromotionCeremony`）仍需 Royalty**：那是授銜流程本身，無 DLC 時沒有軍銜系統可用，這是設計上的必然而非缺口
- **隱匿級的連坐派系不會自動恢復**：解除永久敵對後那三個派系仍是敵對，需要玩家自行修復
- **`researchMods` 無法還原**：見上方「放棄封存級技術」的說明
- 制裁**不**觸發 `DMS_QuestSuccess_Occultech`，因此 Ideology 的封存科技教義（崇尚／可接受／禁忌）目前仍然只由石碑與撞擊點任務驅動，研究完成不會給心情或發展點數。若希望研究完成也算數，在 `ApplySanction` 裡補一次 `Find.HistoryEventsManager.RecordEvent` 即可
