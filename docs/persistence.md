# 持久化调研与 Spec 补充方案（Persistence）

> 状态：调研完成，待评审
> 目的：核对 spec 对「持久化」的覆盖度 → 调研原版 CnCNet 客户端把设置写到哪些文件（含 ra2md.ini）→ 提出 spec 补充方案。
> 结论先行：spec 仅在 5 处零散提及且有一处文件名写错；新版 Blazor 宿主**尚未接线任何用户设置持久化**（无 `UserINISettings.Initialize` 调用）。建议在 spec 新增 §6.10「持久化体系」并在 §5.3/§5.4.6 修正，配套 tasks/checklist 增项。

---

## 1. Spec 现状核对

| 位置 | 原文 | 问题 |
|---|---|---|
| §5.3 `SettingsWindow` 数据源 | `SettingsService`（`UserINISettings`/`DTACnCNetClient.ini`） | **文件名写死 `DTACnCNetClient.ini` 不准确**：实际由 `ClientDefinitions.ini` 的 `[Settings] SettingsFile` 决定，RA2/YR 官方为 `RA2MD.ini`（DTA 才是 SUN.ini） |
| §4.3 `OptionsWindow` 落点 | `SettingsService`（复用 Settings 体系） | 方向正确，无细节 |
| §5.4.6 `IThemeProvider` | 持久化（ini 或 localStorage） | 模糊，未定论；当前实现已走 localStorage（与游戏设置分离） |
| §2 技术选型 | 无持久化条目 | 缺「设置/状态持久化」选型行 |
| §6 关键机制 | 无持久化小节（6.1–6.9 均无） | **缺专门章节** |

结论：spec 提到「持久化」但无体系化设计（文件清单、保存时机、退出 hook、编码、共享文件风险、初始化顺序）。

---

## 2. 原版持久化机制调研（代码事实，含 ra2md.ini）

### 2.1 用户设置 → 游戏根目录 `SettingsFile`

- 文件名：`ClientConfiguration.SettingsIniName`（[ClientConfiguration.cs:311](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/CnCNetClient.Core/ClientConfiguration.cs#L311)）= `ClientDefinitions.ini [Settings] SettingsFile`（默认 `Settings.ini`）。
  - **RA2/YR 官方配置为 `SettingsFile=RA2MD.ini`**（社区/官方资料一致确认）——即把玩家设置直接写进**游戏自己的配置文件** ra2md.ini，游戏本身也读它（分辨率等）。
  - 本仓库复制的是 DTA 客户端（其 ClientDefinitions.ini 为 `SettingsFile=SUN.ini`），但 SettingsFile 由部署的 ClientDefinitions.ini 决定，代码层面与游戏类型解耦。
- 加载：原版 `DXMainClient/PreStartup.cs:129` → `UserINISettings.Initialize(SettingsIniName)`；初始化时合并 `Resources/UserDefaults.ini`（只读默认值，文件缺失则纯默认）。
- 保存：`UserINISettings.SaveSettings()`（[UserINISettings.cs:472](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/CnCNetClient.Core/Settings/UserINISettings.cs#L472)）→ `SettingsIni.WriteIniFile()` → 触发 `SettingsSaved` 事件。
- 保存时机：**变更即存**，非仅退出。原版调用点：
  - `OptionsWindow.SaveSettings()`（保存按钮）
  - `CnCNetLoginWindow`（登录后存昵称/自动登录）
  - `MainMenu`（first-run、翻译游戏文件版本）
  - `CnCNetLobby` / `GameLobbyBase` / `SkirmishLobby` / `LANLobby` / `CampaignSelector`（房间配置、过滤器、遭遇战设置）
  - `GameFiltersPanel`、`PrivacyNotification`
  - `UserINISettings.ToggleFavoriteMap` / `WriteFavoriteMaps`（收藏地图，方法内部直接 Save）
- 内容分区：`Video` / `Audio` / `Options` / `MultiPlayer` / `Compatibility` / `GameFilters` / `GameOptionFilters` / `FavoriteMaps` / `Channels`。
  - 字段示例：昵称 `[MultiPlayer] Handle`、主题 `[MultiPlayer] Theme`、分辨率（RA 走 `[Options] Width/Height`，TS/YR 走 `[Video] ScreenWidth/ScreenHeight`）、音量、窗口模式、自动登录、过滤器等。

### 2.2 客户端还会写这些文件

| 文件 | 位置 | 内容 | 读写方 | 时机 |
|---|---|---|---|---|
| `FinalSun.ini` | 游戏根 `FinalSun/` | 地图编辑器设置（语言/路径） | `FinalSunSettings.WriteFinalSunIniAsync`（[FinalSunSettings.cs](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/CnCNetClient.Domain/FinalSunSettings.cs)） | 启动时，**强制 ANSI 编码** |
| `spawn.ini` / `spawnmap.ini` / `Saved Games/spawnSG.ini` | 游戏根 / Saved Games | 开房/启动游戏 spawn 配置 | `GameLobbyBase`/`CampaignSelector`/`GameLoadingWindow` 等 | 启动游戏时 |
| `Client/Translation.ini` | Client | 翻译 stub（生成） | `Translation` | 按需 |
| `Client/ProcessedIniInfo.ini` | Client | INI 预处理器缓存 | `IniPreprocessInfoStore` | 预处理后 |
| `Client/friend_list` / `ignore_list` / `recent_list` | Client | 好友/忽略/最近玩家 | `CnCNetUserData`（[CnCNetUserData.cs:18-20](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/CnCNetClient.Online/CnCNetUserData.cs#L18-L20)） | **退出时**经 `ApplicationExiting` |
| 注册表（HKCU） | — | 游戏安装路径 | `WritePathToRegistry`（`InstallationPathRegKey`） | 按设置 |
| `Keyboard.ini` | 游戏根 | 当 `SettingsIniAsKeyboardIni` 时设置文件即键盘文件 | `ClientConfiguration` | — |

### 2.3 退出持久化 hook（IAppExitNotifier）

- `IAppExitNotifier`（Integration/Lifecycle）只暴露 `ApplicationExiting` 事件；`AppExitNotifier.NotifyExiting()` 由 [Program.cs:96-100](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Program.cs#L96-L100) 在 `app.Run()` 返回后 finally 调用。
- 已订阅方：`CnCNetUserData` → `Save()`（好友/忽略/最近，JSON/文本）。
- **未接线**：`UserINISettings` 未订阅退出保存（原版靠各 UI 变更即存，退出时一般无需额外落盘）。

### 2.4 当前 Blazor 宿主差距（重点）

- 全仓 grep：UI 工程**无 `UserINISettings.Initialize` 调用**，仅 `EnvironmentCheck` 触碰 `ClientConfiguration.Instance`（[EnvironmentCheck.cs:68](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/EnvironmentCheck.cs#L68)）。即 **用户设置持久化在新版中尚未启用**，SettingsService 尚不存在。
- 主题持久化分轨：`ThemeProvider` → localStorage `cncnet.theme`（WebView 数据目录，[ThemeProvider.cs:12](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Theme/ThemeProvider.cs#L12)）；原版主题是 settings INI 的 `[MultiPlayer] Theme`。二者需定边界。

---

## 3. Spec 补充方案（草案）

### 3.1 新增 §6.10「持久化体系」（建议插入 §6.9 之后）

```markdown
### 6.10 持久化体系

**原则**
- 复用原版 Settings/IniFile 体系（`CnCNetClient.Tools.IniFile`），不引入新存储格式。
- 双轨边界：**客户端/游戏设置**写 settings INI（`ClientDefinitions.ini [Settings] SettingsFile`，RA2/YR 为 `RA2MD.ini`，游戏根目录）；**纯 UI 外观偏好**（主题切换）写 WebView localStorage。
- 保存时机：**变更即存**（各 UI 动作后调用 `UserINISettings.SaveSettings()`）+ **退出兜底**（订阅 `IAppExitNotifier.ApplicationExiting`）。

**初始化顺序**（启动链路）
1. `EnvironmentCheck` 定位 `GamePath`（Resources 父目录）
2. `ClientConfiguration.Instance` 加载 `Resources/ClientDefinitions.ini`（静态配置）
3. `UserINISettings.Initialize(SettingsIniName)` → 合并 `Resources/UserDefaults.ini`
4. 注册 `SettingsService`（scoped）供设置页读写

**文件清单**（新增表格，见 §2.2 调研内容；关键标注）
- `ra2md.ini`（settings INI，RA2/YR）：玩家设置，**与游戏共享文件** → 只写客户端字段，不覆盖游戏字段；写前解析合并、防损坏。
- `FinalSun.ini`：ANSI 编码，首次写入。
- `spawn.ini`/`spawnmap.ini`/`spawnSG.ini`：开房 spawn 配置。
- `Client/friend_list` 等：好友/忽略/最近，退出时保存。
- `Client/Translation.ini`、`Client/ProcessedIniInfo.ini`：翻译 stub / 预处理缓存。

**编码约定**：默认 INI 编码沿用 `IniFile`；`FinalSun.ini` 强制 legacy ANSI（`EncodingExt.ANSI`）。

**主题持久化决策（待定，建议二选一）**
- 方案 A（推荐）：主题写 settings INI（原版 `[MultiPlayer] Theme` 语义），localStorage 仅作启动前的即时回显缓存 —— 跨客户端一致、与 MOD 主题目录联动。
- 方案 B：维持现状 localStorage，仅 UI 外观 —— 简单但与游戏设置分离。
```

### 3.2 修订既有条目

- §5.3 `SettingsWindow` 数据源：`SettingsService`（`UserINISettings` → `ClientDefinitions.ini [Settings] SettingsFile`，RA2/YR 即 `RA2MD.ini`；合并 `UserDefaults.ini`）。
- §5.4.6 `IThemeProvider`：明确持久化走「settings INI + localStorage 回显」或「仅 localStorage」（随 §6.10 决策）。
- §4.3 `OptionsWindow` → `SettingsService`：标注复用 `UserINISettings.SaveSettings()` 变更即存。
- §2 技术选型：补一行「设置/状态持久化：Rampastring.Tools IniFile（settings INI）+ localStorage（UI 外观）+ JSON/文本（好友列表等）」。

### 3.3 tasks / checklist 落点

- tasks.md：M2 增 `2.10 持久化接线：UserINISettings.Initialize + SettingsService + 退出保存（ApplicationExiting）`（依赖 2.8 的 AppExitNotifier 已就绪）；或并入 M4 `4.1 设置中心` 前置子项。
- checklist.md：G 区新增 `G5 设置持久化：settings INI 读写（RA2/YR=ra2md.ini）、UserDefaults 合并、变更即存 + 退出兜底、FinalSun.ini ANSI、spawn ini 生成`。

---

## 4. 评审结论（已确认 2026-08-28）

1. **目标游戏**：按 **RA2/YR 具体**口径写 —— 设置文件即 `RA2MD.ini`，并注明由 `ClientDefinitions.ini [Settings] SettingsFile` 决定、可被 MOD 覆盖。
2. **主题持久化**：选 **方案 A（写 settings INI）** —— 主题落 `[MultiPlayer] Theme`（原版语义），localStorage 仅作启动前即时回显缓存。
3. **接线时机**：`UserINISettings.Initialize` + `SettingsService` + 退出保存 **并入 M4 设置中心**（当前里程碑只补 spec，不改代码）。
4. spec 按上述口径修订为 **v0.5**；tasks.md / checklist.md 同步增项。

> 以下为修订前待确认问题（已被上述结论覆盖，保留备查）：
> 1. 目标游戏：以 RA2/YR 为准（SettingsFile=RA2MD.ini）？还是保持「由部署的 ClientDefinitions.ini 决定」的通用表述？ → **已定：RA2/YR 具体 + MOD 可覆盖**。
> 2. 主题持久化：选方案 A（写 settings INI，贴近原版）还是 B（维持 localStorage）？ → **已定：方案 A**。
> 3. UserINISettings 接线时机：当前里程碑（M2 尾）还是并入 M4 设置中心？ → **已定：并入 M4**。
> 4. spec 补充后是否直接改 docs/spec.md（v0.5）？ → **已定：改 v0.5**。
