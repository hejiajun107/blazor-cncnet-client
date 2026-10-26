# M4.4 LAN 验收清单

> 范围：spec **§5.3 LAN** / checklist **D3**（D3.1 LAN 大厅广播/发现；D3.2 LAN 建/加入房间 + 游戏配置 + 启动）。
> 移植依据：原版 `DXMainClient/DXGUI/Multiplayer/`（`LANLobby` / `LANLobbyBroadcastManager` / `LANMessageDeduplicator` / `LANPlayerManager`）+ `DXGUI/Multiplayer/GameLobby/LANGameLobby.cs`；Domain 层 LAN 命令处理器（`LANServerCommandHandler`/`LANClientCommandHandler` 及各命令实现）已随 M1 移植完成，本次复用。
> 端口常量：UDP `LAN_LOBBY_PORT=1232`（广播/发现）、TCP `LAN_GAME_LOBBY_PORT=1233`（游戏大厅）、玩家端口 `1234`；消息分隔符 `LAN_DATA_SEPARATOR=(char)01` / `LAN_MESSAGE_SEPARATOR=(char)02`。
> 涉及文件：`CnCNetClient.Online/LAN/`（`LanBroadcastManager`、`LanMessageDeduplicator`、`LanPlayerManager` 移植）、`BlazorCncnetClient/Infrastructure/LanLobbyService.cs`、`LanGameRoomService.cs`、`LanGameLaunchService.cs`、`LanGameNavigation.cs`（均 D3.2）、`Components/Pages/LanLobby.razor`（`/lan/lobby`）、`Components/Pages/LanGame.razor`（`/lan/game`，D3.2）、`Components/Pages/LanLoading.razor`（`/lan/loading`，D3.2）、`MainMenu.razor`（LAN 入口）、`Program.cs`（注册）、`wwwroot/css/app.css`。
> 状态：**D3.1 大厅 + D3.2 房间/配置/启动实现完成，待双端端到端验收**（主工程构建 0 错误、本次改动 0 新增警告）。待办：D3.2 端到端验收 + 加载存档局（spawnSG.ini GameID 匹配）。

---

## 1. LAN 大厅广播/发现（D3.1）

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 1.1 | 主菜单 → 局域网 | 进入 `/lan/lobby`；UDP 绑定 1232 并启动广播/发现；同一局域网内多开客户端互相可见 | ☐ |
| 1.2 | 玩家在线状态 | 客户端进入大厅即广播 `ALIVE`（游戏索引 + 昵称）；对方 5s 内出现在玩家列表；10s 无刷新自动移除 | ☐ |
| 1.3 | 大厅聊天 | 输入聊天 → 广播 `CHAT`（颜色索引 + 文本）；颜色选择持久化 `[Options] LANChatColor`；他方显示对应颜色 | ☐ |
| 1.4 | 退出大厅 | 广播 `QUIT`、关闭 socket/监听线程；他方玩家列表即时移除；不残留线程 | ☐ |
| 1.5 | 去重/防抖 | 消息 ID 去重（`MID_xxxxxxxx` 前缀，60s 过期清理），重放/回环广播不重复处理 | ☐ |
| 1.6 | 多网卡 | 广播到所有 up 网卡的 IPv4 广播地址（VPN/多路由器场景）；接口 5s 周期刷新 | ☐ |
| 1.7 | socket 失败 | 端口被占用/无广播接口 → 大厅显示错误提示，不崩溃，可退出重进 | ☐ |

## 2. LAN 建/加入房间 + 游戏配置 + 启动（D3.2）

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 2.1 | 创建房间 | 主机选地图/模式/人数 → TCP 监听 1233 → 广播 `GAME`（协议版本/版本/游戏/地图/模式/加载局/玩家/锁定/加载局标记/地图哈希，10 参数）→ 房间列表出现本机房间 | ☐ |
| 2.2 | 加入房间 | 双击/「加入游戏」→ 校验（本机游戏/未锁定/无同名/已存在于存档局）→ TCP 连主机 1233 → 发 `JOIN` → 进入 LAN 游戏房间 | ☐ |
| 2.3 | 房间内配置 | 玩家位/地图/规则同在线 GameLobby；主机改配置广播 `OPTS`，玩家改选项广播 `POPTS`，请求下发 `POREQ` | ☐ |
| 2.4 | 就绪/锁定/开始 | `READY`/`GETREADY`/`LAUNCH` 流程同在线；主机全部就绪后发 `LAUNCH` + 唯一游戏 ID | ☐ |
| 2.5 | 启动游戏 | 生成 spawn.ini（`[Tunnel]` 无/本地）+ `GameProcessLogic` 启动；端口用 LAN 玩家端口（1234）而非隧道；窗口最小化/恢复 | ☐ |
| 2.6 | 加载存档局 | 双端 `spawnSG.ini` 的 GameID 匹配 → `JOIN <id>` → `LANGameLoadingLobby` 加载流程 | ☐ |
| 2.7 | 退出/断线 | 房主/玩家退出广播 `RETURN`/`QUIT`；连接丢失处理（PING 10s、DROP 20s 阈值）；回大厅 | ☐ |
| 2.8 | 版本不匹配 | 加入不同 `GAME_VERSION` 房间 → 黄色版本不匹配提示，仍可加入 | ☐ |

## 3. 回归

| # | 检查项 | 通过 |
|---|--------|------|
| 3.1 | 在线大厅/游戏/隧道（M3）不回归；主菜单 9 按钮结构不变（LAN 复用现有按钮跳转） | ☐ |
| 3.2 | 设置/更新（M4.1-M4.3）不回归 | ☐ |
| 3.3 | 构建 0 错误；本次改动无新增警告 | ☐ |

---

## 已知限制

- LAN 依赖本机防火墙放行 UDP 1232 / TCP 1233 / TCP 1234（原版同要求）。
- 广播采用 IPv4 广播，跨网段/被路由器隔离的子网不可见（与局域网配置一致；公网 LAN 走 VPN 虚拟网卡）。
- 同网段多开客户端为验收主手段；本机「创建房间」后另一客户端加入即可完成 D3.2 双端流程。
