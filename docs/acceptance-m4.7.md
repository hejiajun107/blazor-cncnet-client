# M4.7 附加窗口验收清单

> 范围：checklist **D4.1 附加窗口（Extras）**。
> 移植依据：原版 `DXMainClient/DXGUI/Generic/ExtrasWindow.cs`（附加窗按钮组）+ `DXMainClient/DXGUI/Generic/MainMenu.cs`（`BtnCredits_LeftClick`/`LaunchMapEditor`）+ `ClientCore/ProcessLauncher.cs`（`StartShellProcess`），去 XNA。
> 端口/配置：地图编辑器 exe 路径 `ClientDefinitions.ini [Settings] MapEditorExePath`（默认 `FinalSun/FinalSun.exe`）/ `UnixMapEditorExePath`；更新日志 `ChangelogURL`、鸣谢 `CreditsURL`（默认 moddb 链接）；关于信息 = `LongGameName`/`LocalGame` + `ProgramConstants.GAME_VERSION` + 引擎。
> 涉及文件：`Infrastructure/ExtrasService.cs`、`Components/Shared/ExtrasWindow.razor`、`Components/Layouts/MainLayout.razor`（挂载宿主）、`Components/Pages/MainMenu.razor`（「鸣谢」入口）、`Program.cs`（注册）、`wwwroot/css/app.css`（`.exw-*`）。
> 状态：**实现完成，待运行验收**（主工程构建 0 错误、本次改动 0 新增 CS 警告）。

---

## 1. 入口与窗口

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 1.1 | 主菜单 → 鸣谢 | 弹出「附加功能 EXTRAS MENU」模态窗（pop-in 240ms），顶部标题栏 + 竖排按钮 | ☐ |
| 1.2 | 取消 | 点「取消」→ fade-out 120ms 关闭回主菜单，无残留状态 | ☐ |
| 1.3 | 点遮罩 | 点模态层外部遮罩 → 等同取消关闭 | ☐ |
| 1.4 | 重复打开 | 再次点「鸣谢」可再次弹出（状态复位，无并发残留） | ☐ |

## 2. 地图编辑器

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 2.1 | 启动 | 点「地图编辑器」→ 以 `GamePath` 相对路径（Windows：`MapEditorExePath`；Unix：`UnixMapEditorExePath`）启动外部进程（UseShellExecute=false） | ☐ |
| 2.2 | 成功提示 | 进程启动成功 → toast「已启动地图编辑器」+ 关闭附加窗 | ☐ |
| 2.3 | 失败处理 | exe 不存在/启动异常 → toast「地图编辑器启动失败：{原因}」，附加窗保持打开可重试 | ☐ |

## 3. 更新日志 / 鸣谢

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 3.1 | 更新日志 | 点「更新日志」→ 系统默认浏览器打开 `ChangelogURL` + toast 提示 + 关闭附加窗 | ☐ |
| 3.2 | 鸣谢 | 点「鸣谢」→ 系统默认浏览器打开 `CreditsURL` + toast 提示 + 关闭附加窗 | ☐ |
| 3.3 | 打开失败 | 无法启动浏览器 → toast「无法打开…：{原因}」，附加窗保持打开 | ☐ |

## 4. 关于

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 4.1 | 打开关于 | 点「关于」→ 附加窗关闭，弹出 Info MessageBox 展示客户端名/游戏名 + 游戏版本（`GAME_VERSION`）+ 引擎 | ☐ |
| 4.2 | 关闭关于 | MessageBox 确定后回到主菜单 | ☐ |

## 5. 回归

| # | 检查项 | 通过 |
|---|--------|------|
| 5.1 | 主菜单其余按钮（战役/多人/遭遇战/局域网/设置等）导航不回归 | ☐ |
| 5.2 | MessageBox / LoginModal 弹窗层级正常（附加窗 z-index 同层互斥，无遮挡残留） | ☐ |
| 5.3 | 构建 0 错误；本次改动无新增 CS 警告 | ☐ |

---

## 已知限制

- 统计窗口（Statistics）：原版附加窗含「Statistics」按钮，本移植归入 M4.8 统计窗口里程碑，附加窗暂不显示该按钮。
- 关于窗口：原版无独立 About 窗口（信息散见于 UpdateWindow），本移植以 MessageBox 汇总展示客户端/游戏名 + 版本 + 引擎，无外部跳转。
- 地图编辑器依赖 MOD 安装目录中实际的 FinalSun 可执行文件；未安装时启动失败并 toast 提示（不崩溃）。
