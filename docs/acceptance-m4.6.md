# M4.6 战役/任务选择验收清单

> 范围：checklist **D4.1 单机/战役（CampaignSelector）入口与配置**。
> 移植依据：原版 `DXMainClient/DXGUI/Campaign/CampaignSelector.cs`（任务列表解析 `ParseBattleIni`/`LoadCustomMissions`、启动 `LaunchMission`、作弊检查 `BtnLaunch_LeftClick`/`filesToCheck`）+ 在线 `GameLaunchService`（窗口管理），去 XNA。
> 端口/配置：任务数据源 `INI/Battle.ini`（空则回退 `BattleFS.ini`，见 `ClientConfiguration.BattleFSFileName`）；难度持久化 `UserINISettings.Difficulty`（0=Easy/1=Medium/2=Hard，spawn.ini `DifficultyModeHuman` 直写、`DifficultyModeComputer` 取反 `|难度-2|`）；自定义任务目录 `ClientConfiguration.CustomMissionPath`（默认 `Maps/CustomMissions`，`.map` 含 `[ClientMissionConfig]`）；`CopyMissionsToSpawnmapINI` 开启时难度地图代码（`INI/Map Code/Difficulty X.ini`）合并写 `spawnmap.ini`。
> 涉及文件：`CnCNetClient.Domain/Multiplayer/SpawnIniGenerator.cs`（`WriteMissionSpawnIni`/`WriteMissionSectionToSpawnIni`/`InitializeMissionSupplementFiles`/`DeleteMissionSupplementalFiles`）、`Infrastructure/CampaignService.cs`、`Infrastructure/CampaignLaunchService.cs`、`Components/Pages/Campaign.razor`（`/campaign`）、`Components/Pages/CampaignLoading.razor`（`/campaign/loading`）、`MainMenu.razor`（战役入口）、`Program.cs`（注册）、`wwwroot/css/app.css`。
> 状态：**实现完成，待运行验收**（主工程构建 0 错误、本次改动 0 新增 CS 警告）。

---

## 1. 入口与页面（D4.1）

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 1.1 | 主菜单 → 战役 | 进入 `/campaign`；顶部显示「战役 CAMPAIGN」徽标 + 「共 N 项 · 难度 X」；右上返回主菜单 | ☐ |
| 1.2 | 返回确认 | 点击「返回主菜单」/「取消」→ 弹确认框（丢失未开始配置）→ 确认回主菜单 / 取消留在页内 | ☐ |
| 1.3 | 初始化 | 首次进入自动加载任务列表并恢复上次难度（`UserINISettings.Difficulty`，默认 Medium） | ☐ |

## 2. 任务列表

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 2.1 | 数据源 | 任务列表按 `INI/Battle.ini [Battles]` 键顺序生成；Battle.ini 缺失/空时回退 `BattleFS.ini` | ☐ |
| 2.2 | 标题行 | 无 Scenario 的任务（如 `TSCMPGNS`/`FSCMPGNS`/`TUTORIALS`）显示为分组标题（Orbitron 小字 + 分隔线），不可选择 | ☐ |
| 2.3 | 空行 | `[EMPTY]` 条目显示为空行（占位分隔），不可选择 | ☐ |
| 2.4 | 任务行 | 官方任务显示名称 + 阵营色点 + FS 标签（`RequiredAddon` 时）；点击选中（描边发光）；再点取消无 | ☐ |
| 2.5 | 禁用项 | 重复 CustomMissionID 的任务禁用（灰显不可选）；无 Scenario 的任务不可启动 | ☐ |
| 2.6 | 自定义任务 | `Maps/CustomMissions/*.map`（含 `[ClientMissionConfig]`）加载进全量集合；选择器默认不展示自定义任务（同原版 filter，后续 CampaignTagSelector 接入） | ☐ |
| 2.7 | 空列表 | 无 Battle.ini / BattleFS.ini / 自定义任务时显示「未找到任何任务」占位，启动禁用 | ☐ |

## 3. 任务描述

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 3.1 | 未选择 | 描述区显示「从左侧选择任务以查看描述」 | ☐ |
| 3.2 | 选择任务 | 显示任务名 + 阵营标签（优先 `SideName` 如 GDI/Nod）+ Firestorm 扩展包/自定义任务标签 + `LongDescription` 说明文字 | ☐ |
| 3.3 | 切换任务 | 切换选中项 → 描述/阵营/标签同步更新 | ☐ |

## 4. 难度与启动

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 4.1 | 难度切换 | EASY/MEDIUM/HARD 三段按钮；选中高亮；下方「玩家难度」与「电脑难度」提示（电脑 = 取反，Hard→Easy）同步 | ☐ |
| 4.2 | 启动可用性 | 未选任务 / 任务无 Scenario / 任务禁用 →「开始任务」禁用 | ☐ |
| 4.3 | 校验 | 未选任务点击（理论上按钮禁用）不触发；已选正常任务 → 直接进入启动流程 | ☐ |
| 4.4 | 作弊检查 | 非 ModMode 且任务地图或核心 INI（`filesToCheck`）被改动（`Updater` 判定非原始）→ 弹「检测到文件改动」确认框；确认后放行启动；ModMode 客户端跳过检查 | ☐ |

## 5. spawn.ini 与游戏内容

| # | 检查项 | 通过 |
|---|--------|------|
| 5.1 | `spawn.ini` 生成：`[Settings]` 含 Scenario（CopyMissionsToSpawnmapINI 时 `spawnmap.ini`）/CampaignID/GameSpeed（Fastest 强制降 Faster）/Ra2Mode·Firestorm（按 ClientGameType）/CustomLoadScreen/IsSinglePlayer=Yes/SidebarHack/Side/BuildOffAlly/DifficultyModeHuman/DifficultyModeComputer（取反）/CustomMissionID（自定义任务） | ☐ |
| 5.2 | 任务配置节：`[GameMissionConfig]` 复制进 spawn.ini（节名为 Scenario 路径）+ `[Settings] ReadMissionSection=Yes`；加载画面键缺失时回退自定义任务补充文件（.shp/.pal） | ☐ |
| 5.3 | 强制选项：`GameOptions.ini [CampaignForcedSpawnIniOptions]` 全部写入 `[Settings]` | ☐ |
| 5.4 | 难度地图代码：CopyMissionsToSpawnmapINI 开启 → `Difficulty Easy/Medium/Hard.ini` 与任务地图合并写 `spawnmap.ini` | ☐ |
| 5.5 | 难度持久化：启动后 `UserINISettings.Difficulty` = 所选难度并保存 | ☐ |
| 5.6 | 自定义任务补充文件：启动前复制 `.shp/.pal` 到游戏目录；游戏退出后清理（`DeleteMissionSupplementalFiles`） | ☐ |
| 5.7 | 游戏内生效：进入对应任务地图，难度/阵营/加载画面正确 | ☐ |

## 6. 加载等待页与返回

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 6.1 | 加载页 | 显示任务名/难度/阵营 + 雷达式加载环 + 运行计时（每秒刷新） | ☐ |
| 6.2 | 游戏退出 | 游戏进程退出 → 宿主窗口恢复 → 自动回到 `/campaign` 页 | ☐ |
| 6.3 | 启动失败 | 进程启动失败 → 恢复窗口 + 错误弹框 + 回 `/campaign`，不重复导航 | ☐ |
| 6.4 | 手动返回 | 加载页「返回战役选择」→ 确认框 → 回 `/campaign` | ☐ |

## 7. 回归

| # | 检查项 | 通过 |
|---|--------|------|
| 7.1 | 在线大厅/游戏/隧道（M3）、LAN（M4.4）、遭遇战（M4.5）、设置/更新（M4.1–M4.3）不回归 | ☐ |
| 7.2 | 主菜单按钮结构不变（战役为既有首项，本次接线导航） | ☐ |
| 7.3 | 构建 0 错误；本次改动无新增 CS 警告 | ☐ |

---

## 已知限制

- 战役游戏选项（`CampaignCheckBox`/`CampaignDropDown`）：原版由 MOD 通过 `GameClass.cs` 注册瞬态控件提供，默认 TS 客户端无实际实例；本移植不注册任何战役选项，`GameOptions.ini [CampaignForcedSpawnIniOptions]` 强制选项仍完整写入。
- 任务预览图（`Mission Previews/*.png`）：原版 XNA 纹理加载，本移植不渲染（任务描述文字完整展示）。
- 自定义任务选择器：原版通过 `CampaignTagSelector`（`CampaignTagSelectorEnabled` 默认关）展示，本移植选择器仅展示官方任务；自定义任务已加载进 `AllMissions`，待 CampaignTagSelector 移植时接入。
- 作弊检查依赖 `Updater` 镜像表（`IsFileNonexistantOrOriginal`）；Updater 未初始化时视为未修改（同原版语义）。
