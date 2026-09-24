# 原版遠古建築參考 / Vanilla Ancient Structures（純 Core）

> 目的：從原版遠古建築的 Def 與生成框架中，整理出**純 Core（不裝任何 DLC）**時，可以用在 DMS 地下設施生成（軍事儲存庫、設施檔案庫、指揮中心）的部分。
>
> 撰寫依據：RimWorld 1.6 的 `Data/*/Defs`，以及 `Assembly-CSharp.dll` 的反編譯結果；對照分支 `DMS2-Dev`（2026-09-25）。**沒有進遊戲實測。**
>
> 判斷原則：
> - DLC 資料夾（Royalty／Ideology／Biotech／Anomaly／Odyssey）裡的 def，一律不引用。
> - Assembly-CSharp 的 C# 類別在 Core 下也存在，可以使用。但以下兩種要排除：有 `ModsConfig.*Active` 閘門的，以及 `*DefOf` 指向 DLC def 的（沒裝 DLC 時是 null，會直接出錯）。

---

## 目錄

1. [原版遠古內容的分佈](#1-原版遠古內容的分佈)
2. [生成流程與 Vault 目前的使用狀況](#2-生成流程與-vault-目前的使用狀況)
3. [版面與房型欄位](#3-版面與房型欄位)
4. [RoomPart](#4-roompart)
5. [RoomContents worker](#5-roomcontents-worker)
6. [PrefabDef](#6-prefabdef)
7. [遠古建築 ThingDef](#7-遠古建築-thingdef)
8. [Core 抽象基底](#8-core-抽象基底)
9. [地形、戰利品表、GenStep](#9-地形戰利品表genstep)
10. [需要改 C# 的系統](#10-需要改-c-的系統)
11. [要避開的東西](#11-要避開的東西)
12. [已知問題](#12-已知問題)
13. [建議實作順序](#13-建議實作順序)
14. [原版檔案索引](#14-原版檔案索引)

---

## 1. 原版遠古內容的分佈

遠古建築的**版面、房型、Prefab、RoomPart 幾乎全部放在 Odyssey**，Core 只有零星幾項。

| 套件 | 內容 | 純 Core 能不能用 |
|---|---|---|
| Core | `ComplexCorridor` 房型、12 個 PrefabDef、`SleepingMechanoids`／`MechDrop` 威脅、約 75 個遠古建築 ThingDef、3 種遠古地形、遠古戰利品表、地下地圖的 GenStep | ✅ |
| Odyssey | 全部 StructureLayoutDef（AncientRuins_*、AncientGarrison、AncientWarehouse、AncientChemfuelRefinery、AncientInfestedSettlement、AncientLaunchSite、AncientStockpile…）、約 70 個 LayoutRoomDef、`AncientPrefabs`／`AncientExteriorPrefabs`、RoomPartDef、`TileMutators_AncientStructures`、地下 `AncientStockpile` 地圖、`AncientHatch` | ❌ def 不能用；C# 類別見下文 |
| Ideology | `AncientComplex` 版面、`LayoutRooms_AncientComplex`、多數 ComplexThreatDef（SleepingInsects、CryptosleepPods、Ambush、RaidTerminal、FuelNode、Infestation、SecurityCrate） | ❌ def 不能用；部分 worker 可以用（見 §10） |
| Biotech | `LayoutRooms_MechanitorComplex`、`Buildings_Ancient_Mechanitor` | ❌ |

---

## 2. 生成流程與 Vault 目前的使用狀況

### 2.1 生成流程

**版面（`LayoutWorker.GenerateStructureSketch`）**

1. `GenerateSketch`（DMS：`VaultLayoutGenerator` 產生房間）
2. `ResolveRoomDefs`：先分配必出房型（`countRange.min`），再依權重抽其餘房型
3. `MergeAdjacentRooms`：相鄰且雙方都設了 `canMergeWithAdjacentRoom` 的房間，會合併
4. `RemoveBorderDoors`：相連且雙方都設了 `canRemoveBorderDoors` 的房間，中間的門可能改成空門洞
5. `FlushLayoutToSketch` → `PostLayoutFlushedToSketch`
6. `ResolveRoomSketches`：鋪 `floorTypes`／`edgeTerrain`，執行 `sketchResolverDef`
7. `shouldDamage` 為 true 時，套用 `DamageBuildingsLight`

**生成（`LayoutWorker.Spawn`）**

1. 生成 sketch（牆、門、地板）
2. 每間房依序跑 `PreFillRooms` → `FillRoom` → `PostFillRooms`
3. `LayoutWorker_Structure` 再處理：`wallDamageRange`、`clearDoorFaction`、`ensureOneDoorUnlocked`，然後是 `surroundingTerrainDef` 和 `scatteredPrefabs`

**房間填充（`RoomContentsWorker.FillRoom`）**

1. 屋頂
2. 拆牆（`canRemoveBorderWalls`）
3. `scatterTerrain`
4. `parts`（`FillOnPost=false` 的那些）
5. `prefabs`
6. `fillEdges`
7. `fillInterior`
8. `scatter`（加上版面層級的 `junkScaterrers`）
9. `wallAttachments`（加上版面的 `wallLampDef`）

`FillOnPost=true` 的 parts 會等到 `PostFillRooms` 才跑。

版面層級（StructureLayoutDef）的 `prefabs`、`parts`、`fillEdges`、`fillInterior`、`scatterTerrain`、`wallAttachments`，會套用到**每一間房（包括走廊）**。

### 2.2 Vault 已經用到的

- **StructureLayoutDef：** `roomDefs`（weight／countRange）、`corridorDef`、`wallDef`、`doorDef`、`doorStuffDef`、`wallLampDef`、`terrainDef`、`junkScaterrers`、`clearRoomsEntirely`、`canHaveMultipleLayoutsInRoom`、`minRoomWidth`／`minRoomHeight`
- **LayoutRoomDef：** `roomContentsWorkerType`、`prefabs`、`fillEdges`、`fillInterior`、`floorTypes`、`edgeTerrain`、`areaSizeRange`、`minSingleRectWidth`／`Height`、`dontDestroyWallsDoors`、`isValidPlayerSpawnRoom`、`thingSetMakerDef`、`spawnJunk`（只有一處）
- **PrefabDef：** `chance`、`relativeRotation`

下面各節列出的，都是**還沒用到**、而且純 Core 能跑的東西。

---

## 3. 版面與房型欄位

### 3.1 LayoutRoomDef（只寫 XML 就能用）

| 欄位 | 預設 | 效果 | Vault 可以怎麼用 |
|---|---|---|---|
| `parts` | – | 在房間掛 RoomPartDef（見 §4） | 箱子、休眠機兵、掩體、屍體 |
| `wallAttachments` | – | 沿牆掛物件，只掛在版面 `wallDef` 那種牆上 | 攝影機、通風口、壁掛配電盤，可以改成依房型設定 |
| `scatter` | – | 在房內散佈物件或污跡團塊 | 房型專屬的污跡（機兵艙放 `Filth_MachineBits`） |
| `scatterTerrain` | – | 在房內散佈不規則的地形團塊 | MetalTile 上補幾塊 Concrete，做出破損地板 |
| `spawnJunk` | true | false 時不套版面層級的 `junkScaterrers` | 電梯廳、獎勵房保持乾淨 |
| `threatPointsScaleCurve` | – | 把點數換算成這間房 parts 的威脅量 | 搭配休眠機兵群 |
| `canBeInMixedRoom` | false | 一間房同時套兩種房型（機率見版面的 `multipleLayoutRoomChance`） | 兵舍加軍械庫的混合房 |
| `canMergeWithAdjacentRoom` | false | 相鄰房間合併（機率見版面的 `adjacentRoomMergeChance`） | 和 DMS 自己的 `mergeChance` 功能重疊，擇一使用 |
| `canRemoveBorderDoors` | false | 中間的門改成空門洞（機率見版面的 `borderDoorRemoveChance`） | 兵舍和食堂連成套房 |
| `canRemoveBorderWalls` | false | 拆掉房間邊牆 | 會破壞分區設計，不建議 |
| `minConnectedRooms`／`minAdjRooms`／`requiresSingleRectRoom` | 0／0／false | 抽房型時的限制條件 | 指揮室只落在樞紐位置 |
| `dontPlaceRandomly` | false | 只能透過 `requiredDef`（例如 `importantRoomDef`）指定 | 特殊房型 |
| `sketchResolverDef` | – | 對每個矩形跑一次 Core 的 SketchResolver | 用處有限 |
| `roofDef`／`noRoof` | – | 屋頂類型／不蓋屋頂 | 地下用處不大 |

**參數類別的欄位**（括號內是預設值）：

- `LayoutScatterParms`：`def`、`stuff`、`groupCount`、`groupsPerHundredCells`(0~2)、`itemsPerGroup`(2~4)、`groupDistRange`(2~5)、`minGroups`
- `LayoutScatterTerrainParms`：`def`（TerrainDef），其餘欄位同上（沒有 `stuff`）
- `LayoutFillEdgesParms`：`def`、`stuff`、`padding`、`contractedBy`(1)、`countRange`、`groupsPerTenEdgeCells`(1)、`groupCountRange`(1~2)、`rotOffset`(Opposite)
- `LayoutFillInteriorParms`：`def`、`stuff`、`fixedRot`、`alignWithRect`、`contractedBy`(2)、`snapToGrid`、`countRange`、`thingsPerHundredCells`(2~3)
- `LayoutWallAttatchmentParms`：`def`、`stuff`、`countRange`、`thingsPer10EdgeCells`(1)、`spawnChancePerPosition`(1)
- `LayoutPrefabParms`：`def`、`countRange`、`minMaxRange`、`ensureNoBlocks`(true)、`countPerHundredCells`(1~2)、`countPerTenEdgeCells`(1)、`rotOffset`
- `LayoutPartParms`：`def`、`chance`(1)、`threatPointsRange`、`countRange`

### 3.2 LayoutDef／StructureLayoutDef

| 欄位 | 預設 | 備註 |
|---|---|---|
| `multipleLayoutRoomChance` | 0.15 | 搭配 `canBeInMixedRoom` |
| `adjacentRoomMergeChance` | 0.65 | 搭配 `canMergeWithAdjacentRoom` |
| `borderDoorRemoveChance` | 0.8 | 搭配 `canRemoveBorderDoors` |
| `wallLampChancePerPosition` | 0.6 | `wallLampDef` 每個候選位置的機率 |
| `importantRoomDef` | – | ⚠ Vault 目前不會生效（見 §10.4） |
| `exteriorDoorDef`／`roomToExteriorDoorRatio` | –／0.33 | 地下設施沒有外門，不用 |
| `corridorShapes` | All | Straight、Cross、T、H、AsymmetricCross；DMS 走自己的走廊產生器，所以不影響 |
| `shouldDamage`／`wallDamageRange` | –／Invalid | ⚠ 和不可破壞牆的設計衝突（見 §11） |
| `ensureOneDoorUnlocked` | false | 只對 `Building_HackableDoor` 有效，這類門的 def 全在 DLC |
| `surroundingTerrainDef`／`scatteredPrefabs` | – | 給地表結構的外圍用；地下外圍是岩層，用處不大 |

---

## 4. RoomPart

自己寫 RoomPartDef，worker 用原版的。所有 `RoomPart_*` worker 都在 Assembly-CSharp 裡。

### 4.1 Core 可用

| RoomPartDef 類別／worker | 效果 | 備註 |
|---|---|---|
| `RoomPart_CrateDef`（內建 worker `RoomPart_Crate`） | 生成箱子，用 `thingSetMaker` 裝戰利品；`triggerThreatSignal`(true) 開箱時送出房間訊號 | `crateDef` 必須是 `Building_Crate`，Core 有 `AncientHermeticCrate`；`rotations` 可限制方向 |
| `RoomPart_DormantMechCluster` | 休眠機兵群，收到房間訊號或聽到噪音就醒（`LordJob_StructureThreatCluster`） | 需要機械族陣營存在；點數用 `threatPointsRange` 或房間的 `threatPointsScaleCurve` |
| `RoomPart_DormantInsectCluster` | 休眠蟲群 | 同上 |
| `RoomPart_DormantThreatCluster` | 一半機率蟲群、一半機兵 | |
| `RoomPart_GestationTankDef` | 放 Core 的 `AncientMechGestatorTank`，每 100 格放 3~7 個；`options` 依權重決定 Proximity／Dormant／Empty | 很適合機兵艙 |
| `RoomPart_InsectHive` | 1~3 個休眠蟲巢 | |
| `RoomPart_BarricadeDef` | 每扇門以 `chancePerDoor`(0.75) 的機率，在門內 `offset`(2) 格處橫放一排 `2×steps−1` 格寬的 `wallDef` | 用 Sandbags／Barricade（要填 `stuffDef`）或 DMS 自己的掩體，做「死守房」 |
| `RoomPart_ThingDef` + `RoomPart_CornerThing` | 在隨機房角放一個 `thingDef` | ⚠ 陣營是 null 時會歸到 `AncientsHostile`（見 §10.5） |
| `RoomPart_ThingDef` + `RoomPart_FillPadding` | 在房內放一個 `thingDef`，避開門口 | |
| `RoomPart_Corpse` | 一具村民屍體（刀傷） | |
| `RoomPart_Gore` | 血泊加一道屍體拖行血跡 | |
| `RoomPart_AncientWeaponStorage` | 鋼貨架加 6~12 把武器（IndustrialGunAdvanced／SimpleGun／RangedHeavy），生成時已禁用 | 軍械庫的另一種變體 |
| `RoomPart_ConnectConduits`（post） | 把房內的 `HiddenConduit` 接到最近的發電機和用電設備 | 動力室可以省一些接線程式碼 |

### 4.2 不要用

| worker | 原因 |
|---|---|
| `RoomPart_SentryDrone` | 有 `ModsConfig.OdysseyActive` 閘門，沒裝 DLC 時什麼都不做 |
| `RoomPart_AncientEngine` | 有 Odyssey 閘門，而且引用 `AncientGravEngine` |
| `RoomPart_Breached` | 會對牆直接 `Destroy()`；碰到 `DMS_SuperWall`（`destroyable=false`）會出錯，也破壞分區設計 |
| `RoomPart_CenterSpace` | 開 4×4 天井（拆屋頂、鋪 PackedDirt），不適合地下 |

### 4.3 XML 範例：開箱就伏擊

```xml
<RoomPart_CrateDef>
  <defName>DMS_Part_SupplyCrate</defName>
  <crateDef>AncientHermeticCrate</crateDef>
  <thingSetMaker>DMS_LogisticTerminal</thingSetMaker>
</RoomPart_CrateDef>

<RoomPartDef>
  <defName>DMS_Part_DormantMechs</defName>
  <workerClass>RoomPart_DormantMechCluster</workerClass>
</RoomPartDef>

<RoomPart_GestationTankDef>
  <defName>DMS_Part_MechVats</defName>
  <options>
    <Proximity>0.4</Proximity>
    <Dormant>0.3</Dormant>
    <Empty>0.3</Empty>
  </options>
</RoomPart_GestationTankDef>

<!-- 掛在 LayoutRoomDef 上 -->
<parts>
  <DMS_Part_SupplyCrate />
  <DMS_Part_DormantMechs>
    <threatPointsRange>150~250</threatPointsRange>
  </DMS_Part_DormantMechs>
  <DMS_Part_MechVats>0.5</DMS_Part_MechVats> <!-- 直接寫數字時代表 chance -->
</parts>
```

---

## 5. RoomContents worker

這些類別可以直接填進 `roomContentsWorkerType`（命名空間是 `RimWorld`）。

### 5.1 Core 安全

| worker | 內容 |
|---|---|
| `RoomContents_CryptosleepCaskets` | 2~4 具遠古冷凍艙（同一組），內容來自 Core 的 `MapGen_AncientPodContents`，敵我各半，外加置物櫃 |
| `RoomContents_CryptosleepCaskets_Hostile` | 同上，但艙內一定是敵人。可以做「冬眠守備隊」房 |
| `RoomContents_MechChargingRoom` | 遠古充電座（純裝飾） |
| `RoomContents_FuelRoom` | 遠古發電機，加 1~2 個不穩定燃料節點 |
| `RoomContents_ComputerRoom` | 一台 `AncientMachine` |
| `RoomContents_AncientRacks` | 大量 `AncientSystemRack` |
| `RoomContents_Kitchen`／`DiningHall`／`OperatingRoom`／`Hydroponics` | 廚房、餐廳、手術室、水耕 |
| `RoomContents_IndustrialStorage` | `AncientLargeContainer` |
| `RoomContents_ChemfuelStorage` | 貨架加化學燃料 |
| `RoomContents_ComplexCorridor`／`WarehouseCorridor` | 走廊加鋼貨架 |
| `RoomContents_ReactorCorpseRoom` | 4~6 具陳年海盜屍體 |
| `RoomContents_DeadBody`（抽象類別） | 繼承後可以做自訂的屍體房 |

### 5.2 會壞（引用 Odyssey 物件）

`RoomContents_CryptosleepCasket`（單數）、`HermeticCrate`、`HighValueStoreRoom`、`Stockpile`、`StockpileEntrance`、`StockpileHermeticCrateRoom`、`SecurityRoom_Stockpile`、`SecurityRoom_Reactor`、`ControlRoom`、`ShelfedCorridor`、`Checkpoint_Corridor`、`InnerCourtyard`、`InnerCourtyardRooms`、`NarrowHalls`、`ToxicWaterRoom`、`TransportRoom`。

`RoomContents_HermeticCrate` 的 `ThingSetMakerDef` 是 virtual 屬性，繼承後覆寫成 Core 或 DMS 的戰利品表，就能在 Core 下使用。

---

## 6. PrefabDef

### 6.1 Core 自帶、可直接引用的 Prefab

`Core/Defs/PrefabDefs/CommonRoomPrefabs.xml`：

`AncientSystemRacks_Rows`、`AncientSystemRack`、`AncientDisplayBank_Edge`、`AncientMachine_Rows`、`AncientEquipmentBlocks_Rows`、`AncientStorageCylinder_Edge`、`AncientGenerator_SmallCluster`、`AncientBarrel_Cluster`、`OperatingTables`、`AncientLockerBank_Row`、`Shelves_Rows`、`AncientLamp_Single`

### 6.2 DMS 還沒用到的功能

- **`PrefabDef`：**
  - `size`、`edgeOnly`、`rotations`（限制可用的旋轉方向）
  - `prefabs`：巢狀子 prefab（`SubPrefabData`：`def`、`chance`、`position`／`positions`、`relativeRotation`）
  - `terrain`：`PrefabTerrainData`（`def`、`chance`、`color`、`rects`）
- **`PrefabThingData`：** `stuff`、`colorDef`／`color`、`stackCountRange`（直接在 prefab 裡放物資）、`hp`（生成時就帶傷）、`quality`、`rects`、`canOverrideData`
- **牆和門會自動換掉：** `RoomContentsWorker.TryPlacePrefabs` 放置時，prefab 裡的牆、門會換成版面的 `wallDef`／`doorDef`（只要 `canOverrideData` 為 true，預設就是）。原版的 `Subroom_SpacerCrates` 就是這樣在房中隔出小密室的。照做的話，隔間牆會自動變成 `DMS_SuperWall`。

---

## 7. 遠古建築 ThingDef

Core 裡所有 `Ancient*` 建築都可以在沒有 DLC 的情況下使用。表中的「不可拆」表示 `deconstructible=false`，這類建築被打爛時會掉鋼渣。

### 7.1 有實際功能的

| defName | 尺寸 | 功能 | 注意 |
|---|---|---|---|
| `AncientCryptosleepCasket` | 1×2 | 遠古冷凍艙。同一組的艙開一具就全部打開；被打爛會噴火（半徑 2.66）；拆解可拿鋼 180、鈾 5 | 內容物要在生成時塞進去（`RoomContents_CryptosleepCaskets` 或 `RoomGenUtility.SpawnCryptoCasket`）；用 prefab 直接放只會是空艙 |
| `AncientCryptosleepPod` | 1×2 | 同上，但沒有右鍵選單 | 不可拆；原版用訊號（`SignalAction_OpenCasket`）打開 |
| `AncientHermeticCrate` | 1×2 | 可以打開的密封箱，有物資時會發光 | 空箱子打不開（`CanOpen` 的條件是有內容物）；要用 `RoomPart_CrateDef` 或 `RoomGenUtility.SpawnCrate` 生成才有東西 |
| `AncientMechGestatorTank` | 2×2 | 機兵培育槽。Proximity 狀態下，6~12 格內視線可及處出現殖民者就放出機兵；Dormant 狀態只在被打爛時放出。預設機種是 Scyther／Lancer，DLC 機種用了 `MayRequire`，會自動略過 | 預設狀態是 Empty，用 prefab 放就是裝飾；要靠 `RoomPart_GestationTankDef` 或 C# 設定狀態。不可拆 |
| `AncientFuelNode` | 1×1 | 受到任何傷害就點燃引信，接著火焰爆炸（半徑 6.9） | 陷阱、連鎖爆炸 |
| `AncientLamp` | 1×1 | 自帶光源，不用電、一直亮 | 放在地上，不是壁掛 |
| `AncientBed` | 1×2 | 可以使用的床（`Building_Bed`） | |

### 7.2 室內裝飾（適合地下設施）

| 類別 | defName（尺寸） |
|---|---|
| 機房、指揮 | `AncientSystemRack`(1×3)、`AncientDisplayBank`(3×1)、`AncientStorageCylinder`(2×1)、`AncientEquipmentBlocks`(4×2，不可拆)、`AncientMachine`(5×3，不可拆) |
| 看起來會動、其實不會 | `AncientGenerator`(2×2，不發電)、`AncientBasicRecharger`(3×1)／`AncientStandardRecharger`(3×2)（不能充電）、`AncientMechGestator`(3×2)／`AncientLargeMechGestator`(4×3)（殘骸，不可拆）、`AncientMechDropBeacon`(1×1，不會呼叫機兵)、`AncientSecurityTurret`(1×1，損壞的砲塔，**不會開火**) |
| 生活區 | `AncientLockerBank`(3×1)、`AncientOperatingTable`(1×2，不能手術)、`AncientMicrowave`、`AncientKitchenSink`、`AncientRefrigerator`、`AncientOven`、`AncientStove`、`AncientToilet`、`AncientVendingMachine`、`AncientWashingMachine`、`AncientAirConditioner`、`AncientATM` |
| 雜物（都打不開） | `AncientBarrel`、`AncientPipes`、`AncientCrate`、`AncientSpacerCrate`、`AncientMilitaryCrate`、`AncientLargeCrate`、`AncientLongCrate`、`AncientSmallCrate` |

### 7.3 大型殘骸（車輛庫這類大房間）

大多不可拆。

| 類別 | defName（尺寸） |
|---|---|
| 車輛 | `AncientRustedCar`(2×4)、`AncientRustedTruck`(2×4)、`AncientRustedJeep`(3×5)、`AncientRustedCarFrame`(2×3)、`AncientAPC`(5×3)、`AncientTank`(5×3，HP 2000)、`AncientPodCar`(3×2) |
| 引擎、飛行器 | `AncientJetEngine`(3×2)、`AncientDropshipEngine`(3×2)、`AncientRustedDropship`(6×5)、`AncientLargeRustedEngineBlock`(2×1)、`AncientRustedEngineBlock`、`AncientWheel`、`AncientGiantWheel`(2×2) |
| 戰爭機械 | `AncientWarwalkerTorso`(4×6)、`AncientWarwalkerShell`(5×3)、`AncientWarwalkerLeg`(2×4)、`AncientWarwalkerFoot`(2×2)、`AncientWarwalkerClaw`(1×2)、`AncientMiniWarwalkerRemains`(5×3)、`AncientWarspiderRemains`(5×5)、`AncientMegaCannonTripod`(3×3)、`AncientMegaCannonBarrel`(1×2) |
| 其他 | `AncientLargeContainer`(3×5，完全擋路，不可拆)、`AncientPipelineSection`(2×1) |

### 7.4 屏障、地表物件

- **屏障：** `AncientConcreteBarrier`（半掩體，HP 500）、`AncientTankTrap`(2×2)、`AncientRazorWire`、`AncientFence`
- **地表雜物：** `AncientLamppost`（只是造型，不會發光）、`AncientHydrant`、`AncientPostbox`、`AncientShoppingCart`、`AncientShipBeacon`、`Urn`

---

## 8. Core 抽象基底

以下都可以當 DMS def 的 `ParentName`。

| Name | 特性 | 用途 |
|---|---|---|
| `AncientBuildingBase` | 不可佔領、惰性、任何時候都能拆、完全擋路 | 一般遠古造型 |
| `NonDeconstructibleAncientBuildingBase` | 同上，但不能拆 | 大型殘骸 |
| `AncientSmallWalkableBuildingBase` | 1×1、可以穿過（路徑成本 50） | 小型雜物 |
| `AncientMechBuildingBase` | 機兵設備類殘骸，不可佔領 | 機兵艙裝飾 |
| `AncientTerminalBase` | 綠色螢幕閃光、發光、附互動格、每 tick 更新。**Core 裡沒有任何實體 def 繼承它** | DMS 控制台的基底 |
| `CrateBase` | `Building_Crate`，可以打開 | DMS 自己的可開啟箱子 |

---

## 9. 地形、戰利品表、GenStep

- **地形：** `AncientConcrete`、`AncientTile`、`AncientWoodPlankFloor`
- **戰利品表（ThingSetMakerDef）：**
  - `MapGen_AncientPodContents`：冷凍艙內容
  - `MapGen_AncientTempleContents`：神器、動力甲、科技藍圖、路西法藥等
  - `MapGen_AncientComplexRoomLoot_Default`
- **GenStep：** `Underground_RocksFromGrid`、`Underground_ScatterRuinsSimple`、`ScatterRuinsSimple`、`PlaceCaveExit`
- **SketchResolver：** `MonumentRuin`、`DamageBuildingsLight`、`DamageBuildings`、`AddColumns`、`FloorFill` 等
- **威脅：** `SleepingMechanoids`、`MechDrop`，以及抽象的 `DelayedThreat`／`SleepingThreat`

---

## 10. 需要改 C# 的系統

### 10.1 房間威脅訊號

每個 `LayoutRoom` 在建構時都會拿到一個唯一的 `threatSignal`（`"RoomThreat" + ID`），可以從 `room.ThreatSignal` 讀到。

- `RoomPart_Crate` 開箱時會送出這個訊號；`RoomPart_Dormant*Cluster` 會生成一個 `SignalAction_DormancyWakeUp` 監聽它。所以同一間房放箱子和休眠群，開箱就等於伏擊，完全不用寫程式。
- DMS 的警報系統（`AlertResponseUtility` 等）也可以對 `room.ThreatSignal` 送 `Find.SignalManager.SendSignal`，讓「警戒升級 → 叫醒某區休眠機兵」直接交給原版處理。

### 10.2 原版威脅預算系統（ComplexThreat）

`LayoutWorkerComplex` 搭配 `ComplexLayoutDef.threats`，會依點數在各房間分配威脅：

- **觸發方式：** 進房（`RectTrigger`）、開箱（`Building_Casket.openedSignal`）、駭入（`CompHackable`）
- **其他機制：** 延遲觸發（`delayChance`）、被動威脅
- **只會放在沒有 `requiredDef` 的房間**，剛好避開 Vault 的必出房間。
- **Core 已有的威脅 def：** `SleepingMechanoids`、`MechDrop`
- **可以自寫 ComplexThreatDef 的 worker**（只依賴 Core 物件）：`ComplexThreatWorker_Ambush`、`_CryptosleepPods`、`_FuelNode`、`_Infestations`、`_SleepingInsects`
- **不能用：** `_RaidTerminal`（需要 Ideology 的 `AncientEnemyTerminal`）、`_SecurityCrate`（需要 Ideology 的 `AncientSecurityCrate`）
- **要接上的話：**
  - `DMS_LayoutWorker_Vault` 改成繼承 `LayoutWorkerComplex`，版面 def 改成 `<ComplexLayoutDef>`。
  - 覆寫 `PostSpawnStructure`：原版只在 Ideology 啟用時發獎勵箱和通訊台。

### 10.3 威脅點數目前一直是 null（要先修）

- `Building_VaultElevator.GetExtraGenSteps`（`_Source/DMS/Vault/Building_VaultElevator.cs:139`）只帶了 `layout`，沒帶 `sitePart`。
- 所以 `DMS_GenStep_Vault`（`_Source/DMS/Vault/DMS_GenStep_Vault.cs:47`）拿到的 `threatPoints` 永遠是 null。
- 結果：RoomPart 的威脅一律用預設 300 點（除非個別設了 `threatPointsRange`），§10.2 的威脅系統完全不會生成（`threatPoints.HasValue` 是 false）。
- 做法：從電梯所在地圖的站點帶入點數，或改用 `StorytellerUtility.DefaultThreatPointsNow`。

### 10.4 `importantRoomDef` 沒有作用

`DMS_LayoutWorker_Vault.PostGraphsGenerated`（`_Source/DMS/Vault/DMS_LayoutWorker_Vault.cs:68`）覆寫時沒有呼叫 base，而原版正是在 base 裡指定 importantRoomDef。

### 10.5 陣營

`DMS_GenStep_Vault` 呼叫 `Spawn` 時沒有傳陣營。因此 `RoomPart_CornerThing` 放出來的東西會歸到 `Faction.OfAncientsHostile`，不是 DMS 的防務陣營。防務物件請繼續用現有的 C# 放，或另寫一個會掛 `VaultRoomUtility.DefenderFaction` 的 RoomPart worker。

---

## 11. 要避開的東西

### 11.1 DLC 的 defName（名字很像，但不是 Core 的）

- **Odyssey：** `AncientFortifiedWall`、`AncientBlastDoor`、`AncientEmergencyLight_Red`／`_Blue`、`AncientForklift`、`AncientIndustrialShelf`、`AncientSealedCrate`、`AncientSafe`、`AncientSecurityTerminal`、`AncientDestroyedConsole`、`AncientExplosivesCrate`、`AncientGravEngine`、`AncientTransportPod`、`Turret_AncientArmoredTurret`、`HunterDroneTrap`、`WaspDroneTrap`、`AncientHatch`、`AncientHatchExit`，以及所有 Odyssey 的 LayoutRoomDef、PrefabDef、RoomPartDef（例如 `CornerArmoredTurret`、`HunterDrone`、`WaspDrone`）、`MapGen_HighValueCrate`、`MapGen_Scarlands*`
- **Ideology：** `AncientCommsConsole`、`AncientSecurityCrate`、`AncientEnemyTerminal`、`AncientTerminal`、`AncientComplex`（版面）、`MapGen_AncientComplex_SecurityCrate`

### 11.2 有 DLC 閘門的類別

`GenStep_AncientStockpile`（DMS 已經用 `DMS_GenStep_Vault` 取代）、`TileMutatorWorker_AncientStructure`、`RoomPart_SentryDrone`、`RoomPart_AncientEngine`、`RoomContents_InnerCourtyardRooms`。

`LayoutWorker_AncientStockpile` 本身沒有閘門，但寫死了 `AncientBlastDoor`，所以 DMS 另外寫了 `DMS_LayoutWorker_Vault`。

### 11.3 和 Vault 設計衝突的

- `shouldDamage`、`wallDamageRange`、`RoomPart_Breached`：會拆或打壞牆，和不可破壞的 `DMS_SuperWall` 以及分區設計衝突。只適合地表廢墟。
- `canRemoveBorderWalls`：同上。
- `ensureOneDoorUnlocked`：需要可駭入的門，這類 def 全在 DLC。

---

## 12. 已知問題

### 12.1 `Site_LegacyInstallations.xml` 引用了 DLC 建築

`1.6/NewContent/Defs/GenStructures/Site_LegacyInstallations.xml` 用 `FFF_Element_Thing`／`FFF_Element_ThingScatter` 直接引用了下列 def，而且沒有加 `MayRequire`：

| def | 來源 | 行號 |
|---|---|---|
| `AncientCommsConsole` | Ideology | 2977 |
| `AncientEmergencyLight_Red` | Odyssey | 3146、14145、14408、14560 |
| `AncientForklift` | Odyssey | 3413、14403、14416 |

沒裝對應 DLC 時，這些會變成找不到的 def。實際會噴錯還是少放物件，要看 FFF 怎麼處理，**沒有實測**。建議的 Core 替代：

- `AncientEmergencyLight_Red` → `DMS_EmergencyLamp`（Vault 已經在用）或 `AncientLamp`
- `AncientForklift` → `AncientRustedEngineBlock` 或 `AncientLargeCrate`
- `AncientCommsConsole` → `AncientMachine`，或一個繼承 `AncientTerminalBase` 的 DMS 控制台

### 12.2 `AncientSecurityTurret` 只是裝飾

NewContent 裡引用了 14 次。它是 `Building`，沒有任何砲塔 comp，不會開火。如果某處的本意是要會攻擊的砲塔，需要換成 DMS 的砲塔。

---

## 13. 建議實作順序

1. **不用改 C#**
   - 機兵艙掛 `RoomPart_GestationTankDef`。
   - 新增冬眠守備房（`RoomContents_CryptosleepCaskets_Hostile`）。
   - 新增「開箱就伏擊」的物資房：`RoomPart_CrateDef`（DMS 戰利品）加 `RoomPart_DormantMechCluster`。
2. **不用改 C#**
   - 用 `wallAttachments`、`scatter`、`scatterTerrain`、`spawnJunk` 讓各房型的細節更有區別。
   - 用 `canRemoveBorderDoors` 做出套房。
   - 用 prefab 的 `stackCountRange` 直接擺物資，用 `hp` 做出受損的設備。
3. **修正：** 替換 §12.1 的 DLC 引用。
4. **小改 C#：** 把威脅點數帶進地下地圖（§10.3）。
5. **中改 C#**
   - 讓 DMS 警報和 `room.ThreatSignal` 連動（§10.1）。
   - 接上 ComplexThreat 預算系統（§10.2）。

---

## 14. 原版檔案索引

| 內容 | 位置 |
|---|---|
| Core 遠古建築 | `Data/Core/Defs/ThingDefs_Buildings/Buildings_Ancient.xml`、`Buildings_Ancient_Active.xml`、`Buildings_Ancient_Indoors.xml`、`Buildings_Ancient_Outdoors.xml`；冷凍艙在 `Buildings_Misc.xml`；`CrateBase` 在 `Buildings_Base.xml` |
| Core Prefab | `Data/Core/Defs/PrefabDefs/CommonRoomPrefabs.xml` |
| Core 房型、威脅 | `Data/Core/Defs/LayoutRoomDefs/LayoutRooms_AncientRuins.xml`、`Data/Core/Defs/ComplexThreatDefs/ComplexThreats_Misx.xml` |
| Core 戰利品表 | `Data/Core/Defs/ThingSetMakerDefs/ThingSetMakers_MapGen.xml` |
| Core 地下 GenStep | `Data/Core/Defs/MapGeneration/CommonMapGenerator.xml` |
| （參考用）Odyssey 版面、房型 | `Data/Odyssey/Defs/LayoutDefs/Layouts_AncientRuins.xml`、`Layouts_Misc.xml`、`LayoutRoomDefs/LayoutRooms_AncientRuins.xml`、`LayoutRooms_Special.xml`、`RoomPartDefs/RoomParts_Common.xml` |
| C# 關鍵類別 | `LayoutWorker`、`LayoutWorker_Structure`、`LayoutWorkerComplex`、`RoomContentsWorker`、`RoomGenUtility`、`LayoutRoomDef`、`StructureLayoutDef`、`PrefabDef`、`RoomPart_*`、`RoomContents_*`、`ComplexThreatWorker_*`、`CompMechGestatorTank`、`Building_AncientCryptosleepCasket`、`Building_Crate` |
