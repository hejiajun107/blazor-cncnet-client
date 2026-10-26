# 登录页调研（M3.1 前置研究）

> 状态：调研完成，待评审
> 目的：回答「客户端有登录页吗」→ 研究原版 `CnCNetLoginWindow` 机制 → 为 M3.1 登录页设计提供依据。
> 结论先行：**有登录页，但不是「账号密码认证」** —— 原版登录页只有「玩家名 + 3 个复选框」，登录本质是**用昵称连接 CnCNet IRC**（无密码）。新版 Blazor 尚无登录页（主菜单「多人游戏」按钮为占位），但连接基础设施（`CnCNetManager`/`Connection`/`NameValidator`）已从 M1 就绪。

---

## 1. 原版登录页：`CnCNetLoginWindow`

位置：`DXMainClient/DXGUI/Multiplayer/CnCNet/CnCNetLoginWindow.cs`

### 1.1 界面组成（300×220 浮窗）

| 控件 | 说明 |
|---|---|
| 标题 | "CONNECT TO CNCNET" |
| 玩家名输入框 `tbPlayerName` | 最大长度 = `ClientConfiguration.Instance.MaxNameLength`；禁 IME |
| `chkRememberMe` | "Remember me" —— 记住我（记下后下次跳过对话框自动连接） |
| `chkPersistentMode` | "Stay connected outside of the CnCNet lobby" —— 持久连接模式 |
| `chkAutoConnect` | "Connect automatically on client startup" —— 启动自动连接；**仅当 remember+persistent 同时勾选才可勾选** |
| `btnConnect` / `btnCancel` | 连接 / 取消 |

**无密码输入框。** CnCNet 的"登录"= 昵称即身份，昵称注册/密码保护走 CnCNet 论坛侧，客户端不处理密码。

### 1.2 连接流程（BtnConnect_LeftClick）

1. `NameValidator.IsNameValid` 校验昵称合法性（失败弹 MessageBox）
2. 设 `ProgramConstants.PLAYERNAME = 昵称`
3. 持久化设置 → `UserINISettings.SaveSettings()`：
   - `[MultiPlayer] Handle`（PlayerName）
   - `[MultiPlayer] SkipConnectDialog`（remember me）
   - `[MultiPlayer] PersistentMode`
   - `[MultiPlayer] AutomaticCnCNetLogin`
4. 触发 `Connect` 事件

### 1.3 宿主与连接（CnCNetLobby）

- `CnCNetLobby.cs:626-628`：创建 `loginWindow`，订阅 `Connect` / `Cancelled`
- `LoginWindow_Connect` → **`connectionManager.Connect()`**（`CnCNetManager.Connect` → `Connection.ConnectAsync` 连 IRC），然后 `loginWindow.Disable()`
- `LoginWindow_Cancelled` → `loginWindow.Disable()`，顶栏回主界面
- 自动登录：`LoadSettings()` 读设置回填控件；若 remember 已勾 → 直接触发 `BtnConnect`（跳过对话框）

### 1.4 设置持久化字段（UserINISettings，写 settings INI）

`[MultiPlayer]`：`Handle`、`SkipConnectDialog`、`PersistentMode`、`AutomaticCnCNetLogin`

---

## 2. 新版 Blazor 现状

- **无登录页、无登录相关组件/服务**（M3 尚未开始）
- 主菜单已有「多人游戏 Multiplayer」按钮，但 [MainMenu.razor:113-131](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Components/Pages/MainMenu.razor#L113-L131) 目前 toast 占位
- **连接基础设施已就绪**（M1 复制）：
  - `CnCNetClient.Online/CnCNetManager.cs`：`Connect()`/`Disconnect()`/`IsConnected`/`OnDisconnected`/`OnErrorReceived`
  - `Connection`（IRC 连接）、`NameValidator`（昵称校验）
- **持久化未接线**：`UserINISettings.Initialize` 未调用（并入 M4 4.1.1），登录所需 4 个字段的读写暂不可用

---

## 3. 迁移设计建议（供 M3.1 参考）

1. **形态**：贴近原版用**浮窗/Modal**（复用 `MessageBox` 宿主机制扩展 `LoginDialog`），或 Blazor 独立路由页 `/login` —— 待定。
2. **字段**：玩家名 + Remember me / Persistent mode / Auto connect（原版同款三复选框，`AutoConnect` 依赖前两者）。
3. **数据流**：`MainMenu`「多人游戏」→ 若 `SkipConnectDialog` 已勾则直接连接，否则弹登录 → `CnCNetManager.Connect()` → 连接成功进大厅（M3.2）。
4. **持久化**：登录设置写 settings INI 4 字段，依赖 M4 4.1.1 的 `UserINISettings.Initialize`；过渡期可先临时内存 + localStorage 占位。
5. **导航**：连接成功 → 大厅页（M3.2 `CnCNetLobby` 迁移）；断开/失败 → toast + 回主菜单。

---

## 4. 待确认问题

1. 登录形态：**浮窗 Modal**（贴近原版）还是**独立路由页 `/login`**？
2. 认证范围：**仅昵称（贴近原版，无密码）** 还是预留密码/令牌字段？
3. 持久化依赖：登录设置是否要等 M4 4.1.1 的 `UserINISettings` 接线，还是 M3.1 先做轻量过渡（内存+localStorage）？
4. 本次是否先补 spec（M3.1 登录页条目）还是直接开工？
