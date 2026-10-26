# M4.3 自动更新验收清单

> 范围：spec **§5.3 更新** / checklist **D6**（D6.1 启动更新检查、手动更新查询、更新下载与解压；D6.2 新 exe 名 + 单一二进制目录 + 更新清单端到端演练）。
> 入口决策（已与用户确认）：**更新界面放在设置中心「更新」Tab**，与原版 `OptionsWindow → Updater 面板（Force update 按钮）`一致；主菜单不动（保持 9 按钮），仅底部版本/状态文本可选展示。
> 涉及文件：`Infrastructure/UpdateService.cs`（新建，scoped，包装静态 `Updater`；注入具体 `AppExitNotifier` 触发持久化）、`Program.cs`（注册服务 + Global 互斥锁 GUID `1CC9F8E7-9F69-4BBC-B045-E734204027A9`）、`Components/Pages/Settings.razor`（`RenderUpdate` 扩成完整更新面板）、`MainMenu.razor`（启动检查接线）、`wwwroot/css/app.css`（`.upd-progress`/`.upd-actions`）。
> 状态：**实现完成，待验收**（构建 0 错误）。验收项见 §1-§4，需在可访问更新镜像/演练服务环境下勾选；§5 回归项随其它里程碑联验。

---

## 0. 前置准备（必读）

- 环境检测需指向已安装的 RA2/YR 游戏本体（`Resources/` 存在且含 `ClientDefinitions.ini`）；更新镜像表读 `Resources/UpdaterConfig.ini`（`[DownloadMirrors]`）。
- 本地版本信息读游戏根目录 `version` 文件（`[DTA] Version / UpdaterVersion / FileVersions`）。
- 端到端演练（D6.2）需要：本地测试更新镜像（HTTP，放置 `version` 清单 + `.lzma` 归档）或公网 CnCNet 镜像；`Resources/BinariesNET8/Updater/SecondStageUpdater.dll` 存在。
- 验收辅助：观察 `GamePath/Updater/` 目录（下载解压落地）、`GamePath/version`（更新后版本号）、SecondStageUpdater.log（`Client/SecondStageUpdater.log`）。

## 1. 设置中心「更新」Tab（D6.1 手动查询 / 下载解压）

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 1.1 | 设置中心 → 更新分区 | 保留「启动时检查更新」开关（即改即存，spec §6.10）；下方展示本地版本（`Updater.GameVersion`）与更新状态文本 | ☐ |
| 1.2 | 点击「立即检查」 | 调用 `Updater.CheckForUpdates()`；检查中状态显示「检查中…」，完成后按结果更新（无可用更新/发现新版本/检查失败） | ☐ |
| 1.3 | 检查发现新版本（非手动） | 显示「发现新版本 v{ServerGameVersion} · {UpdateSizeInKb} KB」+「立即更新」按钮 | ☐ |
| 1.4 | 检查发现需手动更新 | 显示「需要手动下载安装」+ 打开 `ManualDownloadURL` 的「手动下载」按钮 | ☐ |
| 1.5 | 检查失败/无镜像 | 状态显示失败/「无可用更新镜像」，不崩溃，可重试 | ☐ |
| 1.6 | 点击「立即更新」 | 调用 `Updater.StartUpdate()`；显示进度条（当前文件 % 与总进度 % + 当前文件名 + 状态文本：下载中/解压归档/准备中）；「取消更新」可中止 | ☐ |
| 1.7 | 更新完成 | 「更新完成」提示；若未触发重启（单文件直更）则版本号刷新；若触发 `Updater.Restart` → 客户端持久化后退出，由 SecondStageUpdater 替换文件后重启 | ☐ |
| 1.8 | 更新失败 | 状态显示失败原因，可重试；不崩溃 | ☐ |

## 2. 启动更新检查（D6.1）

| # | 检查项 | 预期结果 | 通过 |
|---|--------|---------|------|
| 2.1 | 启动自动检查 | 主菜单加载且环境就绪后，若「启动时检查更新」开启（默认 true）→ 非阻塞执行 `Updater.CheckForUpdates()`，不阻塞进入任何页面 | ☐ |
| 2.2 | 开关关闭 | `[Options] CheckforUpdates=false` 时不自动检查 | ☐ |
| 2.3 | 检查结果可见 | 启动检查结果反映到设置「更新」Tab 状态（发现新版本时可选 Toast 提示） | ☐ |

## 3. 核心接线（实现正确性）

| # | 检查项 | 预期结果 | 通过 |
|---|--------|---------|------|
| 3.1 | 懒初始化 | `UpdateService` 首次使用（设置页/启动检查）时若环境就绪才 `Updater.Initialize(GamePath, GamePath/Resources, SettingsIniName, LocalGame, 当前 exe 名)`；未就绪时安全降级（UI 显示不可用，不崩溃） | ☐ |
| 3.2 | Restart 接线 | `Updater.Restart` 触发时：先 `IAppExitNotifier.NotifyExiting()` 持久化，再退出进程，让 SecondStageUpdater 完成替换 | ☐ |
| 3.3 | Global 互斥锁 | 宿主进程持有 Global mutex `Global{1CC9F8E7-9F69-4BBC-B045-E734204027A9}`（进程存活期间不释放），SecondStageUpdater 可据此等待主程序退出 | ☐ |
| 3.4 | 事件线程安全 | Updater 后台线程事件经 `IDispatcher`/`InvokeAsync` 回 UI 线程刷新，无跨线程异常 | ☐ |

## 4. 端到端演练（D6.2：新 exe 名 + 单目录）

> 新 exe 名：`BlazorCncnetClient.exe`（`OutputType=Exe` + `UseAppHost`），单目录发布（宿主目录即游戏根目录下的客户端目录）。

| # | 步骤 | 预期结果 | 通过 |
|---|------|---------|------|
| 4.1 | 本地镜像准备 | 在可访问的 HTTP 服务放置 `version`（含 `[DTA] Version` 高于本地、`[FileVersions]` 条目 + 对应归档）与 `.lzma` 文件；`Resources/UpdaterConfig.ini` 指向该镜像 | ☐ |
| 4.2 | 检查 → 更新 | 设置「更新」Tab：立即检查 → 发现新版本 → 立即更新 → 文件下载到 `GamePath/Updater/` → LZMA 解压 | ☐ |
| 4.3 | SecondStageUpdater | `Resources/BinariesNET8/Updater/SecondStageUpdater.dll` 存在时以 `dotnet ... SecondStageUpdater.dll {exe名} "{GamePath}"` 启动；客户端退出后其复制 `Updater/` 文件到根目录并重启客户端 | ☐ |
| 4.4 | 新 exe 名透传 | SecondStageUpdater 使用 `CallingExecutableFileName`（`BlazorCncnetClient.exe`）等待对应进程退出、跳过自身/依赖文件、读取 `ClientDefinitions.ini [Settings] LauncherExe` 重启 | ☐ |
| 4.5 | 版本回放 | 更新后 `version` 文件更新为新版本；重启客户端后「更新」Tab 显示已是最新 | ☐ |

## 5. 回归

| # | 检查项 | 通过 |
|---|--------|------|
| 5.1 | 语言切换（M4.2）不回归：设置页更新分区文案随语言切换 | ☐ |
| 5.2 | 设置其余分区即改即存不回归（对照 acceptance-m4.1） | ☐ |
| 5.3 | 主菜单/登录/大厅/游戏流程不回归 | ☐ |
| 5.4 | 构建 0 错误；本次改动无新增警告 | ☐ |

---

## 已知限制

- 本机无更新镜像/公网不可达时，检查显示失败属预期（D6.1 验收需可访问镜像或本地演练服务）。
- SecondStageUpdater 依赖游戏目录资源（`Resources/BinariesNET8/Updater/`、`ClientDefinitions.ini` 的 `LauncherExe`）；演练前需确认这些文件存在。
- 更新会向 `GamePath/Updater/` 写入下载文件，属正常流程；取消更新时残留文件由客户端下次更新清理（原版同行为）。
