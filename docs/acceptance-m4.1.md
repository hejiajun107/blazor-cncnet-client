# M4.1 设置中心验收清单

> 范围：spec **§5.3 设置中心** + **§6.10 持久化体系**。多 Tab 设置页（/settings）读写 settings INI，控件即改即存；主题/登录偏好持久化迁移至 settings INI。
> 涉及文件：`SettingsService.cs`（新建，scoped）、`Settings.razor`（新建，/settings）、`ThemeProvider.cs`（持久化改 INI）、`LoginModal.razor`/`MainMenu.razor`（登录偏好改 INI）、`Program.cs`（`UserINISettings.Initialize` + 注册 `SettingsService`）、`app.css`（设置页样式 + 720p 紧凑）、`app.js`（主题首帧回显，移除登录偏好 localStorage）。
> 状态：实现完成，主工程构建 **0 错误**。
> 说明：settings INI 为本移植版实际的 **`ClientSettings.ini`**（游戏根目录，`UserINISettings.Initialize("ClientSettings.ini")`，合并 `Resources/UserDefaults.ini` 默认值）；非游戏级 `RA2MD.ini`（由 `[Settings] SettingsFile` 决定的游戏配置文件，两者同属 §6.10「settings INI」体系）。

---

## 0. 前置准备（必读）

- 环境检测需指向已安装的 RA2/YR 游戏本体（settings INI 写入游戏根目录，需可写权限）。
- 建议先静态核对（1–2，单客户端），再做双轨持久化（3）与退出兜底（4）。
- 验收辅助：设置页每个控件改动后，检查游戏根目录 `ClientSettings.ini` 是否即时更新对应键。

## 1. 设置中心页面（/settings，单客户端静态）

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 1.1 | 主菜单点「设置」 | 导航到 `/settings`，蓝色系内页（`data-theme="inner"` 生效），顶栏含「返回」+「设置中心 SETTINGS TERMINAL · 即改即存」+ 右侧玩家名 | ☐ |
| 1.2 | 左导航六个 Tab | 显示/音频/游戏/网络/更新/隐私，点击切换生效，激活项高亮描边 | ☐ |
| 1.3 | 显示 Tab | 界面主题下拉 / 窗口化 / 无边框 / 客户端帧率滑块 / 启动游戏最小化 / 强制最低画质，均显示当前值 | ☐ |
| 1.4 | 音频 Tab | 主音量 / 背景音乐 / 语音音量滑块（% 显示）/ 主菜单音乐 / 聊天消息音效开关 | ☐ |
| 1.5 | 游戏 Tab | 滚动速度 / 双击间隔 / 游戏速度 / 游戏难度下拉 / 自动去昵称下划线 | ☐ |
| 1.6 | 网络 Tab | 玩家名输入框 / 记住我 / 离开大厅保持连接 / 启动自动连接 / Ping 自定义隧道 / 允许地图分享 / 始终显示隧道列表 | ☐ |
| 1.7 | 更新 / 隐私 Tab | 更新：启动检查开关 + 占位提示；隐私：已接受隐私政策 / Discord / Steam 集成 | ☐ |
| 1.8 | 720p 紧凑 | 窗口 1280×720 下布局不溢出（左导航 + 面板正常滚动，控件可用） | ☐ |

## 2. 即改即存（核心验收）

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 2.1 | 切换任一开关（如窗口化） | 立即写入 `ClientSettings.ini` 对应键，无需点保存按钮 | ☐ |
| 2.2 | 拖动滑块（如主音量） | 松开后立即落盘（`SaveSettings`），无额外确认步骤 | ☐ |
| 2.3 | 切换界面主题下拉 | 主题即时切换（applyTheme），同时写入 `[MultiPlayer] Theme` | ☐ |
| 2.4 | 修改玩家名并回车 | 立即写 `[MultiPlayer] Handle`，页顶玩家名刷新 | ☐ |
| 2.5 | 变更后回主页再进设置页 | 改动值已持久化，重新加载后仍显示新值（读写闭环） | ☐ |

## 3. 持久化接线（spec §6.10）

| # | 检查项 | 预期结果 | 通过 |
|---|--------|---------|------|
| 3.1 | 启动初始化 | `Program.cs` 启动时 `UserINISettings.Initialize("ClientSettings.ini")`，合并 `UserDefaults.ini` 默认值；日志无「UserINISettings not initialized!」 | ☐ |
| 3.2 | 主题权威来源 | 主题正式以 `[MultiPlayer] Theme` 为准；启动加载 INI 主题（非 localStorage），首帧无闪变（localStorage 仅回显缓存） | ☐ |
| 3.3 | 主题跨实例一致 | 改主题后 `ClientSettings.ini` 的 `[MultiPlayer] Theme` 更新；重启客户端主题一致 | ☐ |
| 3.4 | 登录偏好 | 登录 Modal「记住我/持久模式/自动连接」勾选后写 `[MultiPlayer]` 的 `SkipConnectDialog`/`PersistentMode`/`AutomaticCnCNetLogin`；`Handle` 与输入昵称一致 | ☐ |
| 3.5 | 自动连接 | `ClientSettings.ini` 中 `SkipConnectDialog=true` 且 `Handle` 非空 → 主菜单启动自动连接跳过 Modal（沿用 M3.1 行为） | ☐ |
| 3.6 | localStorage 收敛 | `app.js` 已移除 `saveLoginPrefs`/`loadLoginPrefs`；仅保留 `cncnet.theme` 作首帧回显缓存 | ☐ |

## 4. 退出兜底（spec §6.10）

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 4.1 | 改设置后立即关窗 | 进程退出前 `ApplicationExiting` 触发 `SettingsService.Save()`，改动不丢失 | ☐ |

## 5. 回归（M3 不回归）

| # | 检查项 | 通过 |
|---|--------|------|
| 5.1 | 登录 Modal 流程（连接/记住我/自动连接）不回归（对照 acceptance-m3.1） | ☐ |
| 5.2 | 主菜单/大厅/加载页/游戏信息面板不回归（对照 acceptance-m3.5~m3.7） | ☐ |
| 5.3 | 主题切换（菜单/内页/红警）不回归，红警金×橄榄军绿样式保持 | ☐ |
| 5.4 | 连接状态灯 / Toast / 横幅不回归（对照 acceptance-m3.10） | ☐ |
| 5.5 | 构建 0 错误；本次改动无新增警告 | ☐ |

---

## 已知限制

- settings INI 使用本移植版既定的 **`ClientSettings.ini`**（游戏根目录），非 spec 举例的 `RA2MD.ini`；两者同属 §6.10 settings INI 体系，`UserDefaults.ini` 合并规则一致。
- 更新 Tab 的「立即检查」为占位，待 M4.3 自动更新端到端接入后启用。
- 音量等滑块仅持久化数值，实际音频输出/音量调节逻辑（音频引擎接入）后续里程碑接线（当前阶段以「写入 settings INI」为验收口径）。
- 部分设置项（如 Discord/Steam 集成、帧率）为原版语义持久化，运行期行为接入对应系统后生效。
