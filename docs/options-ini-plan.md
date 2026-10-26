# OptionsWindow / GameLobbyBase ini 驱动改造方案

> 状态：**待评审**（方案文档，尚未改代码）。
> 上位背景见 [spec.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/spec.md) 与 [mod-customization.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/mod-customization.md)。
> 决策记录见本文 §6「已确认的决策」。

---

## 1. 背景与目标

CNCNet 客户端的设计初衷是**客户端与 mod 解耦**：mod 作者提供 ini 即可改变选项与设置，无需改客户端源码。
移植到 Blazor 后，这一层被搬进了 C#：`GameSessionOption.cs` 把 12 复选框 + 2 下拉全部硬编码，
`Settings.razor` 把 7 个分区的全部控件、文案、取值范围硬编码，代码库对
`Resources\GameLobbyBase.ini` / `Resources\OptionsWindow.ini` **零引用**。

后果：mod 改这两个 ini **完全无效**，必须改 C# 重编译——而 mod 作者本不该碰客户端源码。

**目标**：把功能语义交还给 ini，只保留纯 UI 几何为代码/CSS 侧。

---

## 2. 关键事实（对齐原版的前提）

调研实测（2026-10-19）：**原版本身也不是"全 ini"**。

| 层 | 原版实际做法 | 证据 |
|---|---|---|
| 控件清单（游戏大厅） | **ini 声明**：`[GameOptionsPanel]` 的 `$CC-GO*` **类型化控件集合**决定清单与顺序 | `GameLobbyBase.ini:227-251`；类型为 `GameLobbyCheckBox` / `GameLobbyDropDown` 的才是游戏选项 |
| 控件清单（设置页） | **硬编码在 C# 里**（代码创建控件） | 翻译键 `INI:Controls:<面板>:<控件>:Text=` 即代码建控件的产物，见 `bak\Resources\Translations\ru\Translation.ini` |
| `OptionsWindow.ini` | 只管三件事：① `[XXXPanelExtraControls]` 额外控件注册 ② 各控件节属性覆盖 ③ 值绑定键 | `bak\Resources\OptionsWindow.ini` |
| `GenericWindow.ini` | 只有窗口边框等通用件，**不含控件清单** | `bak\Resources\GenericWindow.ini`（139 行，节多为 `panelBorder*`） |
| `SkirmishLobby.ini` / `MultiplayerGameLobby.ini` | `BasedOn=GameLobbyBase.ini`，再追加**模式专有**选项（`cmbGameSpeedCapSkirmish` / `cmbGameSpeedCapMultiplayer`，经 `$BaseSection=cmbGameSpeedCap` 继承属性） | 两个文件各约 80 行 |

对照：`chkAutoReady`（`MultiplayerGameLobby.ini:21`）注册为 `$CCMP01=chkAutoReady:XNAClientCheckBox`，
虽以 `chk` 开头却**不是**游戏选项 —— 说明**控件类型**（而非节名前缀）才是判据。

**因此本方案按两种机制分别对齐原版（2026-10-19 实测修正）**：
- **游戏大厅游戏选项**：清单**在 ini**（`$CC-GO*` 类型化集合 + 按模式继承），代码不再硬编码；
- **设置页**：清单**在代码**，ini 负责额外控件注册 / 属性覆盖 / 值绑定 / 文件开关。

### 2.1 一处必须记录的事实（源码注释有误）

`GameLobbyBase.ini` 中 **不存在 `BroadcaseToLobby` 键**（逐节核查过；该节所有键见 §4.1）。
`GameSessionOption.cs` 的注释称"广播集同原版 `BroadcaseToLobby=yes`"，属**引用错误**。
广播集实际来自原版**代码默认值**，故这部分**不在 ini 范围内**，保留代码定义。

---

## 3. 豁免清单（不读 ini，由代码/CSS 承接）

| 类别 | 键 | 承接方式 |
|---|---|---|
| 坐标/尺寸 | `Location` / `$X` / `$Y` / `$Width` / `$Height` / `Size` / `FillWidth` | 布局令牌 + CSS（§4.4） |
| 绘制/皮肤 | `DrawMode` / `BackgroundTexture` / `IdleTexture` / `HoverTexture` | CSS / 主题目录 / 插件 CSS |
| 布局表达式 | `getX(...)` / `getBottom(...)` 等 ini 侧的相对定位表达式 | 响应式 grid/flex 天然替代 |
| 现有 UI 结构 | `Settings.razor` 的 7 分区、各分区内部控件排布 | 属 UI 布局，保留不动 |

> 理由（比"HTML 没这概念"更硬）：原版 `Location=285,180` 是**绑死固定画布的绝对像素**；
> 移植版要在 1280×720 到 4K 之间自适应，照搬绝对坐标会让"能改"变成"改了就坏"。
> 正确的抽象是把 grid 的列宽/间距/顺序抽成可配置项。

---

## 4. 设计

### 4.1 游戏选项（`Resources\SkirmishLobby.ini` / `MultiplayerGameLobby.ini` → `GameLobbyBase.ini`）

**ini 键语义（实测自 `GameLobbyBase.ini`）**

复选框 `[chk*]`：

| 键 | 语义 | 移植版对应 |
|---|---|---|
| `Text` | 显示文案 | 现硬编码的第 1 参 |
| `SpawnIniOption` | 写入 spawn.ini 的键名 | 现 `spawnIniOption` 参数 |
| `CustomIniPath` | 自定义 ini 路径（`INI/Game Options/*.ini`） | 现 `customIniPath` 参数 |
| `Checked` | **默认勾选**（注意键名不是 `DefaultValue`） | 现 `defaultChecked` 参数 |
| `Reversed=yes` | 勾选语义反转 | 现 `reversed` 参数 |
| `ToolTip` | 悬浮说明 | 现 `ToolTip` 属性（UI 渲染为 `title`） |

下拉 `[cmb*]`：

| 键 | 语义 | 移植版对应 |
|---|---|---|
| `OptionName` | 逻辑名 | 现构造参数 |
| `Items` | 逗号分隔选项 | 现 `Items` |
| `DefaultIndex` | 默认选中项 | 现 `defaultIndex` |
| `SpawnIniOption` | spawn.ini 键名 | 同左 |
| `DataWriteMode` | 写入格式（实测出现 `String` / `Index`） | 现 `dataWriteMode` |
| `Enabled` / `Visible` | 启/显 | **不收录该选项**（如 `cmbTechLevel` 为 `no`）；mod 改回 `yes` 即出现，无需改代码 |
| `ToolTip` | 悬浮说明 | 同左 |

**特例**：`[cmbGameSpeedCap]` 是**继承用基节**（`Items` 被注释掉、`DefaultIndex=2`、`DataWriteMode=Index`、`SpawnIniOption=GameSpeed`）——
它**不直接在** `[GameOptionsPanel]` 声明，只作为 `cmbGameSpeedCapSkirmish` / `cmbGameSpeedCapMultiplayer`（各自提供 `Items`）的 `$BaseSection` 基节。
因此"下拉声明必须带 `Items`"仍成立（缺失即严格报错），无需为 `[cmbGameSpeedCap]` 开特例。

**解析设计（已实施并实测，2026-10-19）**

- 新增 `Infrastructure/Options/GameOptionsIniLoader.cs`：按模式读
  `SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), "SkirmishLobby.ini" | "MultiplayerGameLobby.ini")`。
- 用 **`CCIniFile`**（`CnCNetClient.Core/CCIniFile.cs`）而非裸 `IniFile`：它已实现
  `[INISystem] BasedOn` **文件继承链**（递归）与 `$BaseSection` **节继承**——后者让
  `cmbGameSpeedCapSkirmish` 自动获得 `[cmbGameSpeedCap]` 的 `OptionName` / `SpawnIniOption` / `DataWriteMode` / `DefaultIndex`，只需自带 `Items`。
- 取 `[GameOptionsPanel]` 中 `$CC*` 声明 `<名称>:<类型>`：类型 `GameLobbyCheckBox` → 复选框、`GameLobbyDropDown` → 下拉框，
  其余（`XNALabel` / `XNAControl` / `XNAClientButton`…）跳过；按键内序号排序，**复选框在前（即 GO 位序）、下拉框在后**。
  ⚠️ `GetSectionKeys` 对不存在的节返回 `null`（`IniFile.cs:728`）——必须判空。
- **排除 `Enabled=no` / `Visible=no` 的选项**（如 `cmbTechLevel`）；mod 改回 `yes` 即重新出现，无需改代码。
- `CreateDefaultGameOptions()` 退居**广播集模板**（ini 无 `BroadcaseToLobby` 键，见 §2.1），并补充模式专有的 `cmbGameSpeedCapSkirmish` / `cmbGameSpeedCapMultiplayer`。
- 消费方：`SkirmishService.cs:93`（`SKIRMISH_INI`）、`GameRoomService.cs:141`（`MULTIPLAYER_INI`）。
- GO 位序：`GameRoomService.cs:1185`（`GetGameOptionsCode`）/ `1199`（`ApplyGameOptionsCode`）**未改动**，顺序自然跟随 ini。
- 实测结果：两模式均 **12 chk（位序与原硬编码逐位一致）+ 3 下拉**（Credits / Game Speed / Starting Units）——
  比移植前多出原版本就有、移植时遗漏的 **Game Speed**。

**尚未承接**：见 §7「已知取舍」。

### 4.2 设置页（`Resources\OptionsWindow.ini`）

**注册机制**

```ini
[DisplayOptionsPanelExtraControls]
0=chkMEDDraw:FileSettingCheckBox
```

- 格式：`<序号>=<控件名>:<控件类型>`；**根节 `[ExtraControls]` 为空**（原版实测）。
- 面板名 → 移植版分区映射：

| 原版面板 | 移植版分区 |
|---|---|
| `DisplayOptionsPanel` | `display` |
| `AudioOptionsPanel` | `audio` |
| `GameOptionsPanel` | `game` |
| `CnCNetOptionsPanel` | `network` |
| `UpdaterOptionsPanel` | `update` |
| `ComponentsPanel` | `components` |
| （无对应） | `privacy`（移植版独有，不接受 ExtraControls） |

**控件属性（实测自 `OptionsWindow.ini`）**

| 键 | 语义 |
|---|---|
| `Text` | 文案（缺失时回退代码内置文案——原版多数控件文案走翻译体系 `INI:Controls:…`） |
| `ToolTip` | 说明 |
| `DefaultValue` | 默认值 |
| `SettingSection` / `SettingKey` | **值绑定**：读写哪个 ini 的哪个节/键（如 `Video` / `UseDDWrapperForMapEditor`） |
| `EnabledFile<n>` | `源,目标[,策略]`，勾选时执行文件操作（§4.3） |
| `ParentCheckBoxName` / `ParentCheckBoxRequiredValue` | 联动：父开关满足值时才可用 |
| `Enabled` / `Visible` | 启/显（`chkWriteInstallPathToRegistry` 即 `Enabled=false`+`Visible=false`） |

**控件类型**：原版实测**仅** `SettingCheckBox` 与 `FileSettingCheckBox`（后者仅 `chkMEDDraw` 一例）。
故移植版**只支持这两种**，不发明 `SettingDropDown` / `SettingTextBox` / `SettingSlider` 等原版不存在的类型
（现有 UI 的下拉 / 文本 / 滑块属**内置控件**，由代码清单创建，不接受 ExtraControls 注册）。

**关键技术点（实施结果，2026-10-19）**

1. **值绑定通用读写已存在**：`UserINISettings.SetValue(section,key,…)` / `GetValue(section,key,…)`
   （`CnCNetClient.Core\Settings\UserINISettings.cs:372-388`，各有 string/bool/int 三个重载），
   落盘走 `SaveSettings()` → `SettingsIni.WriteIniFile()`（`UserINISettings.cs:483-494`）。
   **无需新增 API**——直接读写任意节/键，`[Video] UseDDWrapperForMapEditor` 实测可持久化。
2. **未知控件类型 / 缺值绑定** → **严格报错**（`ClientConfigurationException`）：类型不认识、或注册控件未同时声明
   `SettingSection`+`SettingKey`，均视作配置错误。**不**静默跳过。
3. **文案来源优先级**：对**额外控件** `ini Text → 控件名`；对**内置控件** `ini Text → 代码内置文案`
   （对照表见下）。⚠️ 实测效应：随包的 `OptionsWindow.ini` 中 `[chkPersistentMode]` / `[chkDiscordIntegration]`
   带 `Text=` 覆盖（英文），故这两个内置开关的中文文案会被 ini 覆盖为英文——见 §7。
4. **内置控件对照表（移植版草稿键 ↔ 原版控件名）**：仅名称可对应者接受 `Text` / `Enabled` / `Visible` 覆盖与
   `ParentCheckBoxName` 联动：
   `clientRes`↔`ddClientResolution`、`playerName`↔`tbPlayerName`、`skipConnectDialog`↔`chkSkipLoginWindow`、
   `persistentMode`↔`chkPersistentMode`、`autoConnect`↔`chkConnectOnStartup`、`discord`↔`chkDiscordIntegration`、
   `steam`↔`chkSteamIntegration`。移植版特有控件（语言 / 主题 / 音量 / 渲染器等）在原版 ini 中无对应节，不接受覆盖。
5. **`privacy` 分区**：移植版独有，无原版面板映射，不接受 ExtraControls。

**已实施（2026-10-19）**：新增 `Infrastructure/Options/OptionsWindowIni.cs`
（`OptionsWindowIni.Load` / `OptionsWindowConfig` / `SettingControl` / `SettingControlAttributes` / `EnabledFileApplier`）；
`Settings.razor` 增 `Extras(section)` 渲染 + `CommitExtras()` 提交 + 内置控件 `IniText` / `IniRowClass` 覆盖。
实测（随包 ini）：`display` 分区注册 1 项 `chkMEDDraw:FileSettingCheckBox`，属性节解析出
`Video`/`UseDDWrapperForMapEditor` + 2 条 EnabledFile；`EnabledFile` 五种情形（Copied / Identical / TargetKept / Overwrite / SourceMissing）全部符合预期。

### 4.3 FileSettingCheckBox + `EnabledFile`

原版唯一实例 `chkMEDDraw`：

```ini
[chkMEDDraw]
EnabledFile0=Resources/Compatibility/DLL/ddwrapper.dll,Map Editor/ddraw32.dll,OverwriteOnMismatch
EnabledFile1=Resources/Compatibility/Configs/aqrit.cfg,Map Editor/aqrit.cfg,KeepChanges
SettingSection=Video
SettingKey=UseDDWrapperForMapEditor
```

- 格式 `源路径,目标路径[,策略]`；策略实测两种：`OverwriteOnMismatch`（目标已存在且内容不同则覆盖）/ `KeepChanges`（仅目标不存在时复制）。
- **已核实原版全文件无 `DisabledFile`** → **只实现勾选侧**：勾选时按策略复制；**取消勾选不删除 / 不回滚任何文件**，
  仅将 `SettingSection`/`SettingKey` 写为 `false`（与原版一致，不凭空假设删除）。
- **已实施（2026-10-19）**：`EnabledFileApplier.Apply()` 返回 `Copied` / `Identical` / `TargetKept` / `SourceMissing`；
  源文件不存在时**不报错、不阻断**，仅记 `LogWarning`（源是 mod 资源，可能未随包分发）。
  由 `Settings.razor` 的 `Extras` 渲染为开关（FileSettingCheckBox 与 SettingCheckBox 渲染一致，差异仅在提交时的文件复制）。

### 4.4 布局令牌（`wwwroot/css/app.css`）

把结构性布局值抽成 `--lay-*` 令牌，三档（`:root` / `:root[data-theme="inner"]` / `:root[data-theme="ra"]`）按实际差异给值，
并同步断点内的覆盖。落点（实测）：

| 选择器 | 现硬编码布局值 | 位置 |
|---|---|---|
| `.settings-grid` | `grid-template-columns:200px minmax(0,1fr)` | `app.css:1209-1254` |
| `.settings` / `.set-nav` / `.set-panel` | `padding:64px 24px 52px` / `gap:12px` 等 | 同上 |
| `.lobby` / `.lobby-grid` | `flex-direction:column` / `padding:64px 24px 52px` / `gap:12px` | `app.css:403-457` |
| `.groom` / `.groom-grid` | `padding:60px 16px 40px` / `gap:10px` / `grid-template-columns`+`rows` | `app.css:706-762` |
| 主菜单 `.stage` / `.left` / `.panel` | `padding:0 4.5vw` / `gap:36px` / `max-width:56%` / `padding:26px 22px` | `app.css:181-234` |
| 顶栏（`.topbar` 系） | `position:fixed` / `gap` / `padding` | `app.css:117-159` |

- **注意** `app.css` 无 `@layer`、仅 1 处 `!important` → 覆盖靠"后注入 + 特异性"。
- 内页需写 `:root[data-theme="inner"]` 档；外部主题（`ThemeProvider.LoadExternalThemes`，`ThemeProvider.cs:127-160`）
  加载时**移除 `data-theme`**，故外部主题/插件只需覆盖 `:root` 即可生效。

**已实施（2026-10-19）**

- 在 `:root` 定义 20 个 `--lay-*` 令牌（`app.css:36-50`），默认值与**原硬编码逐字节相同**（故默认渲染不变）。
- 令牌落点全部改为引用令牌：`.hud` / `.foot` / `.stage` / `.left` / `.panel` / `.lobby` / `.lobby-grid` /
  `.groom` / `.groom-grid` / `.settings` / `.settings-grid` / `.set-nav` / `.set-panel`；
  新增 `--lay-hud-padx/pady`、`--lay-foot-padx/pady` 覆盖顶/底栏（原 `.topbar` 类即本移植版的 `.hud` / `.foot`）。
- 断点改为**只覆盖令牌**：`@media(max-width:1440px) and (max-height:820px)` 与 `@media(max-width:920px)`
  各在 `:root` 内覆盖 `--lay-*`（值与原逐条硬编码等价），各选择器自动跟随，源序保持（920 后于 1440，主菜单纵向堆叠仍生效）。
- **三档说明**：`:root` 为基准档；`:root[data-theme="inner"]` / `:root[data-theme="ra"]` 实测**仅有颜色差异、无布局差异**，
  故**不额外赋值**（避免凭空造差异）；两档及外部主题/插件仍可随时覆盖同名 `--lay-*` 令牌实现按主题改布局。
- 校验：`app.css` 花括号配对（919/919）、编译 0 错误。

---

## 5. 兜底与错误处理（已确认：严格 + 不加校验）

| 场景 | 处理 | 依据 |
|---|---|---|
| ini 缺失 / 关键节缺失 | **报错**（不静默降级） | 对齐原版 `DirectDrawWrapperManager.cs:33-66` 的 `throw ClientConfigurationException` 范式 |
| 单节解析失败 | 按严格模式：报错 | 同上 |
| GO 位序两端不一致 | **不校验** | 对齐原版，靠"同一 mod 同一 ini"约定（下表风险） |

> 与 F4 插件机制的区别：那里"任何异常不得阻断启动"是针对**外部插件**；
> 游戏资源 ini 属于 mod 必备文件，严格处理与原版一致。

---

## 6. 已确认的决策

1. **机制对齐原版**（2026-10-19 实测修正）——按两种机制分别处理：
   - **游戏大厅游戏选项**：清单**在 ini**（`[GameOptionsPanel]` 的 `$CC-GO*` 类型化集合 + 按模式继承），代码不再硬编码；
   - **设置页**：清单**在代码**，ini 负责额外控件注册 / 覆盖 / 绑定 / 文件开关（§4.2 保持此机制）。
2. **豁免纯 UI 几何**（§3）：`Location` / `DrawMode` 类不读 ini。
3. **严格以 ini 为准**：不设代码默认集兜底。
4. **GO 顺序不加校验**。
5. 布局令牌**只做令牌化**，不引入 `[Layout]` ini 节。

---

## 7. 风险与已知取舍

| 风险 | 影响 | 处置 |
|---|---|---|
| GO 位序无校验 | 房主/加入者 ini 不同 → 勾选状态静默错位（表现为"选项显示不对"） | 已知并接受；文档中向 mod 作者强调"同一 mod 必须用同一份 ini" |
| 严格模式 | 用户拿到损坏/缺失 ini → 客户端不可用 | 已知并接受；错误信息需明确指向具体文件与节 |
| 广播集不在 ini | `BroadcaseToLobby` 语义无法由 mod 配置 | 记录为已知限制（§2.1）；如后续需要，再评估是否引入客户端自有的扩展键 |
| ExtraControls 类型无法映射 | mod 注册了未知控件类型 | **严格报错**（§5）：类型不在 `SettingCheckBox` / `FileSettingCheckBox` 内即视为配置错误 |
| 设置页重写面大 | `Settings.razor`（约 1000 行）改造引入回归 | 分批实施 + 逐批实跑（§9） |
| `ToolTip`（游戏选项侧）接入 | `GameSessionOption` 现持有 `ToolTip` 字段，由 `GameOptionsIniLoader` 从 `[chk*]`/`[cmb*]` 的 `ToolTip` 键读入 | **已消除**：遭遇战（`Skirmish.razor`）与在线房间（`GameLobby.razor`）均在复选框 `label` / 下拉 `div` 渲染 `title=ToolTip`；随包 `GameLobbyBase.ini` 已带 16 条 `ToolTip`（12 复选 + 4 下拉）。设置页侧 `Extras` 亦已渲染 `title` |
| 迁移后多出 Game Speed 下拉 | 遭遇战/房间界面与 spawn.ini 多出 `GameSpeed=<index>` | **与原版一致**（移植前遗漏），实测确认；房主/加入者用同一 ini 即口径一致 |
| 内置控件文案被 ini 覆盖为英文 | 随包 `OptionsWindow.ini` 的 `[chkPersistentMode]` / `[chkDiscordIntegration]` 带英文 `Text=`，使设置页这 2 个开关显示英文 | **已确认保留**（用户拍板）：这是"ini 文案优先"（§4.2 决策 3）的必然结果，与原版一致 |
| 新增对 `Resources/*.ini` 的运行时依赖 | 遭遇战/房间页现需 `SkirmishLobby.ini` / `MultiplayerGameLobby.ini`（+ 继承链 `GameLobbyBase.ini` / `GenericWindow.ini`）；设置页需 `OptionsWindow.ini`。缺失按严格模式抛错 | **非新增依赖类别**：客户端此前已硬依赖 `Resources/Renderers.ini`（`DirectDrawWrapperManager.cs:41` 抛 `ClientConfigurationException`）与 `Resources/ClientDefinitions.ini`（插件 / UI / Components / LoadingScreen）。这些 ini 属**客户端自身 Resources**，与 `bak\Resources` 同一集合；发布时随游戏目录的 `Resources` 提供。⚠️ 注意 `Resources/**` **不在** `BlazorCncnetClient.csproj` 的 `Content` 内（仅 `wwwroot/**`），故开发环境的 `bin\Debug\net10.0\Resources` 系手工放置而非构建产出（发布链路一直如此） |

---

## 8. 验证用例

| # | 操作 | 期望 |
|---|---|---|
| V1 | 在 `[GameOptionsPanel]` 删掉一条 `$CC-GOxx=chkXxx:GameLobbyCheckBox` 声明 | 遭遇战/房间界面少一个选项；spawn.ini 无对应键（✅ 已实测） |
| V2 | 在 `[GameOptionsPanel]` 加 `$CC-GOxx=chkMyOpt:GameLobbyCheckBox` **并**加 `[chkMyOpt]` 节（`Text`/`SpawnIniOption`/`Checked`） | 界面多一个选项，spawn.ini 出现对应键（✅ 已实测） |
| V3 | 修改 `[cmbCredits] Items=` / `DefaultIndex=` | 下拉项与默认值跟随 |
| V4 | 把 `cmbTechLevel` 改回 `Enabled=yes`+`Visible=yes` | 该下拉出现（格式化后不再需要改代码）（✅ 已实测） |
| V5 | 在 `[DisplayOptionsPanelExtraControls]` 加一项 + 控件节（`SettingCheckBox`） | 设置页 display 分区出现新开关（✅ 解析层已实测：随包 ini 的 `chkMEDDraw` 即由该节注册进 display） |
| V6 | 上述控件设 `SettingSection=x`/`SettingKey=y`，改值并保存 | 对应 ini 的 `[x] y=` 落盘（重启后保持）（✅ 已核实 `SetValue`→`SaveSettings`→`WriteIniFile` 全链路可用） |
| V7 | 勾选 `chkMEDDraw`（`FileSettingCheckBox`） | 目标文件按 `OverwriteOnMismatch` / `KeepChanges` 策略复制（✅ 文件层已实测 5 种情形） |
| V8 | 插件 CSS 里覆盖 `--lay-*` 令牌 | 对应页面布局改变（无需重编译）（✅ 机制已就绪：令牌定义于 `:root`，外部主题/插件后注入即生效） |
| V9 | 临时移走 `SkirmishLobby.ini` / `MultiplayerGameLobby.ini`（或 `GameOptionsPanel` 无 `$CC*`） | 按严格模式给出明确报错（含文件路径） |
| V10 | 编译 + 单测（现有 30 项）+ `publish.ps1` | 0 错误、测试全过、打包校验全绿（batch 1 已跑：✅ 0 错误 / 30 通过） |

---

## 9. 实施顺序（建议）

1. **游戏选项声明式化**（§4.1）——收益最大、面最小，先做先验。**✅ 已完成并实测（2026-10-19）**：按 `$CC-GO*` 类型化集合 + 按模式继承实现；两模式 12 chk 位序不变、下拉 3 个（含 Game Speed）；V1/V2/V4 实测通过；编译 0 错误、单测 30/30。
2. **设置页 ini 机制**（§4.2）——含 ExtraControls 注册 + 值绑定通用 API。**✅ 已完成并实测（2026-10-19）**：值绑定通用 API 直接复用既有 `SetValue`/`GetValue`（无需新增）。
3. **FileSettingCheckBox**（§4.3）——依赖 §4.2 的控件框架。**✅ 已完成并实测（2026-10-19）**（并入 §4.2 的 `Extras` 渲染，未单开 .razor 组件）。
4. **布局令牌**（§4.4）——独立，可并行。**✅ 已完成（2026-10-19）**：`:root` 20 个 `--lay-*`，默认值与硬编码逐字节一致；两断点改为只覆盖令牌；花括号校验通过、编译 0 错误。
5. **游戏选项 ToolTip 接入**（§7 原"未接入"项）——**✅ 已完成（2026-10-19）**：`GameSessionOption.ToolTip` 字段 + `GameOptionsIniLoader` 读入 `ToolTip` 键 + `Skirmish.razor`/`GameLobby.razor` 渲染 `title`；编译 0 错误、单测 30/30。
6. **统一 ToolTip 机制（收敛到原生 `title`）**——**✅ 已完成（2026-10-19）**：核实 `Components/Shared/Tooltip.razor` 为孤儿组件（全仓零引用；`docs/mod-customization.md` 未将其登记为插件/可替换扩展点，仅 `docs/tasks.md` 的 2.7.3 历史任务提及），连同 `wwwroot/css/app.css` 的 `.tip-wrap` / `.tip` 规则一并删除；全站 tooltip 统一由原生 `title` 承接（原版亦为原生 tooltip）。编译 0 错误、单测 30/30。
7. **控件身份钩子（`data-*`）**——**✅ 已完成（2026-10-19）**：游戏选项复选框与下拉容器加 `data-option="<Name>"`（`Skirmish.razor` / `GameLobby.razor` / `LanGame.razor`）；`Settings.razor` 内置控件行与 `Extras(...)` 渲染行加 `data-setting="<Section>:<Key>"`。实例化 `id` 仍仅归 JS / 锚点使用（全仓仅 ~8 处），样式基线仍为 class + `:root` 令牌（§4.4），`id` 不作样式选择器。
8. **LAN 房间补 ini 驱动游戏选项**——**✅ 已完成（2026-10-19）**：`GameOptionsIniLoader.LAN_INI = "LANGameLobby.ini"`（BasedOn=MultiplayerGameLobby.ini → GameLobbyBase.ini，共享 `[GameOptionsPanel]`，与原版 LAN lobby 同源）；`LanGameRoomService.GameOptions` 构造期声明式加载并严格校验；新增 `SetCheckBoxOption` / `SetDropDownOption`（房主）+ `GetGameOptionsCode` / `ApplyGameOptionsCode`；`OPTS` 广播扩展为 `seed/mapSHA1/mode/frameSendRate/removeStart/gameOptionsCode[;下拉索引…]`，`Client_HandleGameOptions` 对称解析（旧格式缺省时保留当前值）；`LanGame.razor` 新增游戏选项网格（房主可改、加入者只读，与在线房间 GO 语义一致）；`LanGameLaunchService` 将 `GameOptions` 传入 `WriteSpawnIni` / `WriteMap`。编译 0 错误、单测 30/30。
9. **UI 实跑验收**（V1–V7 的界面侧）+ **`publish.ps1`** ——待做（§8 V8 插件覆盖令牌已具备机制）。

每批完成后编译 + 实跑 + 汇报，最后统一执行 §8 全部用例。

---

## 10. 改动文件清单

**新增**

- `src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Options/GameOptionsIniLoader.cs`（§4.1，已实施）
- `src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Options/OptionsWindowIni.cs`（§4.2+§4.3，已实施；含 `OptionsWindowIni` / `OptionsWindowConfig` / `SettingControl` / `SettingControlAttributes` / `EnabledFileApplier`）
- `src/BlazorCncnetClient/BlazorCncnetClient/Components/Controls/FileSettingCheckBox.razor`（§4.3，**未采用**：FileSettingCheckBox 与 SettingCheckBox 渲染一致，差异仅在提交时的文件复制，故并入 `Settings.razor` 的 `Extras`）

**删除**

- `src/BlazorCncnetClient/BlazorCncnetClient/Components/Shared/Tooltip.razor`（批次 6：孤儿组件，零引用、非扩展点；tooltip 收敛到原生 `title`）

**修改**

- `src/BlazorCncnetClient/CnCNetClient.Domain/Multiplayer/GameSessionOption.cs`（仅改注释：说明清单已由 ini 驱动、`CreateDefaultGameOptions` 退居广播集模板；**子类签名未动**）
- `src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Options/GameOptionsIniLoader.cs`（批次 8：新增 `LAN_INI = "LANGameLobby.ini"`）
- `src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/SkirmishService.cs:93`（`GameOptionsIniLoader.Load(SKIRMISH_INI)`）
- `src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/GameRoomService.cs:141`（`GameOptionsIniLoader.Load(MULTIPLAYER_INI)`）
- `src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/LanGameRoomService.cs`（批次 8：`GameOptions` 加载 + `SetCheckBoxOption` / `SetDropDownOption` / `GetGameOptionsCode` / `ApplyGameOptionsCode` + `OPTS` 扩展与解析）
- `src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/LanGameLaunchService.cs`（批次 8：`GameOptions` 传入 `WriteSpawnIni` / `WriteMap`）
- `src/BlazorCncnetClient/BlazorCncnetClient/Components/Pages/Settings.razor`（`Extras` 渲染 + `CommitExtras` 提交 + 内置控件 `IniText` / `IniRowClass`；批次 7：`data-setting` 钩子）
- `src/BlazorCncnetClient/BlazorCncnetClient/Components/Pages/Skirmish.razor`、`GameLobby.razor`（批次 5：`title` tooltip；批次 7：`data-option` 钩子）
- `src/BlazorCncnetClient/BlazorCncnetClient/Components/Pages/LanGame.razor`（批次 8：游戏选项网格 + 房主改选项处理；批次 7：`data-option` 钩子）
- `src/BlazorCncnetClient/BlazorCncnetClient/wwwroot/css/app.css`（新增 `.set-row.ini-hidden`；布局令牌 §4.4；批次 6：移除孤儿 `.tip-wrap` / `.tip`）
- `src/BlazorCncnetClient/CnCNetClient.Core/Settings/UserINISettings.cs`（**未改动**：通用 `SetValue`/`GetValue` 已存在）

**文档同步**

- `docs/mod-customization.md`（面向 mod：可改哪些 ini 键、豁免哪些）→ **✅ 已补** 新增 **F1.5「控件身份钩子」** 节：登记 `data-option` / `data-setting`（含取值规则、稳定性承诺、与 design tokens 的关系）
- `docs/spec.md`、`docs/checklist.md`（登记本次改造）

---

## 11. 实施时需先核实的问题（不假设，实测）

1. `DataWriteMode` 的完整取值集合（实测仅见 `String` / `Index`；原版可能还有 `Boolean` / `MapCode`）。→ **已实现** `String` / `Index` / `Boolean` / `MapCode`，未知值严格报错。
2. `Enabled` / `Visible` 是否也适用于 `[chk*]` 复选框节（实测仅见用于 `[cmb*]` 与设置控件）。→ **已按统一规则支持**：loader 对 chk/cmb 一律读取，缺省 true。
3. 取消勾选 `FileSettingCheckBox` 时原版的确切行为（无 `DisabledFile`，需以原版实测或源码确认为准）。→ **已定**：不删除 / 不回滚文件，仅写回 `SettingKey=false`（沿"无 DisabledFile 即无反向操作"的实测事实）。
4. `OptionsWindow.ini` 中设置控件的 `Text` 是否可能直接写在控件节。→ **已定**：**是**，实测 `[chkMEDDraw] Text=Enable DDWrapper for map editor`、`[chkPersistentMode] Text=Stay connected outside of the lobby` 等均在控件节内。（原假设"chkMEDDraw 未见 Text"有误，实测其确有 `Text`。）
5. `[ExtraControls]` 根节为空时，是否还有其它注册路径。→ **已定**：无。注册只走 `[<面板>ExtraControls]`（`<面板>` ∈ §4.2 映射表）；根节 `[ExtraControls]` 实测为空且不参与解析。
