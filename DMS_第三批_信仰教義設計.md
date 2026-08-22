# 第三批設計：讓 HistoryEvent 真的有效果

**狀態：已於 2026-08-22 實作。** 檔案在 `1.6/Ideology/Defs/DMS_Memes.xml`、`DMS_Precepts_Operations.xml`、`DMS_Precepts_Formality.xml`、`DMS_Precepts_SealedTech.xml`、`DMS_Precepts_SyntheticLife.xml`，三語 DefInjected 在 `1.6/Ideology/Languages/*/DefInjected/{MemeDef,IssueDef,PreceptDef,ThoughtDef}/DMS_Ideology.xml`。

以下設計內容保留原樣作為記錄；實作時與設計不一致的地方列在下一節。

## 0. 實作與設計的差異

**0.1 `DMS_MemberCourtMartialed` 改成不帶 Doer 記錄。**
原設計要在 `Formality_Strict` 的 `KnowsMemoryThought` 上設 `doerMustBeMyFaction: true`。但 `IdeoUtility.Notify_HistoryEvent` 的行為是：事件**帶** Doer 時，只有跟 Doer 同地圖／同商隊的 pawn 會收到「知情」通知；被告在服刑期間被 lend part 移出地圖，等於全殖民地都收不到。事件**不帶** Doer 時，原版改走「通知所有自由殖民者」那條路，正好就是「消息傳回來了」的語意。所以 `QuestPart_CourtVerdict.cs` 改成 `new HistoryEvent(DMS_DefOf.DMS_MemberCourtMartialed)`，precept 端也不設 `doerMustBeMyFaction`（設了反而永遠不觸發）。

**0.2 任務結算事件一律只能用 `KnowsMemoryThought`。**
`QuestScriptDef.successHistoryEvent` 由 `IdeoUtility` 記錄，事件裡沒有 Doer；而 `PreceptComp_SelfTookMemoryThought` 會直接 `GetArg<Pawn>(Doer)`（不是 TryGet），掛上去會拋例外。六個 `DMS_QuestSuccess_*` / `DMS_QuestFailed_*` 與 `DMS_MemberCourtMartialed` 因此全部走 Knows。

**0.3 沒有做「與 Pacifist meme 互斥」。** 查過了，原版沒有 `Pacifist` meme（那是 trait）。改成在 `DMS_Operations_Abhorrent` 與 `DMS_Formality_Contemptuous` 上掛 `conflictingMemes: DMS_Meme_ChainOfCommand`。

**0.4 `DMS_Formality_Casual` 補了 1 點成長點。** 原設計是「無 comps 的純旗標」，但那樣它在 UI 上會完全沒有說明文字。給 `DevelopmentPoints(DMS_QuestSuccess_Ceremony, 1)` 既無害又讓它看起來不像壞掉。`DMS_SyntheticLife_Acceptable` 維持無 comps（「不需要對裝備有感情」本來就該是零效果）。

**0.5 圖示是借用的，需要換。**
- meme：`UI/Faction/ArmyFavor`（DMS 自己的階級章盾牌圖，形狀剛好合用，但那是 favor 圖示不是 meme 圖示）
- issue：`UI/Issues/Raiding`、`UI/Issues/Apostasy`、`UI/Issues/Research`、`UI/Issues/BodyModifications`（全部是原版 Ideology 的圖，Ideology 啟用時一定在）
刻意避開 Biotech 的 `UI/Issues/GrowthVats`：`1.6/Ideology` 不吃 Biotech gating，沒裝 Biotech 會找不到貼圖。

**0.6 meme 沒有做 `descriptionMaker`。** 原版只有 structure meme 強制要求它；normal meme 缺了不會噴 ConfigError，只是信仰自動生成的起源敘述不會出現指揮鏈的段落。那是幾百行的敘述規則，翻譯成本遠大於效益。`generalRules`（16 條命名片段）有做。

---

## 1. 為什麼需要這一步

`HistoryEventDef` 只是一個被丟進 `HistoryEventsManager` 的標籤。它會產生效果，只有四條路：

| PreceptComp | 效果 | 關鍵欄位 |
|---|---|---|
| `PreceptComp_SelfTookMemoryThought` | 做的人自己得到 memory | `eventDef` + `thought` |
| `PreceptComp_KnowsMemoryThought` | 同信仰的所有人得到 memory | `eventDef` + `thought`、`doerMustBeMyFaction`、`doerMustBeMyIdeo` |
| `PreceptComp_UnwillingToDo` | 直接擋下這個行為 | `eventDef` + `eventLabel` |
| `PreceptComp_DevelopmentPoints` | 流動信仰成長點 | `eventDef` + `points` + `eventLabel` |

DMS 現在的 `1.6/Ideology/` 只有 `CultureDef` 與 `StyleCategoryDef`，沒有任何 meme 或 precept，所以上面四條路一條都沒走。

---

## 2. 設計原則

**只做一個 meme，不做一整個信仰體系。** DMS 的定位是「軍事寫實的內容包」，不是 Ideology 擴充。做太多 meme 會跟 Vanilla Ideology Expanded 之類的模組打架，也會逼玩家為了用 DMS 的內容去換掉整套信仰。

**Precept 要能單獨掛在別人的信仰上。** 四個 issue 都設計成獨立的 `IssueDef`，玩家可以只挑「對封存科技的態度」而不接受整個軍紀 meme。

**不要用 `PreceptComp_UnwillingToDo`。** 它會直接讓 pawn 拒絕執行，在 DMS 這種「玩家主動按下按鈕呼叫火力支援」的情境會變成純粹的擋路。所有負面態度一律走 memory thought，讓玩家自己決定要不要吃這個心情。

**全部 gated 在 Ideology DLC 之後。** 檔案放 `1.6/Ideology/Defs/`，`LoadFolders.xml` 已經有 `IfModActive="Ludeon.RimWorld.Ideology"` 的條目。

---

## 3. Meme：`DMS_Meme_ChainOfCommand`（指揮鏈）

一個 normal 級 meme，`impact` 設 `High`，`renderOrder` 隨意。

**世界觀：** 這支殖民地把自己當成一支還沒解編的部隊。軍階、儀典、任務結算是他們維持秩序的方式，而不是形式主義。

**requiredPrecepts / thingStyleCategories：** 沿用既有的 `DMS_StyleCategory_*`。

**它強制帶入的 precept：**
- `DMS_Precept_Formality_Strict`（軍務儀典：嚴格）
- `DMS_Precept_Operations_Venerated`（軍事行動：崇尚）

**它排斥的 meme：** 原版 `Pacifist` 兩者互斥。

---

## 4. 四組 Issue / Precept

### 4.1 Issue：`DMS_Issue_Operations`（軍事行動）

消費 `DMS_QuestSuccess_Military` / `DMS_QuestFailed_Military` / `DMS_CalledFireSupport`。

| Precept | 立場 | comps |
|---|---|---|
| `DMS_Precept_Operations_Venerated` | 打仗是這個群體存在的理由 | `KnowsMemoryThought`(Success_Military, +6, 6 天)<br>`KnowsMemoryThought`(Failed_Military, −8, 6 天)<br>`KnowsMemoryThought`(CalledFireSupport, +3, 2 天)<br>`DevelopmentPoints`(Success_Military, 4) |
| `DMS_Precept_Operations_Necessary` | 打仗是不得已，但該做就做（**預設，weight 最高**） | `KnowsMemoryThought`(Failed_Military, −4, 4 天)<br>`DevelopmentPoints`(Success_Military, 2) |
| `DMS_Precept_Operations_Abhorrent` | 拒絕主動用兵 | `SelfTookMemoryThought`(CalledFireSupport, −10, 6 天)<br>`KnowsMemoryThought`(CalledFireSupport, −5, 4 天) |

`DMS_CalledFireSupport` 是本組的關鍵：它是玩家唯一會反覆按下去的按鈕，也是「這個殖民地怎麼看待間接火力」最好的量測點。`Abhorrent` 這一檔刻意設計成「可以用，但每次都要付心情代價」。

### 4.2 Issue：`DMS_Issue_Formality`（軍務儀典）

消費 `DMS_QuestSuccess_Ceremony` / `DMS_QuestFailed_Ceremony` / `DMS_MemberCourtMartialed`。

| Precept | 立場 | comps |
|---|---|---|
| `DMS_Precept_Formality_Strict` | 授銜、完訓、軍法都是神聖的 | `KnowsMemoryThought`(Success_Ceremony, +5, 8 天)<br>`KnowsMemoryThought`(Failed_Ceremony, −10, 8 天)<br>`KnowsMemoryThought`(MemberCourtMartialed, −6, 10 天，`doerMustBeMyFaction=true`)<br>`DevelopmentPoints`(Success_Ceremony, 5) |
| `DMS_Precept_Formality_Casual` | 誰扛槍誰說話，階級是紙上的東西（**預設**） | 無 comps（純旗標，供其他內容判斷） |
| `DMS_Precept_Formality_Contemptuous` | 軍階是壓迫的工具 | `KnowsMemoryThought`(Success_Ceremony, −4, 6 天)<br>`KnowsMemoryThought`(MemberCourtMartialed, +4, 6 天) |

注意 `MemberCourtMartialed` 要設 `doerMustBeMyFaction: true`。它記的 `Doer` 是被告，不是判決者；沒有這個限制的話，把外派的殖民者送去受審會讓整個殖民地覺得「有人被判刑了」而不分敵我。

### 4.3 Issue：`DMS_Issue_SealedTech`（封存科技）

消費 `DMS_QuestSuccess_Occultech` / `DMS_QuestFailed_Occultech`。

| Precept | 立場 | comps |
|---|---|---|
| `DMS_Precept_SealedTech_Venerated` | 挖出封存科技是使命 | `KnowsMemoryThought`(Success_Occultech, +8, 15 天)<br>`DevelopmentPoints`(Success_Occultech, 8) |
| `DMS_Precept_SealedTech_Acceptable` | 有用就拿（**預設**） | `DevelopmentPoints`(Success_Occultech, 3) |
| `DMS_Precept_SealedTech_Forbidden` | 那些東西被封起來是有理由的 | `KnowsMemoryThought`(Success_Occultech, −8, 15 天) |

`Forbidden` 這檔跟 DMS 現有的封存科技敘述最搭：那些 def 的收尾本來就是一段「經 ███ 及 ██ 之審批，列為 I 級封存技術」的公文。做一個信仰站在審批者那一邊，內容上是自洽的。

### 4.4 Issue：`DMS_Issue_SyntheticLife`（人造生命）

消費 `DMS_GestatedMech` 與 `DMS_AteSyntheticFood`。

| Precept | 立場 | comps |
|---|---|---|
| `DMS_Precept_SyntheticLife_Venerated` | 人造生命比血肉更可靠 | `KnowsMemoryThought`(GestatedMech, +6, 10 天)<br>`SelfTookMemoryThought`(AteSyntheticFood, +2, 1 天) |
| `DMS_Precept_SyntheticLife_Acceptable` | 是工具（**預設**） | 無 comps |
| `DMS_Precept_SyntheticLife_Abhorrent` | 用子宮長出機器是褻瀆 | `SelfTookMemoryThought`(GestatedMech, −12, 15 天)<br>`KnowsMemoryThought`(GestatedMech, −8, 10 天)<br>`SelfTookMemoryThought`(AteSyntheticFood, −4, 1 天) |

`DMS_AteSyntheticFood` 目前掛在 `DMS_CombatRation`（C 口糧）與 `DMS_Artiprotein`（人工蛋白）兩個 ThingDef 上，只給兩個極端檔，因為這兩樣都是常態消耗品，中間檔如果也給心情會變成每天都在跳 thought。`Abhorrent` 的 −4 已經跟現有的 `DMS_OverEat`（−12，沒有營養接口時）疊加，兩者相加會很痛，這是刻意的。

**已知缺口：熟食不算。** 原版只在「直接吃下去」時觸發 `ateEvent`（`FoodUtility.ThoughtsFromIngesting` 的 direct 分支）。用人工蛋白煮成的餐點走 `AddIngestThoughtsFromIngredient`，那條路只認寫死的 `AteHumanMeatAsIngredient` / `AteFungusAsIngredient` / `AteInsectMeatAsIngredient`，不會讀 ingredient 的 `ateEvent`。人工蛋白的主要用途正是當食材，所以這個 issue 要真的有感，需要一個 `AddIngestThoughtsFromIngredient` 的 Harmony postfix 外加一個 `DMS_AteSyntheticFoodAsIngredient` 事件——這屬於第二批規模的工作，尚未實作。

---

## 5. 需要一併產出的 ThoughtDef

上表一共要 **17 個 `ThoughtDef`**（每個 precept comp 一個），命名 `DMS_Thought_<Issue>_<Stance>_<Event>`。全部是單 stage 的 `Thought_Memory`，`durationDays` 如表所列，`stackLimit` 建議 2～3（`Success_Military` 這種會連續觸發的設 3，`Occultech` 這種一輩子幾次的設 1）。

`PreceptComp_KnowsMemoryThought` 會呼叫 `ThoughtUtility.GetNullifyingTraits`，所以負面 thought 記得掛 `nullifyingTraits`（`Psychopath`、`Bloodlust` 對應戰鬥相關的那幾個）。

---

## 6. 翻譯成本估算

| 項目 | 數量 | 每項需翻的欄位 |
|---|---|---|
| MemeDef | 1 | label / description |
| IssueDef | 4 | label / description |
| PreceptDef | 12 | label / description / (部分) `eventLabel` |
| ThoughtDef | 17 | stage label / stage description |

約 **68 個字串 × 3 語 = 204 條**。比第一批＋第二批的 Tale rulePack（約 230 條）稍小，但敘述性文字更長。建議照 issue 分四次做，不要一次全上。

---

## 7. 風險與注意事項

**跟 `CultureDef` 的關係。** DMS 現有的 `1.6/Ideology/Defs/Cultures.xml` 是獨立系統，不需要改；meme 與 culture 在原版是正交的。

**`enabledForNPCFactions`。** `PreceptComp_KnowsMemoryThought` 會檢查這個旗標。DMS 的 NPC 派系（`DMS_Army`、`DMS_Legacy`）如果被指派到帶這些 precept 的信仰，預設不會生效，這是對的——不需要為 NPC 開。

**流動信仰的成長點會被玩家用來刷。** `DevelopmentPoints` 對 `DMS_SupplyChain` 這類可重複接的任務要給得保守（表中的 Logistics 我刻意完全沒給），否則補給鏈任務會變成信仰成長的農場。目前 `DMS_QuestSuccess_Logistics` 與 `DMS_QuestFailed_Logistics` 在第一批已經記錄，但**這份設計不消費它們**——留給未來的「契約精神」issue，或者乾脆維持純資料。

**先做一個 issue 驗證手感。** 建議從 4.1（軍事行動）開始，它掛的 `DMS_CalledFireSupport` 是唯一高頻事件，最快能看出數值調得對不對。
