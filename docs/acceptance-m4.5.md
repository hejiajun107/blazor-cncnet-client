# M4.5 遭遇战验收清单

> 范围：checklist **D4.1 遭遇战（Skirmish）入口与配置**。
> 移植依据：原版 `DXMainClient/DXGUI/Multiplayer/GameLobby/SkirmishLobby.cs`（数据模型/持久化/校验/启动语义）+ 在线 `GameLaunchService`（窗口管理），去 XNA。
> 端口/配置：无隧道本地主机（`spawn.ini` 不写 `[Tunnel]` 节）；设置持久化 `Client/SkirmishSettings.ini`（`Player`/`AIPlayers`/`Settings` 节）；AI 玩家上限 7（人类固定 1 位，`MAX_PLAYER_COUNT=8`）。
> 涉及文件：`CnCNetClient.Domain/Multiplayer/SpawnIniGenerator.cs`（`aiPlayers` AI 扩展）、`Infrastructure/SkirmishService.cs`、`Infrastructure/SkirmishLaunchService.cs`、`Components/Pages/Skirmish.razor`（`/skirmish`）、`Components/Pages/SkirmishLoading.razor`（`/skirmish/loading`）、`MainMenu.razor`（遭遇战入口）、`Program.cs`（注册）、`wwwroot/css/app.css`。
> 状态：**实现完成，待运行验收**（主工程构建 0 错误、本次改动 0 新增 CS 警告）。
> 已对照原版补齐：G1 游戏退出后重生成随机种子（`RegenerateSeed`）；G2 coop 地图强制 `TeamId=1` + `AutoSurrender=false`；G6 每槽队伍配置 + 自动结盟（出生点→队伍映射）；G3 出生点随机（`StartingLocation=0` + `ForceRandomStartLocations`）；G7 游戏选项持久化（`[GameOptions]` 复选/下拉 + spawn.ini/地图代码应用）；G5 人类 Spectator 阵营（阵营下拉含观战 + coop 不可观战 + spawn.ini `IsSpectator`）。

---

## 1. 入口与页面（D4.1）

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 1.1 | 主菜单 → 遭遇战 | 进入 `/skirmish`；顶部显示本地游戏名 + 「单机 · 人类 1 + AI N」；右上返回主菜单 | ☐ |
| 1.2 | 返回确认 | 点击「返回主菜单」→ 弹确认框（丢失未开始配置）→ 确认回主菜单 / 取消留在页内 | ☐ |
| 1.3 | 初始化 | 首次进入自动建立人类玩家 + 1 个默认「简单」AI；持久化设置存在时恢复上次配置 | ☐ |

## 2. 地图与模式选择

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 2.1 | 地图下拉 | 按游戏模式分组列出全部地图（模式 + 地图名）；选择后地图预览缩略图更新 | ☐ |
| 2.2 | 地图信息 | 显示地图名/作者/尺寸/模式/人数范围；有 Briefing 时展示简介 | ☐ |
| 2.3 | 出生点图层 | 地图预览上叠加出生点坐标点；已占用点显示玩家颜色 + 名称，占用点不可选 | ☐ |
| 2.4 | 点击出生点 | 点击空闲出生点 → 人类玩家出生点切换为该点（地图允许范围内） | ☐ |
| 2.5 | 换图行为 | 换图后人类出生点重置为该图允许的首个位置 | ☐ |
| 2.6 | 未选图 | 未选图时「开始游戏」禁用；地图区域显示「请选择地图」占位 | ☐ |

## 3. 人类玩家配置

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 3.1 | 阵营 | 下拉切换阵营（Sides 列表）；槽位与玩家列表同步更新 | ☐ |
| 3.2 | 颜色 | 色板点击切换颜色（含名称 tooltip）；选中态高亮；槽位色块同步 | ☐ |
| 3.3 | 出生点 | 下拉 1–8 切换人类出生点；与 AI 出生点冲突由开始前校验拦截 | ☐ |

## 4. AI 玩家配置

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 4.1 | 添加 AI | 槽位「+ 添加 AI 玩家」/ AI 面板「＋」→ 新增 AI（默认简单难度、空闲阵营/颜色/出生点）；计数与玩家列表更新 | ☐ |
| 4.2 | 移除 AI | AI 卡片「✕」→ 移除该 AI，剩余 AI 序号/名称重新编号；全部移除后显示空提示 | ☐ |
| 4.3 | AI 上限 | 添加至 7 个 AI 后「＋」隐藏/禁用（人类 1 + AI 7 = 8 上限） | ☐ |
| 4.4 | 阵营/颜色/出生点 | 每个 AI 独立下拉配置；槽位同步显示 | ☐ |
| 4.5 | 难度 | 简单/中等/困难切换，槽位「AI 难度」标签同步 | ☐ |
| 4.6 | 重置 AI | 「重置 AI」→ 清空后重新添加 1 个默认 Easy AI（原版 InitDefaultSettings 语义） | ☐ |

## 5. 校验与开始

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 5.1 | 人数不足 | 地图 EnforceMinPlayers 且总人数不足 → 开始 toast 提示需要最少人数，不启动 | ☐ |
| 5.2 | 人数超限 | 总人数超地图 MaxPlayers → toast 提示最多人数，不启动 | ☐ |
| 5.3 | 出生点冲突 | EnforceMaxPlayers 时多人同出生点 → toast「多名玩家不能共享同一出生点」，不启动 | ☐ |
| 5.4 | 单机限制 | 选择 MultiplayerOnly 地图 → 校验提示只能在 CnCNet/局域网游玩 | ☐ |
| 5.5 | 开始成功 | 校验通过 → 跳转 `/skirmish/loading`；宿主窗口按设置最小化；游戏进程以 -SPAWN 启动 | ☐ |

## 6. spawn.ini 与游戏内容

| # | 检查项 | 通过 |
|---|--------|------|
| 6.1 | `spawn.ini` 含人类玩家 + 全部 AI（`[AIPlayers]`/`[HouseHandicaps]`/`[HouseCountries]`/`[HouseColors]`/出生点）；无 `[Tunnel]` 节 | ☐ |
| 6.2 | `spawnmap.ini`（`spawnmap.ini`/地图配置）正常生成 | ☐ |
| 6.3 | GameID 唯一（随机正数），GameLoading 页显示 `#GameID` | ☐ |
| 6.4 | 游戏内出现对应数量 AI 与配置（阵营/颜色/难度/出生点） | ☐ |
| 6.5 | coop 地图：人类与 AI 均 `TeamId=1` 结盟；`[Settings] AutoSurrender=false` | ☐ |
| 6.6 | 连续两局 `[Settings] Seed` 不同（游戏退出后重新生成种子） | ☐ |

## 7. 加载等待页与返回

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 7.1 | 加载页 | 显示模式/地图/玩家/GameID + 雷达式加载环 + 运行计时（每秒刷新） | ☐ |
| 7.2 | 游戏退出 | 游戏进程退出 → 宿主窗口恢复 → 自动回到 `/skirmish` 页 | ☐ |
| 7.3 | 启动失败 | 进程启动失败 → 恢复窗口 + 错误弹框 + 回 `/skirmish`，不重复导航 | ☐ |
| 7.4 | 手动返回 | 加载页「返回遭遇战」→ 确认框 → 回 `/skirmish` | ☐ |

## 8. 持久化

| # | 检查项 | 通过 |
|---|--------|------|
| 8.1 | 开始游戏时写 `Client/SkirmishSettings.ini`（人类 + AI + 地图/模式） | ☐ |
| 8.2 | 重启客户端后重新进入遭遇战 → 恢复上次地图/玩家/AI 配置 | ☐ |
| 8.3 | 设置文件损坏/缺失 → 回退默认配置（人类 + 1 Easy AI）不崩溃 | ☐ |

## 9. 回归

| # | 检查项 | 通过 |
|---|--------|------|
| 9.1 | 在线大厅/游戏/隧道（M3）、LAN（M4.4）、设置/更新（M4.1–M4.3）不回归 | ☐ |
| 9.2 | 主菜单按钮结构不变（遭遇战为新入口，不挤占原 9 按钮） | ☐ |
| 9.3 | 构建 0 错误；本次改动无新增 CS 警告 | ☐ |

## 10. 队伍配置与自动结盟（G6）

> 移植依据：原版 `PlayerExtraOptionsPanel`（`chkBoxUseTeamStartMappings`/`ddTeamStartMappingPreset`/`teamStartMappingsPanel`）+ `PlayerExtraOptions.GetTeamMappingsError` 校验；spawn.ini 联盟写入见 `AllianceHolder`（`MultiX_Alliances` 节）。
> 说明：自动结盟配置为**会话级**（不写入 `SkirmishSettings.ini`，同原版）。

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 10.1 | 人类/各 AI「队伍」下拉 | 每槽（人类 + 每个 AI）可独立选 `-`/A/B/C/D；槽位/卡片显示同步 | ☐ |
| 10.2 | 队伍禁用 | coop 或 `ForceNoTeams` 地图：队伍下拉禁用（保持无队伍）；换图后自动重置 `TeamId=0` | ☐ |
| 10.3 | 自动结盟开关 | 非 coop 且地图含 TeamStartMapping 预设时「启用自动结盟」可选；无预设地图开关禁用 | ☐ |
| 10.4 | 预设下拉 | 含「自定义」+ 地图预设；选择预设 → 映射列表更新并回到「自定义」（同原版 apply 语义） | ☐ |
| 10.5 | 出生点映射编辑 | 每个允许出生点可选 阻挡/无队伍/A-D；地图不允许的出生点下拉禁用 | ☐ |
| 10.6 | 互斥 | 启用自动结盟后：手动队伍下拉禁用（队伍由出生点映射决定） | ☐ |
| 10.7 | 映射校验 | 启用自动结盟时：出生点映射重复 / 仅一个队伍 → 开始游戏被拦截并提示 | ☐ |
| 10.8 | spawn.ini 联盟 | 手动设队伍 → `MultiX_Alliances` 按 TeamId 分组写入；启用自动结盟 → 按出生点映射推导队伍写入 | ☐ |
| 10.9 | 游戏内生效 | 游戏内对应 House 结盟关系与配置一致（手动或自动结盟） | ☐ |

## 11. 出生点随机（G3）

> 移植依据：原版 `PlayerHouseInfo.RandomizeStart`（`StartingLocation=0` 默认交游戏自身随机 `StartingWaypoint=-1`；有自动结盟映射 / 出生点非连续 / `UseClientRandomStartLocations` 时由客户端随机分配）；`GameLobbyBase` 出生点下拉在 `ForceRandomStartLocations` 时 `AllowDropDown=false`。
> 说明：普通地图可在出生点下拉手动选「随机」（值 0）；`ForceRandomStartLocations` 地图出生点下拉整体禁用、全员随机。

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 11.1 | 普通地图出生点「随机」 | 出生点下拉含「随机」选项；选「随机」后槽位显示「出生随机」 | ☐ |
| 11.2 | 随机出生 spawn.ini | 选「随机」的玩家 `spawn.ini` 不写具体 waypoint（`StartingWaypoint=-1`，交游戏随机） | ☐ |
| 11.3 | ForceRandomStartLocations 图 | 换图后人类与全部 AI 出生点重置为 0；出生点下拉禁用（显示「随机」）；地图预览点击选点无效 | ☐ |
| 11.4 | 强制随机 spawn.ini | 强制随机图所有玩家 `StartingWaypoint` 由客户端随机分配（互不重复）或为 -1（交游戏） | ☐ |
| 11.5 | 与自动结盟共存 | 启用自动结盟时：玩家随机出生由客户端按空闲出生点分配（映射阻挡点不参与分配） | ☐ |
| 11.6 | 校验回归 | 玩家显式选同一具体出生点仍被校验拦截；选「随机」不受同点冲突校验限制 | ☐ |

## 12. 游戏选项（G7）

> 移植依据：原版 `SkirmishLobby` 继承 `GameLobbyBase` 的 `GameLobbyDropDown`/`GameLobbyCheckBox` 选项组（`GameLobbyBase.ini`）+ `SaveSkirmishGameOptions` 配置（`[GameOptions]` 节读写，GameMode/Map `ForcedDropDownValues`/`ForcedCheckBoxValues` 选项跳过）。
> 说明：移植 12 个复选框 + 2 个下拉（`cmbCredits` Starting Credits / `cmbStartingUnits` Starting Units；`cmbTechLevel` 原版 `Enabled=no`、`cmbGameSpeedCap` 无 Items，故不移植）。复选写 spawn.ini `[Settings]` 或应用地图代码 `CustomIniPath`；`StolenTech`/`NavalCombat`/`DestroyableBridges` 为 Reversed 语义。

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 12.1 | 选项区展示 | 遭遇战页「游戏选项」区：12 复选框 + 2 下拉，显示名/顺序与原版一致 | ☐ |
| 12.2 | 默认值 | 默认勾选与下拉索引同原版（Short Game/MCV Repacks/Build Off Allies/Ingame Allying/Stolen Tech/Naval Combat/Destroyable Bridges/No Spawn Previews 勾选；Credits=15000、Starting Units=0） | ☐ |
| 12.3 | 复选框写 spawn.ini | 勾选状态变化 → `spawn.ini [Settings]` 对应键值更新（ShortGame/MCVRedeploy/Superweapons/Crates/BuildOffAlly/MultiEngineer/AlliesAllowed） | ☐ |
| 12.4 | 下拉写 spawn.ini | Credits/Starting Units 选择 → `spawn.ini [Settings]` `Credits`/`UnitCount` 写入选中项值 | ☐ |
| 12.5 | 地图代码应用 | 勾选 Ingame Allying → 应用 `AlliesAllowed.ini`；勾选 Brutal AI → 应用 `BrutalAI.ini`；Stolen Tech/Naval Combat/Destroyable Bridges 取消勾选（Reversed）才应用对应 ini | ☐ |
| 12.6 | [GameOptions] 持久化 | `SaveSkirmishGameOptions` 开启：修改选项 → 退出 → 重进，选项状态从 `SkirmishSettings.ini [GameOptions]` 恢复 | ☐ |
| 12.7 | Forced 选项跳过 | 被 GameMode/Map 强制的选项：加载时忽略已保存值（`IsGameOptionForced`），spawn.ini 以强制值覆盖 | ☐ |

## 13. 人类观战阵营（G5）

> 移植依据：原版 `SkirmishLobby`（仅 `ddPlayerSides[0]` 加 `AddSideToDropDown(spectatorName)`）+ `GameLobbyBase.CheckGameValidity`（coop+spectator 拦截；`Players.Count(p => p.SideId < dd.Items.Count - 1)` 观战不计玩家数）+ `GetSpectatorSideIndex() = SideCount + RandomSelectorCount`（本端无 Random/随机选择器 → `SpectatorSideIndex = SideCount`）+ `WriteSpawnIni`（`[Settings] IsSpectator` / `[IsSpectator] MultiN`）+ `PlayerHouseInfo`（观战 `InternalSideIndex = SpectatorInternalSideIndex`、`RandomizeStart` waypoint 90）。
> 说明：仅人类阵营下拉含 Spectator（同原版 SkirmishLobby）；AI 无观战选项。

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 13.1 | 阵营下拉含 Spectator | 人类阵营下拉末项为 Spectator；选择后槽位显示「观战」；AI 阵营下拉无 Spectator | ☐ |
| 13.2 | 玩家数扣除 | 观战时玩家计数与 min/max 校验按非观战玩家计算（仅 AI 计入） | ☐ |
| 13.3 | coop 不可观战 | 合作地图选 Spectator → 开始游戏被拦截并提示「合作任务不能观战」 | ☐ |
| 13.4 | spawn.ini IsSpectator | 观战时本地 `[Settings] IsSpectator=true`、`Side=SpectatorInternalSideIndex`、`[IsSpectator]` 节 Multi1=true、出生点固定 waypoint 90 | ☐ |
| 13.5 | 持久化恢复 | 选 Spectator 后保存设置/重进，阵营保持 Spectator（加载越界判断允许观战索引） | ☐ |

---

## 已知限制

- 遭遇战 AI 行为完全由游戏本体 AI 决定，客户端仅下发配置（阵营/颜色/难度/出生点）。
- `SkirmishSettings.ini` 的出生点冲突校验仅在地图 `EnforceMaxPlayers` 时执行（同原版 SkirmishLobby）。
- GameID 采用随机正数（原版按战绩统计生成；本端无战绩库，随机保证本局唯一）。
