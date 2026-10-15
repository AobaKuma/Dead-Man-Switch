# DMS 情境音樂切換系統實作計畫

更新日期：2026-10-15（同日依 1.7 原版、FFF 1.7 與 DMS 原始碼查證後修訂；第 2.3 節六項亦於同日查證結案）  
目標版本：DMS 2／RimWorld 1.7  
文件狀態：實作前計畫。第 2 節標示「已查證」者為對照組件原始碼的結果；新增型別、欄位與門檻仍為設計，不代表目前已存在。待決策事項集中於第 11 節。

## 1. 目標與範圍

建立配合 DMS 站點、地下設施、守墓人、Boss 與封鎖機制的情境音樂系統，使用原版 `MusicTransitionDef` 與 `MusicSequenceDef` 管理播放。音樂跟隨目前觀看地圖，既能預告警戒，又不因遠方事件搶走配樂。

聲音方向：冷戰軍事電子樂、復古類比合成器、低沉電貝斯伴奏、乾燥且克制的打擊樂。保留 RimWorld 的孤寂、留白與疲憊感；排除西部、木吉他、主奏電吉他、EDM drop 與史詩管弦樂高潮。

本次範圍：

- 八種音樂狀態及其 transition／sequence，與一個自訂 sequence worker。
- 六組主要音樂資產；探索與受困情境允許共用或變奏。
- 地圖分類、事件接入、Boss 追蹤、停戰緩衝、戰後餘波。
- 存讀檔、舊存檔恢復、地圖切換、暫停、原版特殊音樂與 DLC 音樂相容。
- 玩家設定開關（停用 DMS 情境音樂）與三語翻譯。
- 開發者診斷與可重現的驗收場景。

不在本次範圍：修改警戒機制、守軍 AI、Boss 難度、全域殖民地日常音樂、依封存科技研究進度長期覆蓋配樂，以及逐小節動態混音。

## 2. 已確認的現況與限制

### 2.1 原版音樂系統（已查證，1.7 `Assembly-CSharp`）

**仲裁（`MusicManagerPlay.CheckTransitions()`，public）**

- 由 `MusicUpdate()` 在 `GenTicks.IsTickInterval(60)`（即 `TicksGame % 60 == 0`）時呼叫，且狀態為 `Fadeout` 時不呼叫。暫停時 `TicksGame` 不前進，所以通常整段暫停期間都不檢查；若剛好停在 60 的倍數上，則改為每幀檢查。
- 優先值較低的 transition **永遠無法取代**目前已觸發的 transition（條件為 `triggeredTransition.priority <= transition.priority`）。同優先值也不會互相取代。
- 能否被更高優先的 transition 打斷，只看目前 sequence 的 `CanBeInterrupted()`（預設回傳 `canBeInterrupted`，預設 true）或對方的 `overridesInterruptions`。`minTimeToPlay` 不影響是否被打斷。
- 目前 transition 的離開條件：`useTransitionForLifetime`（預設 true）且非手動觸發、`MinTimeSatisfied()`、`IsTransitionSatisfied()` 為 false。成立後以 `overrideFadeout`（未設為 10 秒）淡出；淡出期間不檢查 transition，淡出完成後的下一次檢查才接手。
- 升級時，舊曲以**新** sequence 的 `fadeoutLastSongDuration`（預設 10 秒）淡出後才播新曲。
- `MinTimeSatisfied()`：只在第一輪（`timesLooped <= 0`）比較 `SongTime`（音源播放秒數）與 `minTimeToPlay`。
- `MusicTransition.IsTransitionSatisfied()` 基底只處理 `dangerRequirement`。

**Sequence worker（`MusicSequenceWorker`）**

- `ShouldTransition()`、`ShouldLoop()`、`ShouldEnd()`、`CanBeInterrupted()`、`SelectSong()` 皆為 virtual；`InitializeWorker()` 不是。
- `ShouldEnd()` 每幀（Sequence 狀態）被呼叫；回傳 true 即淡出結束序列，可繞過 `minTimeToPlay`。
- transition 對應的 sequence 固定（`def.sequence`），但 `SelectSong()` 可依情境選曲；只在開歌與每次循環時呼叫。
- `SelectSong()` 預設以 `timesLooped % songs.Count` 取曲；**曲庫為空時除以零並每幀拋例外**。`song` 與 `songs` 同時設定且使用預設 worker 會產生 config error。
- `loop=false` 的序列播完即 `Stop()`，`triggeredTransition` 清空；若該 transition 仍成立，下次檢查會重播。原版沒有「序列完成」回呼。
- 其他可用欄位：`loopDelayRange`、`fadeInDuration`、`loopFadeout`、`overrideVolume`、`pausedVolumeFactor`、`nextSequence`、`endOnDanger`／`endOnNoDanger`、`transitionOnDanger`／`transitionOnNoDanger`。

**危險訊號**

- `DangerMusicMode` = `OverrideDangerMode` 或劇本覆寫，或**任一地圖** `dangerWatcher.DangerRating == High`。在站點觀看時，基地被襲也會為 true。
- 單張地圖的 `DangerWatcher` 每 101 tick 重算：未起霧、敵對且有威脅的目標 `combatPower` 總和 > 400 即 High；殖民者 900 tick 內受傷也為 High。只排除未攻擊中的 `LordJob_DefendPoint`／`LordJob_MechanoidDefendBase` 成員與休眠單位，DMS 自訂 LordJob 的守軍一被看見就會計入。

**強制播放（含外部偵測，已查證）**

- 簽章：`public void ForcePlaySong(SongDef song, bool ignorePrefsVolume)`，非 virtual，只轉呼叫私有 `PlaySong(song, forced: true, ignorePrefsVolume)`。`PlaySong` 設定私有 `songWasForced`、`currentSong` 與私有 `songEndTime = Time.time + song.clip.length`，並令 `IsPlaying = true`。**不會**清除 `sequenceWorker` 或 `triggeredTransition`。
- 強制曲期間 `MusicUpdate()` 每幀把狀態重設為 `Normal`、`fadeoutFactor = 1`：`UpdateSequenceState()` 不執行，任何淡出只持續一幀就被重設。`CheckTransitions()` 仍照常執行：若此時有 transition 觸發，新 sequence 的 `fadeoutLastSongDuration` 大於 0 時 `StartSequence()` 只開始淡出、隨即被重設，transition 被標為觸發卻不播放，強制曲結束後由原版一般曲接續，直到該 transition 不成立或下一次淡出完成才恢復；為 0 時則直接 `PlaySong(..., forced: false)`，強制曲被立即切掉。
- `songWasForced` 只在下一次 `PlaySong(forced: false)` 才清除；強制曲播完後（`Time.time >= songEndTime`）`IsPlaying` 變 false，但 `songWasForced` 仍為 true。因此私有旗標單獨使用時無法判斷「強制曲是否仍在播」。
- 原版 1.7 的呼叫端只有 `CompVoidNode`（Anomaly）、`Screen_Credits` 與開發者動作；FFF 1.7 有 `CompUseEffect_PlaySong`；DMS 有 `QuestPart_BossgroupArrivesWithMusic`（實際上 `song` 恆為 null）。
- **偵測方式**：Harmony postfix `MusicManagerPlay.ForcePlaySong` 可行（public、非 virtual、無多載），用來取得「開始」事件：記下 `song` 與開始的 `Time.time`，並立即把 snapshot 標髒。「是否仍在強制播放」以每幀輪詢判斷，條件為 `IsPlaying && songWasForced`：私有欄位以 `AccessTools.FieldRefAccess<MusicManagerPlay, bool>("songWasForced")` 快取讀取，與原版語意完全一致（自然播完時 `IsPlaying` 變 false；`loopFadeout` 重播或任何非強制開歌都會清掉旗標）。若欄位日後改名、FieldRef 建立失敗，退回 `IsPlaying && CurrentSong == 記錄的曲目`，只在 `loopFadeout` 以同一首重播的邊界情況多讓位一首。記錄放在不存檔的客戶端組件；`MusicManagerPlay` 在讀檔時重建，新遊戲／讀檔時清空。
- **幀內順序**：`Root_Play.Update()` 先呼叫 `Current.Game.UpdatePlay()`（含 `GameComponentUpdate()`），再呼叫 `musicManagerPlay.MusicUpdate()`。在 `songWasForced` 為 true 且 `IsPlaying` 仍為 true 時主動呼叫 `CheckTransitions()`，就會落入上述「已觸發卻不播放」的狀態。因此 DMS worker 在強制期間一律回傳 false，DMS 也只在上述條件轉為 false 之後的幀才主動檢查。強制曲結束的那一幀，`MusicUpdate()` 先把 `IsPlaying` 設為 false 再執行（若剛好輪到的）60 tick 檢查，此時觸發的序列可正常開始。
- 強制曲自然結束的那一幀，若 `nextSongStartTime` 落在 `[現在 − 5 秒, 現在]`，原版會立即 `StartNewSong()` 插入一般曲，下一幀才由 DMS 以 `fadeoutLastSongDuration` 淡出。可選的緩解：snapshot 仍有 DMS 狀態且強制曲剩不到一幀時，呼叫 public 的 `ScheduleNewSong()`（把下一首延後 1 秒）。實作時二擇一，並列入驗收。

**時間**

- `MusicManagerPlay` 的 `CurTime` 為 `Time.time`（實際秒數，不受遊戲速度影響）。
- `Game.UpdatePlay()` 每幀無條件呼叫 `GameComponentUpdate()`，暫停與觀看世界地圖時也會執行；`TickManagerUpdate()` 在同一幀內可能跑多個 tick，所以高速時 60 tick 檢查只在幀邊界剛好落在 60 的倍數時發生。
- `MusicSequenceWorker.InitializeWorker()` 的洗牌與 `MusicManagerPlay` 的間隔亂數都使用 `Verse.Rand`。
- 讀檔時 `MusicManagerPlay` 重新建立，`triggeredTransition` 為空，第一次檢查即重新挑選，不需保存播放狀態。

**交戰事件 hook 候選（已查證，採用 `Verb.TryCastNextBurstShot`）**

| 候選 | 簽章／呼叫端 | 涵蓋 | 不涵蓋或問題 | 頻率與成本 |
|---|---|---|---|---|
| `Verse.Verb.TryCastNextBurstShot` | `protected void TryCastNextBurstShot()`，非 virtual；Verb 每發射一發（含連發每一發）呼叫一次 | 所有 Verb 攻擊：pawn 與砲塔（`Building_TurretGun`）、遠程與近戰、迫擊砲；含未命中；可讀 public 的 `caster` 與 `CurrentTarget` | 不經 Verb 的傷害：陷阱、IED、直接生成投射物或空投的轟炸效果 | 每發一次，全地圖都會呼叫；第一個判斷必須是 O(1) 的「此地圖是否需要記錄」 |
| `Verse.Pawn_HealthTracker.PostApplyDamage` | `public void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)`；唯一呼叫端 `Pawn.PostApplyDamage` | 對 pawn 實際造成的傷害，包含非 Verb 的爆炸與轟炸；`dinfo.Instigator` 可判敵友 | 未命中；被護盾吸收（`PreApplyDamage` 的 `absorbed` 直接返回，不會到這裡）；攻擊砲塔或建築；火焰等環境傷害也會進入，需以 Instigator 過濾 | 每次命中一次 |
| `RimWorld.DangerWatcher.Notify_ColonistHarmedExternally` | `public void Notify_ColonistHarmedExternally()`，無參數；唯一呼叫端 `Pawn_HealthTracker.PreApplyDamage`（HomeFaction 為玩家且 `ExternalViolenceFor`） | 玩家單位受外部暴力 | 沒有 instigator，無法排除友軍誤傷與陷阱；玩家先開火不計；寫入的 `lastColonistHarmedTick` 為 private | 極低，但語意不符 4.2 |
| `Pawn_MindState.lastAttackTargetTick`／`lastAttackedTarget` 輪詢 | public 欄位，由 internal `Notify_AttackedTarget()` 在 `TryCastNextBurstShot` 成功出手後寫入 | pawn 的攻擊 | 砲塔（非 pawn）；需逐 pawn 掃描 | O(pawn 數)；只適合少量已知 pawn（例如友方支援 lord，見 2.2） |
| `Pawn_MindState.lastCombatantTick` | public 欄位 | — | `MindStateTickInterval` 在 pawn 持有主武器時就更新，等同「有武器」，不可用 | — |

結論：以 Harmony prefix 掛 `Verb.TryCastNextBurstShot`（`[HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]`），讀 `__instance.caster` 與 `__instance.CurrentTarget`。過濾順序：caster 所在地圖的 DMS 音樂組件旗標（O(1)）→ 目標為 Thing → 雙方 `HostileTo` 且其中一方為玩家 → 目標為未倒地 pawn 或砲塔建築。成立就在該地圖記錄最後交戰 tick。Verb 在模擬流程內執行，寫入的是遊戲端狀態，Multiplayer 下各端一致。不經 Verb 的傷害第一版不單獨觸發交戰，只透過警戒原因（例如 `IsAirspaceBlocked`）反映；實測不足時再以 `Pawn_HealthTracker.PostApplyDamage` 補強。

**Multiplayer（已查證 Multiplayer 0.11.5 的 1.6 版；本機沒有 1.7 版）**

- 本機 Workshop 的 `rwmt.Multiplayer`（2606448745）`supportedVersions` 只有 1.5 與 1.6，沒有 1.7 組件。以下為 `1.6/AssembliesCustom/Multiplayer.dll` 的行為；1.7 版發布後必須重查。
- 每張地圖的模擬在 `Multiplayer.Client.AsyncTimeComp.Tick()` 內執行，前後由 `PreContext()`／`PostContext()` 包住：先 `Rand.PushState()` 並設 `Rand.StateCompressed = randState`，結束時存回 `randState` 再 `Rand.PopState()`。世界 tick（`AsyncWorldTimeComp`）同理。同步檢查只比對這些地圖、世界與指令的亂數狀態。
- `MusicManagerPlay` 不在 MP 的任何 patch 清單中（`MultiplayerStatic.DoPatches()` 的 non-deterministic 清單只有音效、Effecter、Mote／Fleck 等），`Multiplayer.dll` 也沒有任何 Music 相關字串。`MusicUpdate()` 在 `Root_Play.Update()` 的幀流程中執行，位於上述 Push/Pop 之外：原版選曲、間隔與洗牌消耗的是未同步的外層 `Verse.Rand`，各客戶端各自選曲，不參與同步檢查。
- 推論：在幀流程呼叫 `CheckTransitions()`／`StartSequence()` 不會造成不同步；從 tick（例如 `MapComponentTick`）呼叫則會消耗該地圖的同步亂數，而且只發生在觀看該地圖的客戶端，必然不同步。5.2 的限制成立。
- MP 的非同步時間：每張地圖有自己的 `mapTicks`，只有在該地圖的 tick 內（以及 `SetMapTimeForUI` 列出的 UI 方法內）`Find.TickManager.TicksGame` 才是該地圖時間。客戶端幀流程讀到的 `TicksGame` 不一定是觀看地圖的時間。因此警戒上升、接敵、倒數、餘波等 tick 窗口必須在 `MapComponentTick` 內換算成布林值或剩餘 tick 存進 snapshot，客戶端只讀結果，不自行拿 `TicksGame` 相減。

**SongDef 與音檔載入**

- `SongDef.clipPath` 由 `ContentFinder<AudioClip>` 解析；模組的 AudioClip **只**從各載入資料夾的 `Sounds/` 讀取（`GenFilePaths.ContentPath<AudioClip>()`），不存在 `Music/` 資料夾的載入規則。支援 `.wav`、`.mp3`、`.ogg` 等格式。
- `SoundDef` 與 `SongDef` 使用同一套 AudioClip 路徑規則；`Defs/DMS_Sound.xml` 的 `clipPath` 寫法可作為參考。
- `SongDef.playOnMap` 預設 true、`tense` 預設 false：未處理時，原版 `ChooseNextSong()` 會把 DMS 曲目當成一般殖民地平靜曲隨機播放。Anomaly 序列曲與 DMSE 以 `commonality=0` 避開輪播；`playOnMap=false` 亦可。
- 1.7 的模組音檔大於串流門檻時會串流播放，記憶體風險較低，仍需實測首次載入時間。

**DLC transition（皆以 `Find.Maps` 全域判斷，不限目前地圖）**

| DLC | defName | 優先值 | 中斷性 | 觸發摘要 |
|---|---|---:|---|---|
| Odyssey | `OrbitalRelax` | 50 | 可中斷 | 存在太空層地圖且無危險 |
| Odyssey | `OrbitalCombat` | 75 | `canBeInterrupted=false`，`endOnNoDanger`，最短 30 秒 | 存在太空層地圖且有危險 |
| Odyssey | `Mechhive` | 100 | 共用 `OrbitalCombat` sequence | 存在太空層地圖、任一地圖有 Cerebrex 核心且有危險 |
| Anomaly | `HorrorRelax` | 100 | 可中斷 | 灰霧、迷宮／地底口袋地圖等，且無危險 |
| Anomaly | `HorrorTension` | 150 | 可中斷 | 不自然黑暗、死亡帷幕、血雨、地底生成器、仇恨吟唱等，且無危險 |
| Anomaly | `HorrorMonolithAdvanced` | 175 | `canBeInterrupted=false`，`useTransitionForLifetime=false`（播完為止） | 巨石等級提升後 3600 tick 內 |
| Anomaly | `HorrorCombat` | 200 | `canBeInterrupted=false`，`endOnNoDanger`，最短 30 秒 | 有危險且有 Anomaly 實體／儀式 |
| Anomaly | `CorpseAttack` | 600 | `overridesInterruptions` | 甦醒屍體攻擊 |
| Anomaly | `MetalHell` | 999 | `overridesInterruptions` | 存在金屬地獄口袋地圖 |

Core 本身沒有 transition。

### 2.2 DMS、FFF 與相關模組（已查證）

**Boss**

- `_Source/DMS/QuestPart_BossgroupArrivesWithMusic.cs`：抵達時若 `song` 非空則呼叫 `ForcePlaySong`，並清空同任務內 `QuestPart_Bossgroup.bosses`；存檔欄位 `song`。新系統不能以該清單追蹤 Boss。
- `_Source/DMS/ModExtension_BossSong.cs`：**目前沒有任何 XML 使用**，`song` 永遠為 null，`ForcePlaySong` 實際上從未執行。`GetSong()` 對空清單呼叫 `RandomElement()`。
- `_Source/DMS/QuestNode_Root_BossgroupFactionExposed.cs` 生成 Boss（`list3`）與護衛，是登記遭遇的候選位置；投放由任務 `DropPods` 在抵達訊號後執行。
- `_Source/DMS/BossKillRelay/BossKilledSignal.cs`：`PawnKindDef.isBoss` 的 pawn 死在地圖上時廣播 `DMS_BossKilled`（參數 SUBJECT／POSITION／MAP／INSTIGATOR）。`CompBossKillRelay.requireAllBossesDead` 已實作「同圖最後一架 Boss」判斷，可重用。
- 軍火庫站點的 Boss（駭蜥）不預置在地圖上，由停機坪的誘餌雷達召喚。

**Boss 生成路徑（已查證）**

DMS 只有四個 Boss 入口，全部經原版 `RimWorld.BossgroupWorker.Resolve(Map map, int wave)`（public virtual）：

| 入口 | 元件 | BossgroupDef → Boss PawnKind | 放置處 |
|---|---|---|---|
| 誘餌天線 `DMS_DecoyRadio` | `DMS.CompUseEffect_SummonRaid`（`Defs/Things_Building/DMS_Boss.xml`） | `DMS_Geochelone` → `DMS_Mech_Geochelone` | 誘餌站點版面 `DMS_DecoySiteOutpost_A`／`_B`（`Site_DecoyOutposts.xml`）與 `DMS_DecoySiteStructure` 的 centerpiece |
| 誘餌雷達 `DMS_DecoyRadar` | 同上 | `DMS_Tarbosaurus` → `DMS_Mech_Tarbosaurus` | 軍火庫 Boss 停機坪 `DMS_Arsenal_Boss`（`Site_Arsenal.xml`） |
| 誘餌發射器 `DMS_DecoyTransmitter` | 同上 | `DMS_ClusterWeaver` → `DMS_Mech_ClusterWeaver` | 機場版面 `DMS_Airport`（`Site_LegacyInstallations.xml`）與機場任務的 centerpiece |
| 加密發報器 `DMS_EncryptedTransmitter`（物品） | `Fortified.CompUseEffect_SummonRaid`（`Defs/Things_Item/DMS_Manufactured.xml`） | `DMS_Sergeant` → `DMS_Mech_Sergeant` | 玩家使用 |

- 三座誘餌建築的基底 `DMS_BossCallerBase` 有 `designationCategory`，玩家也能在基地建造，所以 Boss 遭遇可能發生在玩家據點。
- DMS 的 BossgroupDef 都掛 `Fortified.CallableExtension`：FFF 的 `Patch_BossgroupEverCallable` 讓 `CallBossgroupUtility.BossgroupEverCallable` 回傳 false，原版機械師召喚器叫不出 DMS Boss。
- 流程：`Resolve()` → `Find.BossgroupManager.Notify_BossgroupCalled(def)` → 隱藏且自動接受的任務 `DMS_Boss`（`Defs/Quests/BossGroup.xml`）→ `QuestNode_Root_BossgroupFactionExposed.RunInt()` 生成 Boss 與護衛並傳給世界 pawn，加入 `QuestPart_BossgroupArrivesWithMusic`（`minDelay` 2500–5000、`maxDelay` 60000–180000，`inSignalEnable` 取 slate 的 `inSignal`，由 `QuestGen.InitializeQuestGen()` 預設為任務初始訊號，任務一開始即啟用）→ 抵達訊號 → `QuestPart_DropPods`（落點 `DropCellFinder.FindRaidDropCenterDistant`）與 `QuestPart_Bossgroup`（`LordJob_BossgroupAssaultColony`）。
- 延遲倒數可讀：`QuestPart_BossgroupArrives` 的 `minDelay`、`maxDelay`、`mapParent`、`bossgroupDef` 為 public 欄位，`EnableTick` 為 public 屬性（未啟用時回傳 −1，`enableTick` 本身為 protected）。`QuestPartTick()` 由 `Quest.QuestTick()` 每 tick 呼叫：過了 `enableTick + minDelay` 後，在 `mapParent.IsHashIntervalTick(2500)` 且 `bossgroupDef.Worker.ShouldSummonNow(mapParent.Map)`（地圖上沒有交易、訪客或授勳 lord）時 `Complete()`；到 `enableTick + maxDelay` 強制完成。
- 預計抵達 tick：`Gen.HashOffsetTicks(this WorldObject)` 與 `Gen.IsHashIntervalTick(this WorldObject, int)` 為 public，可推算「`EnableTick + minDelay` 之後第一個 2500 hash 邊界」；`ShouldSummonNow` 為 true 時誤差為 0。實際抵達落在呼叫後 2500–7500 tick（1 倍速約 42–125 秒），被訪客阻擋時延後，最晚 `maxDelay`。`QuestPart_BossgroupArrivesWithMusic` 是 DMS 自有類別，可直接覆寫 `QuestPartTick()` 發布預計抵達 tick，不需 Harmony。
- 抵達到 spawn：`Complete()` 送出的訊號由 `SignalManager.SendSignal()` 同步分派，同一呼叫內 `QuestPart_DropPods` 以 `DropPodUtility.DropThingsNear(..., openDelay: 110, ...)` 投下 `DropPodIncoming`（`SkyfallerProperties.ticksToImpactRange` 預設 120–200），落地後 110 tick 開艙。Boss 約在抵達訊號後 230–310 tick（1 倍速約 4–5 秒）才 spawn；抵達訊號當下 Boss 在艙內（`MapHeld` 有值、`Spawned` 為 false），lord 已建立。
- 改投地圖：投放時若 `mapParent` 已沒有地圖（玩家已離開站點），`QuestPart_DropPods` 改用 `Quest.TryFindNewSuitablePlayerMapParentForRetarget()` → `QuestScriptDef.TryFindNewSuitableMapParentForRetarget()`，投到玩家據點（或有重力引擎的地圖）。遭遇的目標地圖必須以實際投放地圖為準。站點被摧毀時任務以 `mapParent.Destroyed` 結束，Boss 不會出現。
- `QuestPart_BossgroupArrivesWithMusic.Complete()` 在 `base.Complete()` 之後清空 `QuestPart_Bossgroup.bosses`：登記 Boss 必須在清空前讀取，或沿用 `RunInt()` 生成時的登記。
- `isBoss` 不等於 Boss 遭遇：`DMS_Mech_Caretta`（`DMS_Army` 一般 Combat 編組）與 `DMS_Mech_Tarbosaurus`（`DMS_Legacy` Combat 編組，權重 0.25）也設 `isBoss=true`，會隨一般襲擊或駐軍出現；`DMS_Mech_Killdozer`（`BaseKillDozerKind`）也設 `isBoss=true`。`DMS_BossKilled` 與 `CompBossKillRelay` 都以 `isBoss` 判斷，音樂端必須再以已登記的遭遇 pawn 過濾。

**FFF 1.7 限時站點增援（原版 `RimWorld.Planet.TimedDetectionRaids`，已查證）**

- FFF 沒有自訂增援倒數類別。`Fortified.SitePartWorker_FFF_TimedHostileSite.PostMapGenerate(Map)` 取 `map.Parent.GetComponent<TimedDetectionRaids>()`，呼叫 `StartDetectionCountdown(ticks, 0)` 並設 `alertRaidsArrivingIn = true`。倒數長度為 `FFF_TimedSiteUtility.EntryCountdownTicks(baseHours, minHours, maxHours)` = `Clamp(baseHours / threatScale, min, max) × 2500`，參數取自 `FFF_TimedSiteExtension`（預設 36／8／72 小時）。原版 `Site` WorldObjectDef 本身就帶這個 comp。
- 使用此 worker 的 DMS SitePartDef（`1.7/NewContent/Defs/Quests/`）：`DMS_DecoySite`（餘械中繼站）、`DMS_SageNodeSite`（SAGE 節點）、`DMS_AirportSite`（餘械機場）、`DMS_LaunchSiteSite`（廢棄發射場）、`DMS_OperationBaseSite`（餘械前進基地）、`DMS_RepairStationSite`（餘械維修站）、`DMS_SiteStationSite`（荒廢餘械站點）、`DMS_VaultStationSite`（儲存庫地面站）、`DMS_PirateAssemblySite`（海盜組裝廠）。軍火庫的 `DMS.SitePartWorker_ArsenalSite` 繼承它且未覆寫 `PostMapGenerate`，同樣啟動倒數。
- 公開介面：屬性 `TicksLeftToSendRaids`、`DetectionCountdownStarted`（`ticksLeftToSendRaid >= 0`）、`NextRaidCountdownActiveAndVisible`、`RaidsSentCount`、`DetectionCountdownTimeLeftString`；欄位 `alertRaidsArrivingIn`、`delayRangeHours`；方法 `StartDetectionCountdown(int ticks, int notifyTicks = -1)`、`ResetCountdown()`、`SetNotifiedSilently()`、`CopyFrom(TimedDetectionRaids)`。
- 倒數與波次：`CompTickInterval(int delta)` 以 delta 遞減，歸零時以 `IncidentDefOf.RaidEnemy` 對該地圖發動襲擊（點數 `DefaultThreatPointsNow × 1.5`，派系為站點派系，不適用時改機械族），成功則 `raidsSentCount++`。襲擊屬原版 RaidEnemy，立即從地圖邊緣或空投進場，沒有額外的「接近中」階段。
- 重複：每次發動後立刻以 `delayRangeHours.RandomInRange × 2500` 重設（FFF 未改，預設 18–24 小時），只要地圖存在就一直重複，清空敵人也不停。
- 停止：唯一停止點是地圖消失（`!mapParent.HasMap` 時 `ResetCountdown()`）；FFF 與 DMS 都沒有其他地方呼叫 `ResetCountdown()`。
- 存檔欄位：`ticksLeftToForceExitAndRemoveMap`（實際存的是 `ticksLeftToSendRaid`，原版沿用舊鍵名）、`ticksLeftTillNotifyPlayer`、`alertRaidsArrivingIn`、`raidsSentCount`、`delayRangeHours`。
- 音樂用法：`DetectionCountdownStarted && TicksLeftToSendRaids <= 1800` 為增援接近的警戒原因；`RaidsSentCount` 增加即「增援抵達」，於 `MapComponentTick` 比對。comp 掛在地表 `Site` 上，只適用地表地圖；地下口袋地圖沒有這個倒數。

**FFF 1.7 RaidWave（設施快速反應部隊，已查證）**

- DMS 入口：`DMS.CompAlertEffector_CallBattleGroup.DoEffect()`（戰鬥群呼叫器）與 `DMS.FacilityAlarmUtility.Trip()` 的保底呼叫，都經 `DMS.BattleGroupCallUtility.TryCall(RaidWaveDef, Thing source, bool respectCooldown, SoundDef, EffecterDef)` → `Fortified.RaidWaveWorker.Resolve(Map)`。玩家信標 `DMS_RaidWaveBeacon_PeacekeepingOperators` 經 `Fortified.CompUseEffect_CallRaidWave`。使用 `DMS_RaidWave_FacilityQRF` 與 `DMS_RaidWave_PeacekeepingOperators`，派系都是 `DMS_Legacy`，lord 為 `LordJob_AssaultColony`，對玩家敵對（信標名稱雖是「維和」，叫來的不是友軍）。
- `RaidWaveWorker.Resolve(Map)` 以 `GameComponent_RaidWave.Notify_RaidWaveCalled(def)` 記錄次數，產生隱藏任務 `FFF_RaidWave_Default`（`Fortified.QuestNode_Root_RaidWave`）：生成 pawn 傳給世界 → `Fortified.QuestPart_RaidWaveArrives`（`minDelay` 2500–5000、`maxDelay` 60000–180000）→ 抵達訊號 → `QuestPart_DropPods`（`FindRaidDropCenterDistant`）與 `Fortified.QuestPart_RaidWave`。
- `QuestPart_RaidWaveArrives.QuestPartTick()` 與原版 `QuestPart_BossgroupArrives` 相同，另多檢查 `mapParent.Map != null`。public 欄位 `minDelay`、`maxDelay`、`mapParent`、`raidWaveDef`，`EnableTick` 可讀，預計抵達 tick 推算同 Boss。存檔欄位 `mapParent`、`minDelay`、`maxDelay`、`raidWaveDef`，加上基底的 `enableTick`、`state`。
- 重複與冷卻：每次呼叫都是新任務，波次依 `NumTimesCalledRaidWave(def)` 升級，超過波次表後從 `repeatWaveStartIndex` 起以世界種子固定挑選。全域冷卻 `RaidWaveWorker.GlobalCooldownTicks = 120000`，DMS 警報呼叫預設 `respectCooldown=false`，不受限。任務在全員死亡或 `mapParent.Destroyed` 時結束。
- 音樂用法：DMS 呼叫端（`BattleGroupCallUtility.TryCall`）可在呼叫後直接登記，不需 Harmony；FFF 信標只能低頻（例如每 250 tick）掃描進行中任務的 `QuestPart_RaidWaveArrives`，以 `mapParent` 過濾。

**FFF 1.7 `MapComponent_AlertCounter`**

- 公開：`AlertLevel`（0–100）、`AlertLevelPct`、`IsTriggered`、`IsAirspaceBlocked`、`HasActiveScanners`。`lastNotifyTick` 為 private。
- `Notify(amount = 20)`：未觸發時累加，達 100 即 `triggered` 並執行效果；觸發後的 `Notify` 被忽略。DMS 由 `FacilityAlarmUtility` 傳入各自的增量。
- 衰減：最後一次 `Notify` 後 **20000 tick** 才開始，每 tick 減 0.01（每 100 tick 1%）；降到 0 才清除 `triggered`。因此警戒水位與 `IsTriggered` 都不能直接當作短期音樂訊號。

**FFF 1.7 `MapComponent_FacilityLockdown`**

- 狀態 `Idle`、`Countdown`、`Locked`、`Sealed`、`Disarmed`、`Lost`；公開 `State`、`CountdownLeft`、`GraceLeft`、`BlocksPortals`、`Controller`、`SurfacePortal`、靜態 `For(Map)`。`AnyPlayerPawnInside()` 為 private。
- `Notify_AlertFull()`：`Idle → Countdown`，倒數固定 3600 tick（1 倍速 60 秒、3 倍速 20 秒）；控制器被暈眩時倒數暫停。
- `Notify_AlertFull()` 的唯一呼叫端（已查證）：`Fortified.CompFacilityLockdownController.CompTick()`，條件為 `State == Idle`、`parent.IsHashIntervalTick(Props.checkInterval)`（DMS 中控設 `checkInterval=60`）且 `MapComponent_AlertCounter.IsTriggered || AlertLevelPct >= 1f`。方法只發 ThreatBig 信件，不送任何 Signal。倒數最遲在警戒滿值後 60 tick 開始；DMS 在 `MapComponentTick` 每 tick 讀 `State`（O(1)）即可同 tick 偵測，不需 patch FFF。舊的 `WorldComponent_AlertLockdownDriver` 已標 `[Obsolete]`，只為讀舊檔保留。
- 倒數歸零 → `Locked`；控制器被駭入 → `Disarmed`；控制器被摧毀 → `Sealed`；`Locked`／`Sealed` 時地表入口被駭 → `Disarmed`。
- `Locked`／`Sealed` 且玩家在內又無救援路徑時，啟動 60000 tick 寬限，期滿宣告 `Lost`。`BlocksPortals` 在 `Locked`、`Sealed`、`Lost` 時為 true。
- MapComponent 會自動加到每張地圖，非設施地圖的狀態為 `Idle`；組件存在不代表該地圖是設施。

**守墓人（`_Source/DMS/Gravekeeper/LordJob_GravekeeperPatrol.cs`）**

- 模式 `Patrol`、`Challenge`、`Engage`、`Search`；`LordToil_GravekeeperPatrol.Mode` 為 public，`LordToilData_GravekeeperPatrol.lastSeenTick`／`lastSeenCell` 為 public 欄位。
- `Challenge`：入侵者離開視線 300 tick 後回 `Patrol`。
- `Engage`：只有在 `calmDownTicks`（預設 2500）內未看見入侵者，**且**（`engageOnSight` 或警戒值 < `engageAlertLevel`，預設 0.6）時才回 `Patrol`。警戒值衰減很慢，所以 `Engage` 可能長時間維持但沒有實際接敵。
- `Search`：獎勵房開啟後永久維持，不回巡邏。
- `_Source/DMS/ArsenalSite/ModExtension_ArsenalSite.cs` 的 `engageAlertLevel=0.6`、`calmDownTicks=2500` 控制 AI，不隨音樂門檻調整。

**主戰師、友方 DMS 部隊、封存科技追殺（已查證；「主戰師」查無定義）**

- **主戰師**：DMS、FFF 與三語語系檔都沒有名為「主戰師」的 Def、PawnGroupKind、LordJob、任務或事件。最接近的是研究 `DMS_Occultech_MainBattle`（「主戰規格」，II 級封存檔案），解鎖守墓人 `DMS_Mech_Gravekeeper`、`_Stugna`、`_Sepulturero`、`_Enterrador`、`_Velador` 與 DMSAC 的主戰規格裝備。敵對守墓人出現在軍火庫（`LordJob_GravekeeperPatrol`，由 `GenStep_ArsenalSite` 建立）與地下設施版面（`DMS_Archive`、`DMS_CommandComplex`、`DMS_Vault_Layout`）。結論：查無可用的「主戰師編組」事件來源。降級：`Black Knight Remix` 第一版不接入自動選曲，待第 11 節 D2 決定對應的實體標記。
- **友方 DMS 部隊**：派系 `DMS_Army`（Colonization Forces，一般派系，可結盟也可敵對）。友方實際進場的來源都走原版 `IncidentDefOf.RaidFriendly`（`IncidentWorker_RaidFriendly`，策略 `ImmediateAttackFriendly`；`RaidStrategyWorker_ImmediateAttack.MakeLordJob()` 對非敵對派系建立 `LordJob_AssistColony`）：
  - Royalty 許可 `DMS_Squad_RC`、`DMS_Squad_UC`、`DMS_Platoon_MT`、`DMS_Platoon_AC`、`DMS_Platoon_MD`（`RoyalTitlePermitWorker_CallAid`，私有 `CallAid()`）；
  - 通訊台請求軍事援助；
  - 說書人的友方增援事件。
  - DMS 地下 MapGeneratorDef 設 `disableCallAid`，地下設施叫不到許可援軍。`Fortified.RoyalTitlePermitWorker_MechJoin`／`PawnJoin` 讓單位加入玩家，`CallAirSupport` 是火力支援，都不算友方部隊。
  - 可用標記：地圖上 `lord.LordJob is LordJob_AssistColony && lord.faction?.def == DMS_Army`（皆為 Core 型別，不依賴 DLC）。「實際參戰」= 這些 lord 成員的 `Pawn_MindState.lastAttackTargetTick` 落在窗口內（public 欄位，成員少，輪詢成本低），或交戰 hook 記錄到 caster 屬於這些 lord。
- **封存科技追殺**：
  - 狀態：`DMS.GameComponent_OccultechSanction.PermanentHostile`（public）。排程由 FFF Huntdown 處理：`Fortified.HuntdownDef` `DMS_OccultechHunt`（`1.7/Defs/Misc/DMS_Huntdowns.xml`，派系 `DMS_Army`，`raidDelayRange` 240000–420000，`repeatWhileStaying`、`retargetPlayerHome`）。`Fortified.GameComponent_Huntdown.GameComponentTick()` 每 250 tick 檢查，到期依序呼叫 `worker.TryFireWave(map, wave)` 與 `worker.OnWaveFired(map, wave)`；下一波時間可由 public 的 `HuntdownMapTimer.NextWaveTick(HuntdownDef)` 取得。
  - `HuntdownWorker.TryFireWave(Map, HuntdownWave)` 為 public virtual，基底以 `IncidentDefOf.RaidEnemy` 發動。`DMS.HuntdownWorker_Occultech` 覆寫 `StillHunted()` 要求 `map.IsPlayerHome`，所以追殺只打玩家據點，不會出現在 DMS 站點。
  - 另一來源：研究完成時 `DMS.OccultechSanctionUtility.TryFireFleetRaid(float pointsFactor, float minPoints)`（封存級與隱匿級的 `raidOnComplete`），同樣是 `RaidEnemy`、派系 `DMS_Army`、目標 `Find.AnyPlayerHomeMap`。
  - 標記：兩個來源產生的都是一般 RaidEnemy lord，沒有任何標記，和永久敵對期間說書人派出的普通 `DMS_Army` 襲擊無法區分。兩個呼叫點都是 DMS 自有程式（`HuntdownWorker_Occultech` 可覆寫 `TryFireWave`，`TryFireFleetRaid` 可直接修改），可在呼叫前後比對目標地圖的 `lordManager.lords`，把新增且派系為 `DMS_Army` 的 lord 登記為追殺遭遇（存 lord 參照）。不需 Harmony。

**地圖與其他**

- DMS 地下設施皆使用專屬 `MapGeneratorDef`（`isUnderground`）：`DMS_MilitaryVault`、`DMS_UndergroundHall`、`DMS_DataArchive`、`DMS_CommandComplex`。`Map.generatorDef` 會寫入存檔；地表站點的 `Site.parts` 也會存檔。
- `LoadFolders.xml` 載入根目錄 `/` 與 `1.7/NewContent`；根目錄的 `Sounds/DMS_Music/` 已會被載入為 AudioClip。新增 defs 可放在 `1.7/NewContent/Defs/Misc/Music/`。
- `_Source/DMS/DMS.csproj` 目標為 `net10.0`，輸出至 `1.7/Assemblies`，直接參考遊戲與 Fortified 組件。
- `_Source/DMS/DMS_Mod.cs` 已有 `DMS_Settings`，可加入音樂開關。
- DMS 有 Multiplayer 同步慣例：純視覺亂數不使用 `Verse.Rand`（`_Source/DMS/Rendering/CompSingularityGraphic.cs`）。音樂端邏輯比照辦理。
- DMSE（`_Dead-Man-Switch-Expedition`）有 `DMSE_SongA`（`commonality=0`）並 patch 進 `OrbitalRelax`；FFF 有 `CompUseEffect_PlaySong` 會呼叫 `ForcePlaySong`，DMS 本體目前未使用。

### 2.3 查證結案紀錄

第一輪已結案：sequence worker 計時單位與中斷規則、`DangerMusicMode` 範圍、原版／DLC 音樂仲裁、守墓人狀態讀取方式、FFF 警戒與封鎖公開介面、SongDef 音檔路徑規則。

第二輪（2026-10-15）以 1.7 原版 `Assembly-CSharp`、FFF 1.7 `Fortified.dll`、DMS 原始碼與 Multiplayer 1.6 組件查證以下六項，結論已移到 2.1／2.2：

| 項目 | 狀態 | 結論位置 | 摘要 |
|---|---|---|---|
| FFF 限時站點增援倒數、停止、重複波次、存檔欄位；`Notify_AlertFull()` 呼叫端 | 已查證 | 2.2「限時站點增援」「RaidWave」「`MapComponent_FacilityLockdown`」 | 地表站點用原版 `TimedDetectionRaids`，`TicksLeftToSendRaids` 可讀，地圖存在期間每 18–24 小時重複；設施 QRF 為 `QuestPart_RaidWaveArrives`；`Notify_AlertFull()` 只由 `CompFacilityLockdownController.CompTick()` 呼叫 |
| Boss 生成路徑、延遲倒數、投放到 spawn 的時點 | 已查證 | 2.2「Boss 生成路徑」 | 四個入口都經 `BossgroupWorker.Resolve()`；預計抵達可由 `EnableTick + minDelay` 與 2500 hash 邊界推算；spawn 比抵達訊號晚 230–310 tick；玩家離開站點時改投玩家據點；`isBoss` 也涵蓋一般編組單位 |
| 交戰事件的窄範圍 hook | 已查證 | 2.1「交戰事件 hook 候選」 | 採用 `Verb.TryCastNextBurstShot` prefix；`PostApplyDamage` 作為日後補強 |
| 外部 `ForcePlaySong` 偵測 | 已查證 | 2.1「強制播放」 | postfix 可行；以 `IsPlaying && songWasForced` 判斷是否仍在強制播放；強制期間不可主動呼叫 `CheckTransitions()` |
| 主戰師編組、友方 DMS 部隊、封存科技追殺 | 友方與追殺已查證；主戰師查無 | 2.2「主戰師、友方 DMS 部隊、封存科技追殺」 | 友方以 `DMS_Army` 的 `LordJob_AssistColony` 辨識；追殺需在 DMS 自有呼叫點登記 lord；「主戰師」無對應定義，降級並列入 D2 |
| Multiplayer 下原版選曲亂數 | 以 MP 1.6 查證；1.7 無法查證 | 2.1「Multiplayer」 | 原版音樂在幀流程、同步亂數 Push/Pop 之外，不參與同步；本機 MP 只支援 1.5／1.6 |

仍未解決：

- 「主戰師」的實體定義：待第 11 節 D2。決策前 `Black Knight Remix` 不接入自動選曲。
- Multiplayer 1.7 版的行為：發布後重查 `AsyncTimeComp` 的 Push/Pop 與 patch 清單。降級：沿用 1.6 的結論與 5.2 的限制（不在 tick 內呼叫音樂 API、窗口在 `MapComponentTick` 內換算）。
- 附帶發現（不屬本計畫範圍，列為後續）：`QuestPart_BossgroupArrives.QuestPartTick()` 在 `mapParent.Map` 為 null（玩家已離開站點但站點仍在）時，會在 `ShouldSummonNow(null)` 拋 NullReferenceException，由 `Quest.QuestTick()` 每 2500 tick 記錄一次錯誤，直到 `maxDelay` 強制投放到玩家據點。DMS 子類別覆寫 `QuestPartTick()` 時可一併防護。

## 3. 狀態模型

### 3.1 八種狀態

| 建議 defName | 優先值 | 進入條件 | 退出條件 |
|---|---:|---|---|
| `DMS_InstallationExplore` | 110 | DMS 地表站點、有玩家單位、無更高狀態 | 離開／失去範圍或有更高狀態 |
| `DMS_FacilityExplore` | 120 | DMS 地下設施、有玩家單位、無更高狀態 | 離開／失去範圍或有更高狀態 |
| `DMS_Aftermath` | 140 | 一次有效危機結束，在餘波窗口內且無其他威脅 | 單次播放完畢、新威脅或離開 |
| `DMS_SecurityAlert` | 160 | 近期警戒上升、盤查、搜索初期、接近中的 Boss 或即將到來的增援 | 原因解除且通過緩衝，或被更高狀態接管 |
| `DMS_FacilityTrapped` | 180 | 設施 `Locked`／`Sealed`、玩家仍在內、無交戰 | `Disarmed`、撤出、`Lost` 或新交戰 |
| `DMS_InstallationCombat` | 220 | DMS 自有交戰訊號成立（見 4.2） | 威脅與停戰緩衝都結束 |
| `DMS_BossCombat` | 240 | 已登記且啟動的敵對 Boss 在當前地圖構成威脅 | Boss 失去威脅資格，或封鎖倒數接管 |
| `DMS_LockdownCountdown` | 280 | 當前觀看的設施地圖處於 `Countdown`、玩家仍在內（地表觀看情況見第 11 節 D1） | `Disarmed`、`Locked`／`Sealed`、`Lost` 或撤出 |

與 DLC 的相對位置（見 2.1 表）：

- DMS 探索（110／120）高於 `HorrorRelax`（100）與所有 Odyssey 項目，低於 `HorrorTension`（150）。`HorrorTension` 由明確的 Anomaly 事件觸發，DMS 探索與餘波讓位，符合「明確特殊事件進行時讓位」原則。
- `HorrorMonolithAdvanced`（175）與 `HorrorCombat`（200）、`OrbitalCombat`（75）不可中斷。若它們先開始，DMS 警戒／受困／交戰／Boss 必須等它們結束；此為原版限制，接受並記入驗收。
- `DMS_LockdownCountdown` 設 `overridesInterruptions=true`：刻意切斷 `OrbitalCombat`、`HorrorMonolithAdvanced`、`HorrorCombat`，理由是倒數最短只有 20 秒實際時間。優先值 280 仍低於 600／999，不會越過 `CorpseAttack` 與 `MetalHell`。
- 其他 DMS 狀態不設 `overridesInterruptions`。

優先值為初始方案；正式設定前對照實際已載入的 transition（含第三方模組）再確認。

### 3.2 判斷順序

每次刷新以同一份 snapshot，從高至低選擇唯一 DMS 狀態：

1. 玩家設定停用、正在觀看世界地圖（`WorldRendererUtility.WorldSelected`）、不在有效觀看地圖、外部強制曲播放中，或應讓位給原版特殊事件時，結果為 `None`。
2. 有封鎖倒數：`LockdownCountdown`。
3. 有有效 Boss 威脅：`BossCombat`。
4. 有交戰或停戰緩衝：`InstallationCombat`。
5. 設施仍受困：`FacilityTrapped`。
6. 有警戒原因或警戒緩衝：`SecurityAlert`。
7. 在餘波窗口內且本客戶端尚未播放過該危機的餘波：`Aftermath`。
8. 依地圖類型：`FacilityExplore` 或 `InstallationExplore`。
9. 其他情況：`None`，交還原版。

一般殖民地可因已登記的 DMS 遭遇（Boss、封存科技追殺襲擊）暫時進入警戒／Boss／交戰／餘波，但結束後回 `None`，不改成 DMS 站點探索。誘餌建築可在基地建造、Boss 也可能改投玩家據點，追殺只打玩家據點（見 2.2），所以這條路徑是常態而非例外。

### 3.3 時間與門檻

| 項目 | 初始值 | 計時來源 | 說明 |
|---|---:|---|---|
| 警戒：近期上升窗口 | 2500 ticks | game ticks | `MapComponentTick` 比對 `AlertLevel`，有上升即記錄 tick；窗口內為警戒原因 |
| 警戒水位 | — | — | 水位與 `IsTriggered` 不單獨構成警戒原因（衰減需 20000+ tick），僅供診斷顯示 |
| 守墓人盤查 | `Challenge` 模式 | 即時 | 警戒原因 |
| 守墓人搜索 | `Search` 開始後 2500 ticks | game ticks | 窗口內為警戒原因；之後只有近期接敵才影響音樂 |
| 守墓人接敵（音樂用） | `lastSeenTick` 在 600 ticks 內，且模式為 `Engage`／`Search` | game ticks | 交戰原因；獨立於 AI 的 `calmDownTicks`，不改 AI |
| 增援接近警戒 | 剩餘 1800 ticks | game ticks | 1 倍速約 30 秒。地表站點讀 `TimedDetectionRaids.TicksLeftToSendRaids`；設施 QRF 讀 `QuestPart_RaidWaveArrives` 的預計抵達 tick（見 2.2） |
| Boss 接近警戒 | 預計抵達前 1800 ticks | game ticks | 預計抵達 = `EnableTick + minDelay` 之後第一個 2500 hash 邊界（見 2.2）；`ShouldSummonNow` 為 false（訪客等阻擋）時不編造時間，維持 Pending |
| 交戰紀錄窗口 | 最後交戰後 600 ticks | game ticks | `Verb.TryCastNextBurstShot` prefix 寫入的最後交戰 tick；窗口內為交戰原因，之後才開始停戰緩衝 |
| 停戰緩衝 | 12 秒 | 未暫停的實際時間 | 從最後有效交戰／危險解除起算 |
| 警戒緩衝 | 15 秒 | 未暫停的實際時間 | 所有原因都解除後才起算 |
| 餘波窗口 | 危機結束後 2500 ticks 內可開始 | game ticks | 曲長 20–35 秒；每個危機每個客戶端最多一次 |
| 遊戲端原因更新 | 每 60 ticks | `MapComponentTick` | 加上關鍵事件立即標髒；實際成本再量測 |
| 淡入淡出 | 見第 6 節 | sequence 欄位 | 對應 `fadeoutLastSongDuration`、`overrideFadeout`、`fadeInDuration` |

計時原則：

- 遊戲事件（警戒上升、接敵、倒數、餘波窗口）使用 game ticks，由 `MapComponentTick` 在同步流程內更新，可存檔，Multiplayer 下各端一致。
- 聽感緩衝使用「未暫停的實際時間」：由客戶端在 `GameComponentUpdate` 累加 `Time.deltaTime`，暫停不累加，不存檔。如此暫停不會讓緩衝提前過期，高速時也不會把緩衝縮成一瞬間。
- 讀檔後客戶端狀態從零開始；遊戲端時間戳由存檔恢復。
- Multiplayer 的非同步時間下，客戶端幀流程的 `TicksGame` 不一定是觀看地圖的時間（見 2.1）。所有 game-tick 窗口都在 `MapComponentTick` 內換算成布林值或剩餘 tick 寫入 snapshot，客戶端不自行拿 `TicksGame` 相減。

## 4. 地圖與事件範圍

### 4.1 地圖分類

新增 `ModExtension_DMSMusicContext`，描述地表／地下情境及可選曲庫風格。此名稱為新增設計。

- 地表：掛在 DMS `SitePartDef`；由 `map.Parent is Site` 的 `parts` 推導。多個 site parts 共存時按明確規則合併（例如取最高優先的 context），不能依遍歷順序隨機決定。
- 地下：掛在 DMS 地下 `MapGeneratorDef`，由 `map.generatorDef` 推導；不以洞穴、口袋地圖、建築名稱或陣營推定為 DMS。
- context 一律**推導並快取，不存檔**。`Site.parts` 與 `Map.generatorDef` 都已在存檔中，舊存檔自動可識別，不需生成時寫入或白名單補建；無法推導時交還原版。
- 不因玩家在基地建造一座 DMS 電腦或封鎖裝置，就把基地變成設施地圖。
- 不要求玩家必須有未倒地殖民者；玩家控制機兵與倒地玩家單位仍需納入觀看地圖的有效存在判斷，細節於介面盤點定案。

至少覆蓋：餘械中繼站、海盜組裝廠、機場、軍火庫舊址、儲存庫站、維修站、作戰基地、荒廢站點、發射場、SAGE 節點、石碑密鑰撞擊點、石碑封藏庫，以及地下儲存庫（`DMS_MilitaryVault`）、地下大廳（`DMS_UndergroundHall`）、檔案庫（`DMS_DataArchive`）、指揮設施（`DMS_CommandComplex`）。

### 4.2 有效交戰

- **不使用原版危險訊號判定 DMS 交戰**：`DangerMusicMode` 是全域的；單圖 `DangerWatcher` 會因守軍戰力總和 > 400 在一看見守軍時就常駐 High。DMS 的 transition defs 不設 `dangerRequirement`。
- DMS 自有交戰訊號只接受涉及玩家及敵對戰鬥單位的攻擊、有效射擊或戰鬥行為；以 `Verb.TryCastNextBurstShot` prefix 在模擬流程中記錄地圖的最後交戰 tick（過濾規則見 2.1）。含未命中的射擊與砲塔攻擊；只在有 DMS context 或已登記 DMS 遭遇的地圖記錄。
- 守墓人：`Engage`／`Search` 模式且 600 tick 內看見入侵者時為交戰；`Engage` 但沒有近期接敵（被高警戒值維持）不算交戰。
- 環境傷害、疾病、自傷、友軍誤傷、玩家射擊無敵人的普通物件，不單獨觸發交戰音樂。
- 有敵人駐守、房間未探索、整張地圖藏有睡眠機體，不單獨視為交戰。
- 敵方迫擊砲等經 Verb 的砲擊由上述 hook 涵蓋，可保持交戰；不經 Verb 的轟炸或持續攻擊效果第一版只作為警戒原因，實測不足再以 `Pawn_HealthTracker.PostApplyDamage` 補強（見 2.1）。單純空域封鎖屬警戒原因。
- 友方 DMS 部隊（`DMS_Army` 的 `LordJob_AssistColony` 成員）開火不單獨構成交戰，只在交戰成立時作為選曲用的「友方參戰」標記（見 2.2、7.1）。

### 4.3 Boss 生命週期

將遭遇拆成 `Pending → Approaching → Active → Resolved`，取消則為 `Cancelled`。

- 生成時（`QuestNode_Root_BossgroupFactionExposed.RunInt()`）登記唯一遭遇 ID、召喚地圖、Boss 實體參照；護衛另記，不當成 Boss。召喚地圖只是暫定目標。
- Pending 不播放 Boss 曲；Approaching（預計抵達前 1800 tick，由 `QuestPart_BossgroupArrivesWithMusic` 覆寫 `QuestPartTick()` 發布，見 2.2）只提供警戒原因。
- 抵達訊號（`QuestPart_BossgroupArrivesWithMusic.Complete()`，在 `base.Complete()` 之後、清空 `bosses` 之前）把目標地圖更新為 Boss 的 `MapHeld`：玩家已離開站點時會改投玩家據點。此時 Boss 仍在空投艙內，約 230–310 tick 後 `Spawned` 才轉 Active。
- Active 要求敵對、已啟動、未死亡、未失能且仍位於有效遭遇地圖。
- 死亡：重用 `DMS_BossKilled` 訊號（以 MAP 過濾），並以已登記的 Boss 參照過濾：`isBoss` 也涵蓋一般編組中的玳瑁、駭蜥與推土機（見 2.2）。比照 `CompBossKillRelay.requireAllBossesDead` 只在最後一架登記 Boss 失去資格時解除。
- 離場、轉友方、永久失能、遭遇取消（站點被摧毀時任務以 `mapParent.Destroyed` 結束）：訊號不涵蓋，於 `MapComponentTick` 輪詢。
- 暫時 EMP／短暫暈眩不當作永久擊敗。仍有護衛交火則回一般交戰。
- 不能只用 PawnKind 或任務是否存在判斷；不能依賴會被清空的 `QuestPart_Bossgroup.bosses`。
- 封存科技追殺襲擊沿用同一套遭遇登記（以 lord 參照代替 Boss 參照，見 2.2），只提供選曲標記與交戰地圖旗標，不進入 `BossCombat`。
- 讀檔恢復優先使用已保存參照；舊存檔僅採可信任務／生成標記補登記，不能把友方同型機體補成 Boss。

## 5. 程式架構與檔案規劃

以下為建議新增／修改位置，實作時按已確認的 hook 做必要調整。

| 位置 | 職責 |
|---|---|
| `_Source/DMS/Music/DMSMusicState.cs` | enum、snapshot、原因旗標、純狀態選擇規則 |
| `_Source/DMS/Music/MapComponent_DMSMusicState.cs` | 遊戲端：事件登記、警戒上升與接敵 tick、Boss 遭遇、危機起訖、存檔與恢復 |
| `_Source/DMS/Music/GameComponent_DMSMusic.cs` | 客戶端：觀看地圖變更偵測、未暫停時間累計、聽感緩衝、餘波播放紀錄；不存檔 |
| `_Source/DMS/Music/DMSMusicContextUtility.cs` | 由 `Site.parts`／`Map.generatorDef` 推導 context 並快取 |
| `_Source/DMS/Music/ModExtension_DMSMusicContext.cs` | context 與曲庫設定 |
| `_Source/DMS/Music/MusicTransition_DMS.cs` | 原版 transition 基底及八種 worker；只讀 snapshot |
| `_Source/DMS/Music/MusicSequenceWorker_DMS.cs` | 覆寫 `ShouldEnd()`（context 不符時立即結束）與 `SelectSong()`（依 context／變奏選曲、空曲庫保護） |
| `_Source/DMS/Music/DMSMusicEventBridge.cs` | 事件接入。Harmony：`Verb.TryCastNextBurstShot` prefix（交戰）、`MusicManagerPlay.ForcePlaySong` postfix（強制播放開始）。輪詢：`TimedDetectionRaids`（站點增援）、`QuestPart_RaidWaveArrives`（FFF 信標波次）、`MapComponent_FacilityLockdown.State`、`MapComponent_AlertCounter.AlertLevel`、守墓人 `Mode`／`lastSeenTick`、友方 `LordJob_AssistColony` 成員的 `lastAttackTargetTick`、私有 `songWasForced`（FieldRef） |
| `_Source/DMS/QuestNode_Root_BossgroupFactionExposed.cs`、`QuestPart_BossgroupArrivesWithMusic.cs` | 生成時登記 Boss 遭遇；覆寫 `QuestPartTick()` 發布預計抵達 tick；`Complete()` 更新實際投放地圖並停止呼叫 `ForcePlaySong` |
| `_Source/DMS/CompAlertEffector_CallBattleGroup.cs`（`BattleGroupCallUtility.TryCall`） | DMS 警報呼叫 QRF 後直接登記增援 |
| `_Source/DMS/Occultech/HuntdownWorker_Occultech.cs`、`OccultechSanctionUtility.cs` | 覆寫 `TryFireWave()`、修改 `TryFireFleetRaid()`：呼叫前後比對 lord，登記追殺遭遇 |
| `_Source/DMS/Music/DMSMusicDebugActions.cs` | 狀態顯示、事件注入、清除及診斷 |
| `_Source/DMS/DMS_Mod.cs` | `DMS_Settings` 新增情境音樂開關 |
| `1.7/NewContent/Defs/Misc/Music/Music_Transitions.xml` | 八個 transition definitions |
| `1.7/NewContent/Defs/Misc/Music/Music_Sequences.xml` | 八個 sequence definitions，`workerType` 指向 `MusicSequenceWorker_DMS` |
| `1.7/NewContent/Defs/Misc/Music/Music_Songs.xml` | 正式 SongDef，全部 `playOnMap=false` |
| `Sounds/DMS_Music/` | 播放用音檔（根目錄已由 `/` 載入；`clipPath` 相對於 `Sounds/`） |
| `1.7/NewContent/Languages/*/Keyed/` | 設定開關與診斷文字（English、ChineseSimplified、ChineseTraditional） |
| 既有站點 `SitePartDef`／地下 `MapGeneratorDef` | 加上 context，保留原本生成、AI 與獎勵設定 |

### 5.1 Snapshot 與儲存資料

建議 snapshot 含：地圖識別、context、選定狀態、原因旗標、警戒上升 tick、守墓人接敵 tick、Boss 有效數、封鎖狀態、倒數、增援／Boss 預計抵達剩餘 tick、選曲標記（追殺、友方參戰）、緩衝剩餘時間、餘波資格、讓位原因。所有 tick 窗口以 `MapComponentTick` 換算後的布林值或剩餘 tick 呈現（見 3.3）。

- **存檔（`MapComponent_DMSMusicState`，遊戲端）**：已登記遭遇（Boss 參照、追殺 lord 參照、增援記錄）、危機 ID 與結束 tick、警戒上升 tick、最後交戰 tick、上次看到的 `RaidsSentCount`。全部在同步的 tick 流程內寫入。
- **不存檔（`GameComponent_DMSMusic`，客戶端）**：context 快取、聽感緩衝、未暫停時間累計、本客戶端已播放的餘波危機 ID、上一個觀看地圖、外部強制曲記錄（曲目與開始時間）。音樂端不得改寫遊戲端狀態，也不使用 `Verse.Rand` 做純播放用途的決策。
- 組件存在不代表該地圖為 DMS。所有新欄位必須有安全預設；讀檔後清理 null／destroyed／跨地圖參照，重算狀態。
- 讀檔時若仍在餘波窗口內，該餘波可能再播一次；窗口只有 2500 ticks，接受此行為。

### 5.2 Worker 責任

- 僅取得目前觀看地圖的 snapshot，並比對對應狀態。
- 不在 `IsTransitionSatisfied()` 裡掃描所有任務、生成事件、消費餘波或修改狀態。暫停時若停在 60 的倍數 tick，原版會每幀呼叫它，必須是常數時間的快取讀取。
- 不使用 `dangerRequirement`；DMS 與原版訊號的合併只在 snapshot 內處理。
- 八個 worker 共用同一份 snapshot，不能各算各的優先序。
- 即使 Boss 發生於普通基地，也能以已登記遭遇選擇 Boss 曲。
- `GameComponent_DMSMusic` 在以下時機標髒 snapshot，並於 `MusicManagerPlay.State != Fadeout` 時主動呼叫 public 的 `CheckTransitions()`，不等 60 tick（暫停中也生效）：
  - 觀看地圖或世界地圖切換。
  - 遊戲端在觀看地圖上標記了關鍵事件（封鎖倒數開始、Boss 啟動）。
  - DMS 序列淡出完成（`State` 由 `Fadeout` 回到 `Normal`）且 snapshot 仍有 DMS 狀態，避免原版在空檔先開一首一般曲。
  - 外部強制曲結束（`IsPlaying && songWasForced` 由 true 轉 false 之後的幀）且 snapshot 有 DMS 狀態。
- 強制曲播放中（`IsPlaying && songWasForced`）不主動呼叫 `CheckTransitions()`，`Fadeout`→`Normal` 的判斷也暫停：此期間原版每幀把狀態重設為 `Normal`，主動檢查會讓 transition 被標為觸發卻不播放（見 2.1「強制播放」）。
- `CheckTransitions()` 只從 `GameComponentUpdate()`（幀更新）呼叫，不在 tick 內呼叫：`StartSequence` 的洗牌使用 `Verse.Rand`，在模擬流程中消耗亂數會造成 Multiplayer 不同步（已以 MP 1.6 查證，見 2.1）。遊戲端只負責標記，由客戶端在下一幀處理。`GameComponentUpdate()` 在同一幀內早於 `MusicUpdate()` 執行。

## 6. Sequence、中斷與相容策略

| 狀態 | 優先值 | 循環 | `minTimeToPlay` | `fadeoutLastSongDuration`（進入時淡出前曲） | `overrideFadeout`（離開） | 其他 |
|---|---:|---|---:|---:|---:|---|
| 地表探索 | 110 | 是 | 30 | 5 | 5 | |
| 地下探索 | 120 | 是 | 30 | 5 | 5 | |
| 餘波 | 140 | 否 | — | 3 | 3 | `useTransitionForLifetime=false` |
| 警戒 | 160 | 是 | 15 | 3 | 4 | |
| 受困 | 180 | 是 | 10 | 3 | 3 | |
| 一般交戰 | 220 | 是 | — | 2 | 4 | `pausedVolumeFactor=0.5` |
| Boss 交戰 | 240 | 是 | — | 2 | 4 | `pausedVolumeFactor=0.5` |
| 封鎖倒數 | 280 | 是 | 0 | 1 | 2 | `overridesInterruptions=true` |

數值為初始方案；所有 DMS sequence 保持 `canBeInterrupted=true`，`workerType` 為 `MusicSequenceWorker_DMS`。

**升級與降級的實際行為**

- 升級（例如探索 → 交戰）：下一次檢查即觸發，舊曲以新 sequence 的 `fadeoutLastSongDuration` 淡出後播新曲。未設定時為 10 秒，所以每個 DMS sequence 都必須明確設定。
- 降級（例如 Boss → 交戰、交戰 → 警戒、交戰 → 餘波）：較低優先的狀態無法直接取代。需等目前 transition 不成立且 `MinTimeSatisfied()`，以 `overrideFadeout` 淡出，淡出完成後由原版下一次 60 tick 檢查或 `GameComponent_DMSMusic` 的主動檢查接手（見 5.2）。降級必然有「淡出時間」的空檔，驗收以此為預期；需確認空檔中不會插入原版一般曲。
- 同層切換（地下 120 → 地表 110）同樣屬降級；由 `MusicSequenceWorker_DMS.ShouldEnd()` 在 context 不符時直接結束，不受 `minTimeToPlay` 鎖住。地表 → 地下屬升級，立即切換。
- 離開地圖或切到世界地圖：`ShouldEnd()` 在觀看地圖已非本序列所屬 context 時回傳 true，不需要 Harmony。`ShouldEnd()` 的判斷必須與 transition 不成立的條件一致，否則淡出後會被同一 transition 重新觸發。

**選曲**

- transition 對應的 sequence 固定；地表／地下交戰曲庫與三首變奏由 `MusicSequenceWorker_DMS.SelectSong()` 依 context 與遭遇標記選擇，不拆成多個互斥 transition（拆分會在變奏降級時產生淡出空檔）。
- 選曲只在開歌與循環時發生，同一遭遇天然穩定；中途條件改變要等下一首才生效。
- 同一狀態下從地表切到地下（例如兩邊都在交戰）不會重開歌；如需改用地下曲庫，等下一輪循環。
- 曲庫為空時 `SelectSong()` 回傳安全的後備曲或讓 `ShouldEnd()` 結束序列，不得進入原版的取餘數運算。
- 選曲亂數使用不影響遊戲同步的來源。MP 1.6 下幀流程的 `Verse.Rand` 已不影響同步（見 2.1），但 MP 1.7 未查證，仍維持此規則。

**其他規則**

- 持續狀態使用 `useTransitionForLifetime=true`（預設值，仍明確寫出）。
- 不對任何 DMS 序列設定 `endOnNoDanger`／`endOnDanger`，避免原版全域危險旗標干擾。
- 同狀態刷新不重新開歌；重複的增援或受擊訊號只延長狀態，不重啟歌曲。
- 餘波：原版沒有序列完成回呼。`GameComponent_DMSMusic` 觀察到 `MusicManagerPlay.TriggeredTransition` 為 `DMS_Aftermath` 時，即把該危機記為本客戶端已播放；transition 隨即不成立，但 `useTransitionForLifetime=false` 會讓單曲播完，期間可被任何更高狀態中斷。
- 強制播放：新系統停止呼叫 `ForcePlaySong`。外部來源（FFF `CompUseEffect_PlaySong`、原版 Anomaly `CompVoidNode` 等）強制播放時，以 `ForcePlaySong` 的 Harmony postfix 取得開始事件，以 `IsPlaying && songWasForced` 判斷是否仍在播放（見 2.1）；期間所有 DMS worker 回傳 false，讓強制曲播完（原版此時不執行 `UpdateSequenceState()`，`ShouldEnd()` 不會被呼叫）。DMS sequence 的 `fadeoutLastSongDuration` 必須大於 0：若為 0，強制曲期間一旦有 transition 觸發，`StartSequence()` 會直接切掉強制曲。
- Anomaly／Odyssey 未啟用時不得引用其限定 defs 或直接載入其限定型別；DMS 不需要讀取 DLC transition，只依優先值與中斷規則共存。
- 不修改原版 defs 的全域優先值；第三方音樂模組以自身優先序競爭。

## 7. 音樂資產交付

### 7.1 現有音樂盤點

2026-10-15 盤點：`Sounds/DMS_Music/` 內共有 **12 首 MP3**（合計約 59 MB，單檔 3.7–7.3 MB），檔名已全部改為英文，以下為實際檔名。尚未建立任何 SongDef，也尚未進行聆聽、時長、響度、循環接點或遊戲解碼驗收。英文檔名保留原拼寫；`clipPath` 須與檔名大小寫完全一致；建議 defName 為未建立的實作名稱。

| 現有檔案（相對模組根目錄） | `clipPath` | 建議 SongDef.defName | 計畫用途 | 接入方式與待辦 |
|---|---|---|---|---|
| `Sounds/DMS_Music/Abandoned Radar Station.mp3` | `DMS_Music/Abandoned Radar Station` | `DMS_Music_AbandonedRadarStation` | `DMS_InstallationExplore` 主曲 | 地表探索；驗證長時間聆聽與循環 |
| `Sounds/DMS_Music/Sealed Machinery.mp3` | `DMS_Music/Sealed Machinery` | `DMS_Music_SealedMachinery` | `DMS_FacilityExplore` 主曲；`DMS_FacilityTrapped` 初期共用 | 受困可先使用同曲；若氣氛不足再輸出較暗變奏 |
| `Sounds/DMS_Music/Camera caught ya.mp3` | `DMS_Music/Camera caught ya` | `DMS_Music_CameraCaughtYa` | `DMS_SecurityAlert` 主曲 | 警戒、盤查、搜索、增援接近；優先評估能否製作封鎖倒數變奏 |
| `Sounds/DMS_Music/Garrisions in Action.mp3` | `DMS_Music/Garrisions in Action` | `DMS_Music_GarrisionsInAction` | `DMS_InstallationCombat` 地表曲庫 | 保留原檔名 `Garrisions`；不在本次計畫中更名 |
| `Sounds/DMS_Music/Guns Up.mp3` | `DMS_Music/Guns Up` | `DMS_Music_GunsUp` | `DMS_InstallationCombat` 地下曲庫 | 由 `SelectSong()` 依 context 選曲；注意大寫 `U` |
| `Sounds/DMS_Music/Servo in precise.mp3` | `DMS_Music/Servo in precise` | `DMS_Music_ServoInPrecise` | `DMS_BossCombat` 主曲 | 通用 Boss 主題；同場多 Boss 不重複開歌 |
| `Sounds/DMS_Music/Dormant automata.mp3` | `DMS_Music/Dormant automata` | `DMS_Music_DormantAutomata` | 一般交戰的伏擊開場候選／戰鬥備選 | 第一版不另增第九個狀態；取得可靠伏擊事件後，評估以 `nextSequence` 串接一次性開場，否則保留為備選 |
| `Sounds/DMS_Music/Obsolete Systems.mp3` | `DMS_Music/Obsolete Systems` | `DMS_Music_ObsoleteSystems` | 系統甦醒／封存揭露事件曲預留 | 不作為平靜探索或勝利餘波的預設；需另確認一次性事件與播放仲裁後啟用 |
| `Sounds/DMS_Music/Sage in the war room.mp3` | `DMS_Music/Sage in the war room` | `DMS_Music_SageInTheWarRoom` | SAGE 節點／地下指揮設施候選曲 | 聆聽後再選定曲庫，未驗收前不加入任何播放清單 |
| `Sounds/DMS_Music/Black Knight Remix.mp3` | `DMS_Music/Black Knight Remix` | `DMS_Music_BlackKnight` | 主戰師相關遭遇的交戰／Boss 曲庫變奏 | 已查證：專案內沒有「主戰師」的 Def／事件來源（見 2.2）。D2 決定對應的實體標記前不接入自動選曲；不以顯示名稱或曲名猜測 |
| `Sounds/DMS_Music/Allied Marching Precision.mp3` | `DMS_Music/Allied Marching Precision` | `DMS_Music_MarchingPrecision` | 友方 DMS 部隊實際參戰時的交戰變奏 | 友方抵達本身不算交戰。標記：地圖上有 `DMS_Army` 的 `LordJob_AssistColony`，且其成員的 `lastAttackTargetTick` 在窗口內（見 2.2）；地下設施叫不到許可援軍 |
| `Sounds/DMS_Music/Occultech Huntdown.mp3` | `DMS_Music/Occultech Huntdown` | `DMS_Music_OccultechHuntdown` | 封存科技追殺的高壓交戰變奏 | 僅對已登記的追殺 lord（`HuntdownWorker_Occultech.TryFireWave`／`TryFireFleetRaid` 呼叫前後比對，見 2.2）在當前地圖構成有效戰鬥時啟用；只會發生在玩家據點。`PermanentHostile` 與研究狀態不單獨觸發，普通 `DMS_Army` 襲擊不使用 |

新增三首變奏的用途依曲名制定，均待聆聽驗證；事件介面已查證（見 2.2），其中主戰師待 D2。不轉為任意傷亡、血量或威脅點數門檻。

SongDef 範本（所有 DMS 曲目都必須 `playOnMap=false`，避免進入原版一般輪播）：

```xml
<SongDef>
  <defName>DMS_Music_AbandonedRadarStation</defName>
  <label>Abandoned Radar Station</label>
  <clipPath>DMS_Music/Abandoned Radar Station</clipPath>
  <playOnMap>false</playOnMap>
  <volume>1</volume>
</SongDef>
```

#### 新增變奏的選曲仲裁

- 三首沿用既有八狀態；由 `MusicSequenceWorker_DMS.SelectSong()` 依「遭遇標記＋參戰編組＋友敵關係」選曲，不新增全域常駐音樂狀態，也不新增 transition。
- 封鎖倒數及原版高優先特殊事件仍依既有規則接管，不因使用追殺曲而提高狀態優先值。
- 有效 Boss 戰優先維持 Boss 狀態；只有 Boss 遭遇本身帶有對應主戰師／追殺標記，才使用該 Boss 變奏，否則使用 `Servo in precise`。一般友軍到場不覆蓋 Boss 主題。
- 一般交戰同時符合多種變奏時，初始順序為「已標記追殺遭遇 → 已標記主戰師遭遇（D2 決策前不成立）→ 已投入交戰的友方 DMS 部隊 → 地表／地下預設」。該順序屬設計建議，實測後可調整。
- 變奏不單獨啟動交戰狀態；先確認有效交戰，再決定用曲。選曲只在開歌與循環時發生，不會因一名友軍進出反覆切歌。

### 7.2 主要曲庫覆蓋與缺口

| 曲庫 | 主要用途 | 現有覆蓋與剩餘工作 |
|---|---|---|
| Explore | 地表／地下探索 | 已有 `Abandoned Radar Station`、`Sealed Machinery`；分地表／地下選曲 |
| Alert | 盤查、搜索、接近事件 | 已有 `Camera caught ya`；驗收警戒強度與循環 |
| Combat | 站點與地下交戰 | 已有 `Garrisions in Action`、`Guns Up`；`Dormant automata` 為事件／備選；`Black Knight Remix`、`Allied Marching Precision`、`Occultech Huntdown` 為具條件的遭遇變奏 |
| Boss | 重型機兵與無人機母艦 | 已有 `Servo in precise`；主戰師／追殺標記的 Boss 遭遇可選對應變奏，不將友方曲視為敵對 Boss 預設 |
| Lockdown | 封鎖倒數 | 尚無專曲；可由 `Camera caught ya` 製作急迫變奏，或另製新曲 |
| Aftermath | 危機後餘波 | 尚無短曲；需製作 20–35 秒短段，或由探索曲裁切合適片段 |

受困使用 Explore 的低沉變奏或同一首暗色探索曲；不額外要求第七套主曲庫。

`Obsolete Systems` 與 `Sage in the war room` 納入資產清單，但不為了用完十二首曲目而改變第一版八狀態架構。新增三首屬既有曲庫變奏，尚未填補封鎖專曲與餘波短曲的缺口。

### 7.3 音檔轉接與驗收流程

1. 保留 `Sounds/DMS_Music/` 十二首原始檔，記錄實際時長；本次文件更新不移動或轉碼音檔。
2. 播放檔就放在根目錄 `Sounds/DMS_Music/`（DMS 2 只支援 1.7，根目錄由 `/` 載入）。若日後需要依版本隔離，改放 `1.7/NewContent/Sounds/DMS_Music/`，`clipPath` 不變。
3. 若需要裁切、變奏或重新編碼，輸出新檔並使用穩定英文檔名；原檔保留。
4. 建立「原始檔 → 播放檔 → SongDef → sequence → transition」對照表。
5. 所有 SongDef 設 `playOnMap=false`；以原版開發者工具確認一般輪播清單中不含 DMS 曲目。
6. 封鎖專曲與餘波短曲未完成前，將功能列為未完成；開發期可以有效曲目暫代，但不能因此通過正式資產驗收。
7. 聆聽 `Sage in the war room` 後記錄探索／警戒／交戰的適配結果；無適合位置時繼續保留，不強制播放。

- Suno 生成後需人工聆聽、裁切與音量校正；「loop-friendly」不代表輸出天然無縫。
- 確認 Suno 方案的授權允許在 Steam Workshop 公開散布，並在交付紀錄中註明。
- 決定 `Sounds/DMS_Music/`（約 59 MB，目前未納入 git）的版本控制方式。
- 使用原版支援的編碼與聲道設定，避免破音、首尾爆音、過長靜音與突兀淡出。
- 低頻需保留槍聲、警報與 UI 音效的辨識空間；以 `SongDef.volume` 統一響度。
- defs 啟用前驗證檔案存在、SongDef 可解析、曲庫非空。
- 暫無正式資產時，可在開發環境使用已查證且可用的 Core SongDef 驗證流程；不建立指向缺檔的假 SongDef，不把佔位曲誤列為正式交付。
- Boss 專屬變奏可後續擴充，第一版不為每個 Boss 建立獨立 transition。

## 8. 分階段實作流程

### Phase 0：盤點與接口確認

- [ ] 記錄目前工作樹修改，保留既有使用者變更；實作不得覆寫未提交的 AI、站點與 Boss 改動。
- [x] 查證原版音樂仲裁、sequence worker、危險訊號、強制播放與 SongDef 音檔載入規則（見 2.1）。
- [x] 查證 FFF 1.7 警戒與封鎖公開介面、守墓人狀態讀取方式（見 2.2）。
- [x] 查證第 2.3 節其餘項目，記錄符號、簽章與版本（見 2.3 結案紀錄；Multiplayer 只查到 1.6 版）。
- [ ] 列出完整站點、地下地圖及 Boss 生成路徑對照表。Boss 路徑與使用限時倒數的十個站點 SitePartDef 已列於 2.2；其餘站點（石碑密鑰撞擊點、石碑封藏庫等）的 SitePartDef 對照仍待補。
- [ ] 依第 7.1 節逐首驗收現有十二首音檔，補上時長、響度、循環與用途分類；確認 SAGE 曲目的實際風格。
- [x] 查證主戰師編組／遭遇標記、友方實際參戰判斷與封存科技追殺襲擊來源（見 2.2；主戰師查無，待 D2）。
- [x] 決定每個事件由 DMS 直接通知、讀取公開狀態或窄範圍 Harmony patch 接入（見第 5 節 `DMSMusicEventBridge` 與各 DMS 檔案列）。
- [ ] 處理第 11 節待決策事項。

完成條件：所有必要介面可定位；無法取得的倒數／事件以明確降級方案替代，不能保留假 API。

### Phase 1：純狀態規則與資料

- [ ] 實作 enum、context、snapshot、原因旗標及唯一狀態選擇器。
- [ ] 實作警戒上升窗口、守墓人接敵窗口、停戰／警戒緩衝（未暫停實際時間）、危機 ID 與餘波窗口。
- [ ] 區分遊戲端（存檔、tick）與客戶端（不存檔、實際時間）狀態；加入存檔欄位與舊檔安全預設。
- [ ] 以合成 snapshot 驗證優先序、多原因解除與 `Lost`／`Disarmed` 行為。

完成條件：相同輸入永遠產生相同狀態，不依查詢次數改變結果。

### Phase 2：地圖分類與狀態來源

- [ ] 為 DMS 地表 `SitePartDef` 與地下 `MapGeneratorDef` 掛 context，驗證推導結果與地表／地下切換。
- [ ] 以舊存檔驗證 context 推導（不需補建資料）。
- [ ] 讀取 FFF 警戒（上升偵測）及封鎖公開狀態。
- [ ] 接入守墓人盤查／搜索／交戰模式與 `lastSeenTick`。
- [ ] 接入增援倒數（`TimedDetectionRaids`、`QuestPart_RaidWaveArrives`）、`Verb.TryCastNextBurstShot` 交戰 hook 與友方參戰標記；持續威脅效果先以警戒原因處理（見 2.1、2.2）。
- [ ] 加入未知地圖降級。

完成條件：開發者畫面可看見正確原因與狀態，但此階段不播放音樂。

### Phase 3：Boss 遭遇遷移

- [ ] 生成時登記 Boss 實體，抵達訊號時更新實際投放地圖，spawn 後轉 Active。
- [ ] 覆蓋四個入口（誘餌天線／誘餌雷達／誘餌發射器／加密發報器），含在基地建造誘餌與改投玩家據點的情況。
- [ ] 登記封存科技追殺 lord（`HuntdownWorker_Occultech.TryFireWave`、`TryFireFleetRaid`）。
- [ ] 以 `DMS_BossKilled` 處理死亡（以已登記 Boss 參照過濾，排除一般編組的 `isBoss` 單位）；輪詢處理離場、轉友方、永久失能與取消；短暫 EMP 不解除。
- [ ] 不改動原任務清單清空行為；以獨立資料追蹤音樂。
- [ ] `QuestPart_BossgroupArrivesWithMusic` 停止呼叫 `ForcePlaySong`；保留 `song` 欄位讀檔相容。
- [ ] `ModExtension_BossSong` 目前無 XML 使用：改為提供 Boss 曲庫變奏給 `SelectSong()`，或標記為廢棄；空清單安全降級。

完成條件：Boss 抵達只登記一次，切換地圖不搶曲，任務結束不殘留。

### Phase 4：Transitions、Sequences 與資產

- [ ] 實作 `MusicSequenceWorker_DMS`（`ShouldEnd()`、`SelectSong()`、空曲庫保護）與八種 transition worker。
- [ ] 建立八個 transition 與八個 sequence；依第 6 節設定淡入淡出、`minTimeToPlay`、`pausedVolumeFactor` 與 `overridesInterruptions`。
- [ ] 為現有十二首音檔建立 SongDef（`playOnMap=false`）；只將已驗收且用途確定的曲目加入有效播放清單。
- [ ] 驗證新增三首變奏的選曲條件、Boss 優先與選曲穩定性。
- [ ] 完成封鎖倒數變奏／專曲與戰後餘波短曲，記錄所用來源與播放版本。
- [ ] 先以有效開發曲目驗證，再換上正式資產。
- [ ] 驗證升級、降級空檔、中斷、同狀態不重播及一次性餘波。
- [ ] 實作觀看地圖切換時主動呼叫 `CheckTransitions()`；驗證暫停中切換地圖。
- [ ] 實作外部強制播放偵測與讓位。
- [ ] 確認無 DLC 的載入不出現缺失引用；有 DLC 時依 3.1 驗證共存。

完成條件：完整狀態路徑能聽到正確曲目，沒有缺檔或 defs 解析錯誤。

### Phase 5：設定、診斷、效能與驗收

- [ ] `DMS_Settings` 新增情境音樂開關，補三語 Keyed 翻譯；關閉時所有 DMS worker 回傳 false。
- [ ] 提供目前地圖、狀態、原因、Boss 數、封鎖狀態與緩衝顯示。
- [ ] 提供注入／清除音樂測試事件的開發者工具，不直接改動正式任務獎勵或守軍。
- [ ] 日誌僅在狀態改變或例外時輸出，避免每次查詢洗版。
- [ ] 執行第 9 節情境矩陣與相關建置檢查。
- [ ] 比較多地圖、多機兵情境下的更新成本，確認無每幀任務全掃描（含暫停在 60 倍數 tick 時的每幀查詢）。

完成條件：驗收矩陣通過，正式曲庫就位，未解限制與版本相依已記錄。

## 9. 驗收矩陣

| 場景 | 預期結果 |
|---|---|
| 普通殖民地，無 DMS 遭遇 | 原版音樂；一般輪播中從不出現 DMS 曲目 |
| 基地內有 DMS 電腦、友方 Boss 同型機體 | 不變成站點／Boss 曲 |
| 抵達餘械站點，守軍可見但尚未接敵 | 探索；原版危險模式即使因守軍戰力 > 400 為 High，也不升為交戰 |
| 觀看站點時基地遭襲 | 站點維持 DMS 狀態；切回基地後交還原版危險音樂 |
| 守墓人 Patrol → Challenge → Engage 並接敵 | 探索 → 警戒 → 交戰 |
| 守墓人 Engage，但 600 tick 內未看見入侵者（被高警戒值維持） | 不播交戰曲；依警戒規則判斷 |
| 守墓人 Search，無近期接敵 | 搜索開始後 2500 tick 內為警戒，之後回探索；接敵則交戰 |
| 警戒值單次上升 | 進入警戒；2500 tick 內無新上升且緩衝結束即退出，不等待警戒值衰減 |
| 警戒值長時間維持高水位、沒有新上升 | 不無限期維持警戒 |
| 增援仍即將抵達，但警戒已無上升 | 保持警戒 |
| 地表站點增援倒數（`TicksLeftToSendRaids`）剩 1800 tick 內 | 進入警戒；增援抵達（`RaidsSentCount` 增加）後依交戰規則判斷；下一輪 18–24 小時倒數不提前觸發警戒 |
| 設施 QRF 已呼叫、仍在 `minDelay` 內 | 預計抵達前 1800 tick 才進入警戒，不從呼叫當下就警戒 |
| 敵我砲塔互射、玩家射擊未命中敵人 | 視為交戰；玩家射擊野生動物或無敵物件不算 |
| 地下檔案庫、儲存庫、地下大廳、指揮中樞 | 正確地下 context 與探索曲 |
| 地表 → 地下 | 立即升級為地下探索 |
| 地下 → 地表 | 不被 30 秒最低播放時間鎖住；淡出後接地表探索 |
| Boss 已召喚但仍在長時間等待 | 不提前播放 Boss 曲 |
| Boss 接近 → 投放／啟動 | 警戒 → Boss 落地 spawn 後（抵達訊號後約 230–310 tick）才切 Boss 曲 |
| 召喚 Boss 後玩家離開站點，Boss 改投玩家據點 | 只在實際投放地圖登記與播放；站點不殘留 Boss 狀態 |
| 在基地建造誘餌建築召喚 Boss | 基地進入警戒 → Boss 曲；結束後回原版 |
| 一般 `DMS_Legacy`／`DMS_Army` 襲擊帶有 `isBoss` 單位（駭蜥、玳瑁） | 不播 Boss 曲；其死亡不影響已登記遭遇 |
| 多 Boss 擊倒其中一架 | 其餘仍有效時保持 Boss 曲 |
| Boss 暫時 EMP | 不播放戰後餘波 |
| Boss 死亡但護衛仍交火 | 淡出後轉一般交戰（有降級空檔），之後才允許餘波 |
| Boss 轉友方／離圖 | 解除 Boss 狀態，依現存威脅重新判斷 |
| 已確認的主戰師遭遇實際交戰 | 待 D2 決策；決策前不選用 Black Knight Remix，未標記的一般重型單位不誤觸發 |
| 友方 DMS 部隊抵達但尚未參戰 | 不單獨觸發 Allied Marching Precision 或交戰狀態 |
| 友方 DMS 部隊實際參戰 | 符合仲裁順序時選用 Allied Marching Precision；不搶普通 Boss 主題 |
| 長期封存科技追殺狀態，但當前地圖平靜 | 不播放 Occultech Huntdown |
| 已標記的封存科技追殺襲擊在玩家據點實際交戰 | 使用 Occultech Huntdown；仍讓位封鎖倒數與原版高優先特殊事件 |
| 永久敵對期間的普通 `DMS_Army` 襲擊（非追殺排程） | 不使用 Occultech Huntdown |
| 主戰師、友方支援、追殺條件同時成立 | 依仲裁規則穩定選曲，不在刷新時來回切換 |
| Boss 戰中開始封鎖倒數 | 倒數曲在約 1 秒淡出後接管 |
| 3 倍速下開始封鎖倒數 | 倒數開始即由主動檢查接管，不依賴 60 tick 檢查剛好落在幀邊界；記錄實測延遲 |
| 地下倒數中，玩家觀看相連地表 | 待決策（第 11 節 D1）；決策前依現行規則不播倒數曲 |
| 倒數中解除封鎖，但仍在交戰 | 回 Boss／交戰，不播餘波 |
| 封鎖完成且無交戰 | 受困曲 |
| 受困時再次交火 | 交戰曲 |
| 封鎖中控遭摧毀 | 按 `Sealed` 處理，不能誤認解除危機 |
| 設施 `Lost` 或已撤空 | 受困曲退出，無永久殘留 |
| 肅清信件後仍有砲擊／增援 | 不誤播餘波 |
| 餘波播放中再次受襲 | 立即由有效威脅接管；原危機餘波不重播 |
| 戰鬥地圖切回安全基地／世界地圖 | 退出原地圖 DMS 序列；不受最低播放時間或緩衝鎖住 |
| 暫停中切換地圖 | 不等解除暫停即退出／切換 DMS 序列 |
| 切回仍在交戰的站點 | 恢復對應狀態，不重播召喚開場 |
| Boss／倒數／受困期間存讀檔 | 狀態正確恢復，無重複事件與懸空參照 |
| 餘波窗口內存讀檔 | 餘波最多再播一次（已接受的限制） |
| 更新前舊存檔與已生成地下圖 | 由 `Site.parts`／`generatorDef` 正確識別；無法識別者交還原版且不報錯 |
| 暫停、1／2／3 倍速 | 遊戲倒數依 ticks；聽感緩衝暫停時不前進；無高速切歌 |
| Anomaly `HorrorTension` 進行中，於 DMS 站點探索 | DMS 探索讓位；事件結束後恢復 |
| Anomaly `HorrorCombat` 或 Odyssey `OrbitalCombat` 先開始，DMS 交戰成立 | DMS 交戰等待其結束（原版限制）；封鎖倒數則立即切斷 |
| Anomaly `CorpseAttack` 600／`MetalHell` 999 | DMS 不搶占（包含封鎖倒數）；事件結束後重新判斷 |
| 未啟用 Anomaly／Odyssey | 正常載入，沒有限定 defs／型別錯誤 |
| 外部模組 `ForcePlaySong` | DMS 讓位至強制曲播完，不產生淡出卡住 |
| DMS 序列播放中被外部強制播放 | 強制曲立即播放；DMS transition 不再成立後被原版清除，不留下「已觸發但不播」的狀態 |
| 強制曲播畢且 DMS 狀態仍成立 | 下一幀由 DMS 接手；不插入原版一般曲，或依 2.1 的取捨只短暫淡出一般曲 |
| Multiplayer：兩端觀看不同地圖 | 各自播放對應狀態，不產生同步錯誤（需 MP 1.7 版發布後實測） |
| 設定關閉情境音樂 | 所有 DMS 狀態不觸發，完全交還原版 |
| 曲庫空白／資產缺失 | 載入檢查即報錯；執行期安全回退，不在 `SelectSong()` 除以零，不洗版 |

## 10. 檢查與交付

### 10.1 自動檢查

- 對狀態選擇器寫有意義的測試：優先序、警戒上升窗口、多警戒原因、守墓人模式與接敵窗口、Boss 多實體、一次性餘波、舊檔預設與 `Lost` 退出。
- XML 檢查：唯一 defName、worker 型別存在、sequence／song 引用有效、音檔存在且大小寫一致、曲庫非空、所有 DMS SongDef `playOnMap=false`、每個 DMS sequence 都設定了 `fadeoutLastSongDuration` 與 `overrideFadeout`、DMS transition 未設 `dangerRequirement`。
- 在已有還原資產的環境可先執行 `dotnet build _Source/DMS/DMS.csproj -c Release --no-restore`；缺少還原資產時先正常 restore，再建置。記錄實際結果。
- 注意建置會更新 `1.7/Assemblies/DMS.dll`；僅在正式實作階段執行，保留並辨識原有二進位變更。
- 編譯成功不能替代遊戲內音訊、地圖切換、暫停與存讀檔驗證。

### 10.2 完成交付清單

- [ ] C# 與 XML 實作、必要的窄範圍 hook。
- [ ] 正式音樂檔與清楚的 SongDef 對照。
- [ ] 已停止呼叫 `ForcePlaySong`，外部強制播放可正確讓位。
- [ ] 設定開關與三語翻譯。
- [ ] 新檔與舊檔的驗收記錄。
- [ ] 原版 DLC 與特殊音樂相容記錄（含 3.1 所列不可中斷序列的實測結果）。
- [ ] 音樂授權確認與音檔版本控制方式。
- [ ] 無多餘依賴 DLL、缺檔引用或意外的全域音樂覆蓋。
- [ ] 更新本計畫的勾選狀態，列出未完成項目，不將佔位資產或未測情境標成完成。

## 11. 待決策事項

### D1：封鎖倒數時玩家在地表觀看

**情境**：地下設施處於 `Countdown`，地下仍有玩家單位，但玩家目前觀看的是相連的地表站點（或其他地圖）。

**現行規則**：音樂跟隨觀看地圖，地表播地表狀態；倒數只靠 FFF 的信件與每 600 tick 一次的訊息提示。倒數 1 倍速 60 秒、3 倍速只有 20 秒。

| 選項 | 行為 | 優點 | 代價 |
|---|---|---|---|
| A：維持跟隨觀看地圖 | 地表不播倒數曲 | 最簡單，與其他狀態規則一致 | 玩家在地表可能錯過倒數的緊迫感 |
| B：相連地表例外 | 觀看地圖等於該設施 `SurfacePortal` 所在地圖，且地下仍有玩家時，倒數曲接管 | 涵蓋最常見的「人在地下、鏡頭在地表」 | snapshot 需跨地圖讀取封鎖狀態；需自行判斷地下是否有玩家（`AnyPlayerPawnInside()` 為 private）；倒數 transition 的離開條件與 `ShouldEnd()` 都要同步調整 |
| C：任何地圖都接管 | 只要有任何設施在倒數且有玩家在內就播 | 提示最強 | 違反「不因遠方事件搶走配樂」的核心原則 |

**決策前的實作預設**：採 A，但 snapshot 與 `DMS_LockdownCountdown` worker 的介面保留跨地圖查詢的可能，日後改為 B 時不需重構。決策後同步更新 3.1、3.2、第 6 節與驗收矩陣。

### D2：「主戰師」對應的實體標記

**情境**：`Black Knight Remix` 計畫用於「主戰師相關遭遇」，但 2.2 查證後，DMS、FFF 與語系檔都沒有「主戰師」的 Def、編組、LordJob 或事件。

| 選項 | 標記方式 | 優點 | 代價 |
|---|---|---|---|
| A：守墓人巡邏隊 | 地圖上存在敵對 `LordJob_GravekeeperPatrol` 且處於 `Engage`／`Search` 並有近期接敵 | 已有明確 LordJob 與狀態；軍火庫與地下設施都有 | 範圍只限守墓人，可能與「主戰師」原意不符 |
| B：主戰規格單位 | 交戰中的敵對 pawn 屬於 `DMS_Occultech_MainBattle` 解鎖的 PawnKind 清單（守墓人各型、DMSAC 主戰規格機體） | 對應「主戰規格」研究名稱 | 需維護 PawnKind 清單；一般編組出現這些單位時也會觸發 |
| C：新增明確標記 | 為特定 Boss／RaidWave／編組加 DefModExtension 或任務標籤 | 最精確，與現有曲目仲裁一致 | 需先定義哪些遭遇算「主戰師」，並修改對應 defs |

**決策前的實作預設**：不接入 `Black Knight Remix` 的自動選曲；選曲仲裁中的「已標記主戰師遭遇」一律不成立。決策後同步更新 2.2、7.1 與驗收矩陣。

## 12. 建議實作順序

先完成「地圖識別＋狀態診斷」，再完成「探索／警戒／交戰」，接著遷移 Boss，最後加入封鎖與餘波。音樂製作可依六組曲庫獨立準備；正式啟用所有 transition 前，必須完成中斷、降級空檔、地圖退出、暫停、資產與存檔驗收。

後續擴充可加入不同 Boss 的曲庫變奏、SAGE 系統甦醒短曲與封存科技揭露提示；沿用相同事件與狀態架構，不先增加長期全域音樂狀態。
