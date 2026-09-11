# The Dead Man's Switch (DMS)

> **RimWorld 1.6 模組** — `Aoba.DeadManSwitch.Core` · 版本 `2.0.00-pre.1`
> Steam Workshop: <https://steamcommunity.com/sharedfiles/filedetails/?id=3121742525>
> GitHub: <https://github.com/AobaKuma/Dead-Man-Switch>

千年之前，面對智械戰爭機器、海盜與失控生化武器的威脅，Nara Industries 啟動了一項半自動化戰爭機器計畫，目標是打造一支低科技、低維護需求的機械軍團，協助全銀河的人類對抗這些威脅。

本模組將這支軍團帶進 RimWorld：新增 **Automatroid 機兵**、**Synthroid 合成人**、**無人機**、大量武器與裝備，以及一個完整的派系 **Colonization Forces（殖民軍）**，並附帶專屬任務線、說書人、金庫地圖與大量殘骸建築。

---

## 目錄

- [功能總覽](#功能總覽)
- [相依與相容性](#相依與相容性)
- [目錄結構](#目錄結構)
- [載入資料夾（LoadFolders）](#載入資料夾loadfolders)
- [C# 原始碼](#c-原始碼)
- [建置](#建置)
- [開發注意事項](#開發注意事項)
- [作者](#作者)

---

## 功能總覽

### 機兵（Automatroid / Mechanoid）
以 `1.6/Defs/Things_Race/` 為主，依重量分級：

| 等級 | 機種 |
|---|---|
| 輕型 | Falcon、Noctula、Hyrax |
| 中型（Arquebusier 系） | Arquebusier、Jaeger、Pioneer、Sniper |
| 中型（Ronin 系） | Ronin、Gladiator、Siegebreaker、Protector、Phalanx |
| 中型（Soldat 系） | Soldat、Raider、Grenadier、Kanonier、Sergeant |
| 重型 | BattleFrame、FieldCommand、EscortLifter、ShaftWorker、Tachanka、Mammoth、Geochelone、Caretta、Killdozer、Iguana、Gecko、Ape、HermitCrab、Tarbosaurus |
| 超重型 | ClusterWeaver（集群織鳥，可投放無人機蜂群） |
| 無人機 | VerlingBrid、Hound、Trashpan、Wildfowl（FPV） |
| Synthroid | Lady、Dogge、Frogman（可透過胚胎二次孕育） |

多數機兵擁有專屬技能（`Defs/Abilities/`），並透過 Fortified Framework 提供的 `MechWeaponExtension`、`HeavyEquippableExtension`、多砲塔（`CompPropertiesMultipleTurretGun`）等擴充實現裝備與武裝系統。

### 派系與劇本
- **DMS_Army – Colonization Forces（殖民軍）**：由 AI 統帥領導的軍事化殖民組織，Spacer 科技，支援 Royalty 頭銜系統（榮譽 = honor）。
- **DMS_Legacy – Colonization legacies**：敵對殘餘勢力。
- **玩家派系**：`DMS_Colony`（fleet colony）、`DMS_Deviants`（deviants）。
- **劇本**：`DMS_Scenario – Last Command`。
- 詳細的 PawnKind 編制說明見 `1.6/Defs/Pawnkinds/*/內容目錄.txt`。

### 任務線（Quests）
由 `_Source/DMS/Quests/` 的 C# 與 `1.6/Defs/Quests/`、`1.6/NewContent/Defs/Quests/` 的 XML 共同組成：

| 任務 | 說明 |
|---|---|
| **Supply Chain** | 補給鏈：接單 → 交付 → 領取獎勵，含補給艙（`QuestPart_SpawnSupplyPod`） |
| **Officer Training** | 軍官訓練：派遣殖民者受訓，畢業後獲得頭銜 |
| **Court Martial** | 軍法審判：法庭停戰、判決、審判船 |
| **Promotion Ceremony** | 晉升典禮（Royalty） |
| **Stele / Occultech Key** | 石碑與秘術科技鑰匙，含撞擊地點（Impact Site）站點 |
| **Document Processing** | 文件處理類任務（`CompQuestWorkable` + `JobDriver_ProcessQuestWorkable`） |
| **Decoy Site / Pirate Assembly** | 誘餌前哨與海盜集會站點 |
| **Bossgroup** | 首領集團來襲，附專屬 BGM（`QuestPart_BossgroupArrivesWithMusic`） |

### 說書人
- **Operator Greysia**：透過 `ModExtension_StorytellerBias` 與 `StorytellerComp_FavoredQuest` 偏好本模組來源的任務、事件與突襲派系。

### 金庫（Vault）與探索建築
- `_Source/DMS/Vault/` + `1.6/NewContent/Defs/Vault/`：口袋地圖金庫生成（`DMS_GenStep_Vault`、`DMS_LayoutWorker_Vault`），含入口、軍械庫、指揮室、機兵艙等房間內容。
- `1.6/NewContent/Defs/GenStructures/`、`Things_Building/DMS_ExplorationBuildings_*`：邊緣廢墟、衛星、散落廢墟、戰利品箱、秘術科技裝置等。

### 其他系統
- **Surgical Apparel**：手術植入型服裝（`CompSurgicalApparel`）。
- **Frogman 二次孕育**：機械胚胎植入與出生（`MechEmbryo`、`MechBirth`、`Building_SecondaryGestation`）。
- **Cluster Weaver AI**：載機對峙與無人機投放 AI（`JobGiver_AICarrierStandoff`、`JobGiver_DeployDroneSwarm`）。
- **Occultech 研究分頁**：獨立研究樹（`Defs/Research/`）。
- **無機械師需求**：`NoMechanitorNeed` / `MechanitorPatch`。
- 空中支援、砲塔、改裝件（Modification）、ERA 反應裝甲等。

---

## 相依與相容性

### 必要前置
| 模組 | packageId |
|---|---|
| Harmony | `brrainz.harmony` |
| Biotech DLC | `ludeon.rimworld.biotech` |
| **Fortified Feature Framework (FFF)** | `AOBA.Framework` — <https://github.com/AobaKuma/Fortified-Framework> |

> 大部分機兵／載具／砲塔的底層邏輯已移至 Fortified Framework，本模組的 `DMS.dll` 只保留模組專屬內容（任務、金庫、Frogman、說書人等）。

### 選用相容（透過 `LoadFolders.xml` 條件載入）
Royalty、Ideology、Odyssey、Combat Extended、Gestalt Engine / Reinforced Mechanoid 2、Vanilla Races Expanded – Android、Vanilla Factions Expanded – Pirates、RimLanguage、Ancient Urban Ruins、PLA Armory、PCA、Ratkin、Monolyn。

### 不相容
- `mlie.rebound`

---

## 目錄結構

```
Dead-Man-Switch/
├─ About/                 # About.xml、Preview.png、PublishedFileId.txt
├─ Defs/                  # 版本無關的 Def（設計分類、命名包、音效）
├─ LoadFolders.xml        # 版本／條件載入設定
├─ Sounds/                # 音效（CAS、砲擊、特效、無線電）
├─ Textures/              # 貼圖（Automatroid、Synthroid、武器、建築、UI…）
├─ 1.6/
│  ├─ Assemblies/         # DMS.dll（建置輸出）
│  ├─ Defs/               # 核心 Def：技能、背景、派系、機兵種族、武器、建築、任務、研究…
│  ├─ Patches/            # 核心 XML Patch
│  ├─ Languages/          # English / 繁體中文 / 简体中文
│  ├─ NewContent/         # 2.0 新內容：金庫、探索建築、站點、說書人、RaidWave…
│  ├─ Royalty/ Ideology/ Biotech/ Odyssey/   # DLC 條件內容
│  ├─ CE/                 # Combat Extended 相容
│  └─ Mod/                # 其他模組相容（VREA、VFEP、AUR、PLAA、PCA、RK、Monolyn、RimLanguage、GestaltEngine、FTND、SaveOurShip2）
└─ _Source/
   ├─ DMS/                # 目前的 C# 專案（DMS.csproj）
   └─ 1.5_Backup/         # 1.5 時代的舊原始碼備份（DMS、DMSCE、DMS_Story），僅供參考
```

> `1.6/Mod/FTND` 與 `1.6/Mod/SaveOurShip2` 目前**未**列在 `LoadFolders.xml` 中，不會被遊戲載入。

---

## 載入資料夾（LoadFolders）

`LoadFolders.xml` 對 1.6 的載入順序：`/` → `1.6` → 各 DLC 子資料夾 → 各相容模組子資料夾 → `1.6/NewContent`。
新增相容內容時，請在 `1.6/Mod/<名稱>/` 建立資料夾並在 `LoadFolders.xml` 加入對應的 `IfModActive` 條目（記得同時列出 `_steam` 後綴的 packageId）。

---

## C# 原始碼

位於 `_Source/DMS/`，命名空間 `DMS`，Harmony ID `AOBA.DMS`（`HarmonyEntry.cs` 使用 `PatchAll`）。

| 資料夾 / 檔案 | 內容 |
|---|---|
| `AI/` | Cluster Weaver 載機 AI、無人機投放 Job/JobGiver/ThinkNode |
| `Frogman/` | 機械胚胎、二次孕育建築、口糧回復 |
| `Quests/` | 任務節點與 QuestPart（SupplyChain、OfficerTraining、CourtMartial、Stele、文件處理） |
| `Royalty/` | 晉升典禮、授勳任務 Patch、獎勵接駁船許可 |
| `Storyteller/` | 說書人偏好擴充與 Patch |
| `SurgicalApparel/` | 手術服裝 Comp / Patch / 移除配方 |
| `Vault/` | 金庫 GenStep、LayoutWorker、房間內容、電梯 |
| `Rendering/` | 制服渲染節點 |
| `DMS_DefOf.cs` | DefOf 宣告 |
| `CompPanicAura.cs`、`CompTurretTopDraw.cs`、`CompUseEffect_SummonRaid.cs`、`NoMechanitorNeed.cs`、`MechanitorPatch.cs` | 零散 Comp 與 Patch |

程式碼註解採**中英雙語**，新增程式碼時請沿用此慣例。

---

## 建置

### 需求
- .NET SDK（目標框架 `net472`，C# 10）
- 同層目錄存在 `_Fortified-Framework/`（csproj 以相對路徑 `..\..\..\_Fortified-Framework\` 參考 `Fortified.dll`、`0Harmony.dll` 與 UnityEngine 組件）
- NuGet：`Krafs.Rimworld.Ref 1.6.4565-beta`、`Lib.Harmony 2.4.1`、`Krafs.Publicizer 2.3.0`

### 步驟
```bash
cd _Source/DMS
dotnet build DMS.csproj -c Release
```
或直接執行 `_Source/DMS/build.bat`。輸出位置為 `1.6/Assemblies/DMS.dll`（不產生 pdb）。

`Krafs.Publicizer` 用於公開部分原版 private 成員（如 `Recipe_Surgery.CheckSurgeryFail`、`QuestPartActivable.Complete`），詳見 `DMS.csproj` 內的 `<Publicize>` 項目。

---

## 開發注意事項

- **分支**：`main` 為發布分支，`DMS2-Dev` 為 2.0 開發分支。
- **`.rimignore`**：定義發布到 Workshop 時排除的檔案（`_Source/`、`.idea/`、`*.bat`、`*.pdb` 等）。
- **`.disabled` 檔案**：以 `.disabled` 結尾的 XML（如 `HireableFaction.disabled`、`Patch_AncientBuildings_DevCategory.disabled`）為暫時停用的內容，不會被載入。
- **翻譯**：所有新 Def 都應在 `Languages/` 下補上 English、繁體中文、简体中文三種語言。
- **相依於 FFF**：新增機兵／武器功能前，先確認 Fortified Framework 是否已提供對應的 Comp / Extension，避免重複實作。

---

## 作者

AobaKuma、Bread Mo、Bill Doors、KV4EX、Mortis
