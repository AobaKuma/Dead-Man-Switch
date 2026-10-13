# DMS2 → RimWorld 1.7 打包實作流程

> **目標**：`DMS2-Dev` 的 2.0 內容整體作為 **1.7 版**發布；**1.4／1.5／1.6 凍結在 Workshop 現行的 1.8.27**，不受 DMS2 影響。
> **貼圖**：舊版貼圖放在 `1.6/Shared/Textures`，由 1.4／1.5／1.6 共用；新版貼圖放在 `1.7/Textures`，1.7 只讀新版。

## 進度（2026-10-13，分支 `DMS2-1.7`）

| 階段 | 狀態 | Commit |
|---|---|---|
| 1 準備 | ✅ | `b3a96dae`（Lanius）、`d498ffeb`（合併 `origin/main`）；標籤 `dms-1.8.27-final` → `810112dc` |
| 2 資料夾搬遷 | ✅ 數量與內容皆與凍結點／DMS2 相符 | `ee6056fd` |
| 3 載入設定 | ✅ | `ceeab9d6` |
| 4 建置 | ✅ 0 錯誤，28 個 CS0649 警告 | `58cf9087` |
| 5 1.7 專屬調整 | ✅ | `.rimignore`／`.gitignore`／README 的 commit |
| 6-1 與 Workshop 比對 | ✅ 只剩預期差異（Monolyn UnitSuit 貼圖大小寫：git 版的 `Apparel_Thin` 與其他套裝一致，比 Workshop 版正確） | — |
| 6-2／6-3 遊戲內測試 | ⏳ 待進行 | — |
| 7 發布 | ⏳ 等 FFF 1.7 上架 Workshop | — |
> 盤點日期：2026-10-13 · 遊戲 1.7.4976 rev1043 · 參考文件：FFF 的 `_Sources/Fortified/Docs/RimWorld_1.7_Update.md`、`RimWorld_1.7_vs_1.6.md`

---

## 0. 盤點結果（已實際驗證）

| 項目 | 結果 | 對流程的影響 |
|---|---|---|
| C# 對 1.7 編譯 | 以 `net10.0` + 遊戲本體 DLL + `Lib.Harmony 3.0.0-rc.2` + FFF 1.7 `Fortified.dll` 試編 130 個 .cs：**0 錯誤**，28 個 CS0649 警告（DefOf 欄位，正常） | 原始碼不用改，只改建置設定 |
| Harmony 補丁 | 8 個檔案、16 個補丁類別；無 Transpiler、無字串反射；`__instance` 型別皆與目標一致；以字串指定的 3 個目標方法（`BossgroupEverCallable`、`GetSingleOptionFor`、`IncidentChanceFinal`）在 1.7 都存在 | Harmony 3 的嚴格檢查預期可通過，仍需進遊戲確認 |
| Publicizer | 13 個 `<Publicize>` 在 1.7 試編中全部可用 | 照搬 |
| 著色器／AssetBundle | 本模組沒有自訂著色器或 AssetBundle；XML 只用原版 `shaderType` | 不用處理 |
| 塗裝 | 機兵使用 `Fortified.CompProperties_Paintable` | 依賴 FFF 的 Vulkan 著色器 bundle（FFF 自己也還沒在遊戲內確認畫面） |
| 音效 | 47 個 `.ogg`、1 個 `.wav` | 1.7 改用引擎內建音訊，需抽測 |
| `.rimignore` | 有 `*.zip` | **會把 1.7 的 `Assets.zip` 一起排除**，必須修正 |
| 前置模組 | Harmony 的 Workshop 版已有 1.7；FFF 1.7 已在本機完成（`fa02d56`），但 **Workshop 版還沒有 1.7** | **FFF 1.7 上架是 DMS 1.7 發布的前提** |
| Workshop 現行版 | 1.8.27，支援 1.4／1.5／1.6；內容與 commit **`810112dc`**（2026-08-03）一致（只差 Monolyn 3 張貼圖檔名大小寫與空資料夾） | 作為 1.6 凍結點 |
| 凍結的 1.6 搭配新版 FFF | 1.8.27 的 `DMS.dll` 沒有參考 `Fortified.dll`；1.6 XML 引用的 106 個 `Fortified.*` 型別在目前的 FFF 1.6 中都還在 | 凍結版可直接搭配新版 FFF（欄位層級未逐一驗證） |
| 根目錄 `Textures/` | 1.8.27：881 個檔案（19.7 MB）；DMS2：1,086 個（17.4 MB），其中 590 個與舊版完全相同。`810112dc` → `DMS2-Dev` 共新增 280、修改 212、刪除 75、改名 4。兩邊都沒有 `1.6/Textures`，搬移時不會有路徑衝突；`Defs/`、`Sounds/` 兩版無差異 | 舊版移到 `1.6/Shared/Textures`，新版移到 `1.7/Textures`（階段 2） |
| 分支狀態 | `main` 有 4 個 commit 不在 `DMS2-Dev`（pr/182～188 等）；`origin/main` 比本機 `main` 多 3 個 | 搬遷前先同步 |
| 工作區 | `1.6/NewContent/Defs/Things_Race/Races_Automatroid_Lanius.xml` 尚未提交 | 搬遷前先提交 |

---

## 1. 決策

**已決定**

| # | 決定 |
|---|---|
| D1 | 1.6 **凍結**在 1.8.27（`810112dc`），2.0 內容只提供給 1.7 |
| D2 | **保留** 1.4／1.5，從凍結點一起還原（`810112dc` 內有 871 個檔案） |
| D5 | **貼圖分版本存放**：舊版貼圖（1.8.27 的根目錄 `Textures/`）放在 `1.6/Shared/Textures`，由 1.4／1.5／1.6 共用；新版貼圖（DMS2 的根目錄 `Textures/`）放在 `1.7/Textures`。1.7 不再讀舊版貼圖，兩組完全分開 |

> **為什麼是 `1.6/Shared` 而不是 `1.6/Textures`**：載入資料夾會整個一起載入（`Assemblies`、`Defs`、`Patches`、`Textures`…）。1.4／1.5 若把 `1.6` 列進載入清單，會連 1.6 的 `DMS.dll` 和 Defs 一起載入。所以舊版貼圖放在 1.6 底下一個**只含貼圖**的子資料夾，三個版本都把它列進載入清單。

**待定（有預設值）**

| # | 問題 | 預設 |
|---|---|---|
| D3 | `modVersion` | 只有一個欄位，各版本共用 → 設為 `2.0.0`，再用 `descriptionsByVersion` 說明 1.4～1.6 是 1.8.27 維護版 |
| D4 | 8/3 之後的 1.6 修正（pr/182～188）要不要給 1.6 玩家？ | 不放進凍結點；真的需要的修正之後以 hotfix 方式補進 `1.6/`（階段 8） |

---

## 2. 目標結構

```
Dead-Man-Switch/
├─ About/                    # supportedVersions: 1.4 1.5 1.6 1.7；modDependenciesByVersion
├─ LoadFolders.xml           # v1.4～v1.6 沿用 1.8.27 並加入 1.6/Shared；新增 v1.7
├─ Defs/  Sounds/            # 根目錄只剩這兩個，四個版本共用（兩版內容相同）
├─ 1.4/  1.5/                # 凍結：1.8.27 的內容
├─ 1.6/                      # 凍結：1.8.27 的內容，含 1.8.27 的 DMS.dll（net472）
│  └─ Shared/
│     └─ Textures/           # 舊版貼圖（1.8.27 根目錄的 881 個檔案），1.4／1.5／1.6 共用
└─ 1.7/                      # DMS2 全部內容（原 DMS2-Dev 的 1.6/）
   ├─ Assemblies/DMS.dll     # net10.0 建置
   ├─ Textures/              # 新版貼圖（DMS2 根目錄的 1,086 個檔案）
   ├─ Defs/ Patches/ Languages/ NewContent/
   ├─ Royalty/ Ideology/ Biotech/
   ├─ CE/                    # 保留但 v1.7 不載入（CE 尚無 1.7）
   └─ Mod/                   # 各相容模組
```

- 1.7 不載入 `1.6/Shared`，1.4～1.6 不載入 `1.7`，兩組貼圖完全分開，不依賴同路徑覆蓋。
- `1.6/Shared` 在載入清單中緊接在 `/` 之後，也就是過去根目錄 `Textures/` 的位置，所以 1.4～1.6 的貼圖優先順序與 1.8.27 相同（例如 `1.6/Mod/Monolyn/Textures` 仍會蓋過共用貼圖）。
- 代價：590 個兩版相同的貼圖會存兩份，模組總大小增加約 8.2 MB。

---

## 3. 實作步驟

### 階段 1：準備

```bash
git add 1.6/NewContent/Defs/Things_Race/Races_Automatroid_Lanius.xml
git commit -m "Update Lanius"       # 先提交未完成的修改
git fetch origin
git switch DMS2-Dev
git merge origin/main               # 把 pr/182～188 等修正併進 DMS2
git switch -c DMS2-1.7              # 搬遷在獨立分支上做，完成後再併回
git tag dms-1.8.27-final 810112dc   # 標記 1.6 凍結點
```

環境：.NET 10 SDK（本機已有 10.0.400 ✓）、`../_Fortified-Framework/1.7/Assemblies/Fortified.dll`（✓）。

### 階段 2：資料夾搬遷（用 git 指令，保留歷史）

```bash
FREEZE=dms-1.8.27-final

# 2-1  DMS2 的 1.6 整個改名成 1.7
git mv 1.6 1.7

# 2-2  新版貼圖：DMS2 的根目錄 Textures 整個移到 1.7/Textures
git mv Textures 1.7/Textures

# 2-3  還原凍結的 1.4／1.5／1.6（含 1.8.27 的 DMS.dll）
git checkout "$FREEZE" -- 1.4 1.5 1.6

# 2-4  舊版貼圖：還原 1.8.27 的根目錄 Textures，再移到 1.6/Shared/Textures
git checkout "$FREEZE" -- Textures
mkdir -p 1.6/Shared
git mv Textures 1.6/Shared/Textures
```

驗證（數量以合併 `origin/main` 後的實際值為準）：
- 根目錄不再有 `Textures/`，只剩 `Defs/`、`Sounds/`。
- `1.6/Shared/Textures` 有 881 個檔案，且與凍結點完全相同：`git diff --stat "$FREEZE":Textures "$(git write-tree --prefix=1.6/Shared/Textures/)"` 應為空（`write-tree --prefix` 取暫存區中該資料夾的樹）。
- `1.7/Textures` 約有 1,086 個檔案。
- `git diff --stat "$FREEZE" -- 1.4 1.5 1.6 Sounds Defs ':!1.6/Shared'` 應為空。
- 空的 `1.7/Odyssey/` 可刪除。`1.7/Mod/FTND`、`1.7/Mod/SaveOurShip2` 維持不載入。

### 階段 3：載入設定

**`LoadFolders.xml`**：`v1.4`～`v1.6` 取凍結點的版本（`git show dms-1.8.27-final:LoadFolders.xml`），每個區塊在 `<li>/</li>` 後面插入 `<li>1.6/Shared</li>`，其餘不動（1.8.27 的 `v1.6` **沒有** `1.6/NewContent`，維持原樣）：

```xml
<v1.4>
	<li>/</li>
	<li>1.6/Shared</li>
	<li>1.4</li>
	<!-- 以下照 1.8.27 -->
</v1.4>
<v1.5>
	<li>/</li>
	<li>1.6/Shared</li>
	<li>1.5</li>
	<!-- 以下照 1.8.27 -->
</v1.5>
<v1.6>
	<li>/</li>
	<li>1.6/Shared</li>
	<li>1.6</li>
	<!-- 以下照 1.8.27 -->
</v1.6>
```

再加上 `v1.7`（**不列** `1.6/Shared`；根目錄 `/` 現在只提供 `Defs/`、`Sounds/`）：

```xml
<v1.7>
	<li>/</li>
	<li>1.7</li>
	<li IfModActive="Ludeon.RimWorld.Royalty">1.7/Royalty</li>
	<li IfModActive="Ludeon.RimWorld.Ideology">1.7/Ideology</li>
	<li IfModActive="Ludeon.RimWorld.Biotech">1.7/Biotech</li>

	<!-- Combat Extended 尚無 1.7 版，1.7/CE 暫不載入 -->
	<li IfModActive="hlx.gestaltengine,hlx.gestaltengine_steam,mlie.gestaltengine,mlie.gestaltengine_steam,mlie.reinforcedmechanoid2,mlie.reinforcedmechanoid2_steam">1.7/Mod/GestaltEngine</li>

	<li IfModActive="vanillaracesexpanded.android,vanillaracesexpanded.android_steam">1.7/Mod/VREA</li>
	<li IfModActive="oskarpotocki.vfe.pirates,oskarpotocki.vfe.pirates_steam">1.7/Mod/VFEP</li>

	<li IfModActive="dimonsever000.factionlanguages,dimonsever000.factionlanguages_steam">1.7/Mod/RimLanguage</li>
	<li IfModActive="xmb.ancienturbanruins.mo,xmb.ancienturbanruins.mo_steam">1.7/Mod/AUR</li>

	<li IfModActive="issaczhuang.pla.armory,issaczhuang.pla.armory_steam">1.7/Mod/PLAA</li>
	<li IfModActive="samael.pca,samael.pca_steam">1.7/Mod/PCA</li>
	<li IfModActive="solaris.ratkinracemod,solaris.ratkinracemod_steam">1.7/Mod/RK</li>
	<li IfModActive="ASEL.MonolynRace,ASEL.MonolynRace_steam">1.7/Mod/Monolyn</li>

	<li>1.7/NewContent</li>
</v1.7>
```

**`About/About.xml`**：以 DMS2-Dev 的版本為底，修改：
- `supportedVersions`：`1.4`、`1.5`、`1.6`、`1.7`。
- 把 `modDependencies` 改回 `modDependenciesByVersion`：`v1.4`／`v1.5` 照抄凍結點（Harmony、Biotech、VFE Core）；`v1.6`、`v1.7` 為 Harmony、Biotech、`AOBA.Framework`。
- `modVersion` → `2.0.0`（D3）；新增 `descriptionsByVersion`，`v1.4`～`v1.6` 註明「此版本為 1.8.27 維護版，2.0 內容僅提供給 1.7」。
- `loadAfter`、`incompatibleWith` 兩版相同，維持不變。

### 階段 4：建置（`_Source/DMS/DMS.csproj`）

1.6 的 DLL 已凍結，所以 DMS2 只需要建置 `net10.0` 一個目標。以下設定已在暫存目錄試編通過：

```xml
<PropertyGroup>
  <OutputType>Library</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <OutputPath>$(MSBuildThisFileDirectory)/../../1.7/Assemblies</OutputPath>
  <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
  <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  <LangVersion>10.0</LangVersion>
  <ImplicitUsings>disable</ImplicitUsings>
  <Nullable>disable</Nullable>
  <DefineConstants>$(DefineConstants);RW_1_7</DefineConstants>
  <!-- 遊戲只載入 Assemblies 內的 .dll，不需要 deps.json -->
  <GenerateDependencyFile>false</GenerateDependencyFile>
  <RimWorldDir Condition="'$(RimWorldDir)' == ''">$(MSBuildThisFileDirectory)..\..\..\..\</RimWorldDir>
</PropertyGroup>

<ItemGroup>
  <!-- 與 Harmony 模組 1.7 資料夾內的 0Harmony.dll 相同 -->
  <PackageReference Include="Lib.Harmony" Version="3.0.0-rc.2" ExcludeAssets="runtime" />
  <Reference Include="Assembly-CSharp"><HintPath>$(RimWorldDir)Assembly-CSharp.dll</HintPath><Private>False</Private></Reference>
  <Reference Include="UnityStubs"><HintPath>$(RimWorldDir)UnityStubs.dll</HintPath><Private>False</Private></Reference>
  <Reference Include="Waylith"><HintPath>$(RimWorldDir)Waylith.dll</HintPath><Private>False</Private></Reference>
  <Reference Include="Fortified"><HintPath>..\..\..\_Fortified-Framework\1.7\Assemblies\Fortified.dll</HintPath><Private>False</Private></Reference>
</ItemGroup>
<!-- Krafs.Publicizer 與 13 個 <Publicize> 保持原樣 -->
```

要移除的項目：`Krafs.Rimworld.Ref`、`Lib.Harmony 2.4.1`、`_Fortified-Framework\_Sources\Reference\` 下的 `0Harmony`／`UnityEngine.*` 參考（net10.0 下會造成型別重複 CS0433），以及 `System.*` 的 `Reference Update` 區塊。

建置與確認：
```bash
dotnet build _Source/DMS/DMS.csproj -c Release
grep -c -a ".NETCoreApp,Version=v10.0" 1.7/Assemblies/DMS.dll   # 應 ≥ 1；0 表示還是從 1.6 搬過來的 net472 舊檔
```
- `1.7/Assemblies/` 只能有 `DMS.dll`，不要夾帶 `0Harmony.dll` 或 `Fortified.dll`。
- `build.bat` 的輸出訊息改成 `1.7\Assemblies`。
- 遊戲執行中不要建置（DLL 被鎖住）。

### 階段 5：1.7 專屬調整

| 檔案 | 修改 |
|---|---|
| `.rimignore` | 在 `*.zip` 後加 `!Assets.zip`，並新增 `Assets.zip.*`（同 FFF 的做法） |
| `.gitignore` | 新增 `Assets.zip`、`Assets.zip.*` |
| `README.md` | 路徑 `1.6/` → `1.7/`、建置需求改為 .NET 10、載入資料夾說明、1.6 凍結政策 |
| `1.7/CE/` | 保留，等 CE 推出 1.7 後驗證補丁，再加進 `v1.7` |
| `1.7/Mod/*` | 保持條件載入；各相容模組推出 1.7 版時再逐一驗證（它們沒更新前不會被啟用，不影響載入） |

### 階段 6：測試

**6-1 · 1.4～1.6 以目錄比對代替實機測試**：1.4～1.6 玩家拿到的內容必須與現行 Workshop 版相同，只是舊版貼圖從根目錄搬到 `1.6/Shared/Textures`。

```bash
WS="D:/Steam/steamapps/workshop/content/294100/3121742525"
for d in 1.4 1.5 1.6 Sounds Defs; do echo "== $d"; diff -rq -x Shared "$WS/$d" "$d"; done
echo "== Textures"; diff -rq "$WS/Textures" 1.6/Shared/Textures
diff <(tr -d '\r' < "$WS/LoadFolders.xml" | sed -n '/<v1.4>/,/<\/v1.6>/p') <(tr -d '\r' < LoadFolders.xml | sed -n '/<v1.4>/,/<\/v1.6>/p')
```
預期結果：
- 目錄比對只剩兩類無害差異：Monolyn 3 張貼圖的檔名大小寫，以及 Workshop 端的空資料夾（`1.6/NewContent`、`1.6/Odyssey`、`1.6/Defs/Effects`、`Textures/UI/Icons`）。
- `LoadFolders.xml` 的差異只有三行新增的 `<li>1.6/Shared</li>`。

若手邊有 1.6 版遊戲（例如 Steam 測試版分支），建議再開一次確認 1.6 能讀到 `1.6/Shared` 的貼圖；1.4／1.5 的載入機制相同。

**6-2 · 1.7 隔離冒煙測試**（不影響平常的存檔與模組設定）：
```bash
"D:/Steam/steamapps/common/RimWorld/RimWorld.exe" -savedatafolder="D:/RW17Test" -mod="brrainz.harmony;AOBA.Framework;Aoba.DeadManSwitch.Core" -quicktest
```
- 確認載入的是本機的 `_Fortified-Framework`（Workshop 版尚無 1.7；與 Workshop 重複的 packageId 會被加上 `_steam`）。
- 記錄檔：`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`。

**6-3 · 遊戲內檢查清單**

- [ ] 記錄檔沒有 `Patching exception`、XML 錯誤、`Could not resolve cross-reference`、缺貼圖
- [ ] 16 個 Harmony 補丁類別都有套用（可用開發模式或 Harmony 的補丁清單確認）
- [ ] 各級機兵生成；貼圖全部來自 `1.7/Textures`，抽查 Soldat、Grenadier、Sergeant、Elevator 是否顯示為新版
- [ ] 塗裝視窗與迷彩預覽（FFF Vulkan 著色器）
- [ ] 任務：Supply Chain、Officer Training、Court Martial、Stele／Occultech Key、Document Processing、Bossgroup（開發模式產生）
- [ ] 金庫口袋地圖生成、設施伺服器多階段駭入（`CompServerHackable` 用到 publicize 的 `CompHackable` 欄位）
- [ ] Frogman 胚胎植入與出生、Cluster Weaver 無人機投放、說書人 Greysia
- [ ] 音效抽測（CAS、砲擊、無線電）
- [ ] 貼圖壓縮後是否有半透明邊緣或細節失真（1.7 會把圖集壓成 BC 格式）
- [ ] DLC 組合：只有 Core＋Biotech／全部 DLC
- [ ] 1.7 存檔、讀檔往返
- [ ] 拿 1.8.27 的舊存檔在 1.7 + DMS2 讀取，記下錯誤，決定要做相容處理，還是在說明中註明「不建議沿用舊存檔」

### 階段 7：發布

1. **先等 FFF 1.7 上架 Workshop**（硬相依；目前 Workshop 版只有 1.5／1.6）。
2. 把 `DMS2-1.7` 併回 `DMS2-Dev`，再併進 `main`，打上 `2.0.0` 標籤。
3. 上傳時勾選「建立資源包」，**不要勾「略過原始貼圖」**（1.4～1.6 讀不懂 `Assets.zip`，會缺貼圖）。
4. 上傳前確認：`1.6/Assemblies/DMS.dll` 與凍結點的檔案相同（`git diff --quiet dms-1.8.27-final -- 1.6/Assemblies`），`1.7/Assemblies/DMS.dll` 是 net10.0 版。
5. 檢查上傳暫存複本：若烘焙流程也在 `1.6/Shared` 等 1.4～1.6 的資料夾產生了 `Assets.zip`，可在 `.rimignore` 排除（1.6 以下讀不懂 `Assets.zip`，1.7 也不會載入那些資料夾）。
6. 更新說明與變更紀錄：1.4～1.6 維持 1.8.27，只修嚴重錯誤；2.0 內容只提供給 1.7；附上舊存檔相容性說明。

### 階段 8：之後的維護規則

- **`1.4/`、`1.5/`、`1.6/`（含 `1.6/Shared`）全部凍結**。新貼圖一律放 `1.7/Textures`（或 `1.7/NewContent/Textures` 等 1.7 的載入資料夾）。
- **根目錄只剩 `Defs/`、`Sounds/`，四個版本共用**。要改動前先確認 1.4～1.6 也相容，否則改放到 `1.7/` 下。
- 檢查方式（可放進 PR 審查）：
  ```bash
  git diff --name-only dms-1.8.27-final -- 1.4 1.5 1.6 Sounds Defs ':!1.6/Shared'   # 應為空
  git diff --stat dms-1.8.27-final:Textures HEAD:1.6/Shared/Textures                # 應為空
  ```
- **1.6 hotfix**：從 `dms-1.8.27-final` 開分支修改，修好後用 `git checkout <hotfix> -- 1.6` 帶回主線（注意不要蓋掉 `1.6/Shared`），再打新標籤。
- FFF 每次改版時，確認 1.6 XML 引用的 `Fortified.*` 型別仍然存在（可用本次盤點的掃描方式）。
- CE 推出 1.7 後：驗證 `1.7/CE`，再加進 `v1.7`。

---

## 4. 風險

| 風險 | 機率 | 對策 |
|---|---|---|
| FFF 1.7 延後上架 | 中 | DMS 1.7 與 FFF 1.7 同步發布；在那之前只在本機測試 |
| Harmony 3 讓某個補丁失敗，使 `PatchAll` 中止 | 低（已靜態檢查） | 6-3 第一、二項 |
| 塗裝著色器在 Vulkan 下失效 | 中 | 失效時會退回原版著色器（不會報錯，但迷彩消失）；需要 FFF 端處理 |
| 舊存檔升級到 DMS2 後出錯 | 高 | 6-3 最後一項；在說明中警告 |
| 有人把新貼圖放進 `1.6/Shared`，或改動根目錄的 `Defs/`、`Sounds/`，破壞 1.4～1.6 | 中 | 階段 8 的檢查規則 |
| 1.4～1.6 讀不到 `1.6/Shared` 的貼圖（載入清單寫錯） | 低 | 6-1 的 `LoadFolders.xml` 比對；有 1.6 遊戲時實機確認 |
