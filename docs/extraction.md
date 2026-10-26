# CNCNet 逻辑层抽取清单（Online / Domain / ClientUpdater 去 XNAUI 化调研）

> 版本：v0.1（待用户确认后执行）
> 状态：**待确认** —— 确认后按 §7 顺序落地，并回写 `tasks.md` 勾选对应任务
> 调研基线：原逻辑源 `f:\work\PhotinoCLient\CncnetClient\xna-cncnet-client`（HEAD `e6e367b`，2026-08-01）
> 配套文档：`spec.md`（§4 复用清单）、`tasks.md`（M1 逻辑抽取）
> 硬性约束：逻辑类库**不得引用** `Rampastring.XNAUI` / `MonoGame` / `Microsoft.Xna.Framework.*` / `System.Windows.Forms`；**禁用 `System.Drawing.*`**（见 spec §2、§6.2）

---

## 1. 总体结论

| 模块 | 原文件数 | 直搬（零耦合） | 轻改（类型/条件替换） | 重耦合（需重构） |
|---|---|---|---|---|
| ClientUpdater | 21 | **21** | 0 | 0 |
| Online | 38 | **34** | 2 | 2 |
| Domain | 66 | **54** | 10 | 2 |
| **合计** | **125** | **109** | **12** | **4** |

- **绝大多数文件（109/125，87%）可直接搬运**，仅需调整命名空间与项目引用。
- 真正需要**重构**的只有 4 个文件，耦合点高度集中、模式单一，风险可控：
  - `Online/CnCNetManager.cs`、`Online/CnCNetUserData.cs` —— `WindowManager` 依赖 + 线程调度
  - `Domain/Multiplayer/LAN/LANPlayerInfo.cs`、`Domain/Multiplayer/CnCNet/TunnelHandler.cs` —— `WindowManager.AddCallback`
- **无 DXGUI 业务逻辑夹带问题**：三个模块均不依赖 XNAUI 控件，DXGUI 抽取风险（spec 风险 #1）主要落在 `DXGUI/Multiplayer/**`，不在本次范围（另立清单）。
- **DirectDraw 三文件归类澄清**：`DirectDrawWrapper` / `DirectDrawWrapperManager` / `DirectDrawCompatibilityChecker` 与**客户端 UI 渲染无关**，是**游戏进程的渲染方式（ddraw 兼容层）与 Windows 兼容性配置**，属于游戏启动链路，全部保留（见 §5.4），不属"剥离"项。

### 耦合点总量（grep 基线）

| 耦合模式 | 出现文件 | 规模 |
|---|---|---|
| `WindowManager` 构造器依赖 | CnCNetManager、CnCNetUserData、TunnelHandler、LANPlayerInfo、DirectDrawCompatibilityChecker | 5 |
| `WindowManager.AddCallback` 调用 | CnCNetManager（≈30 处）、TunnelHandler（3）、LANPlayerInfo（1） | ≈34 处 |
| XNA `Color` / `Microsoft.Xna` | Online：CnCNetManager、IRCColor、ChatMessage；Domain：MultiplayerColor、LANColor 等 | 10 |
| `Texture2D`（地图预览/房间贴图） | MapLoader、CnCNetGame、CustomCnCNetGame、LANLobbyUser | 4 |
| `System.Windows.Forms`（条件编译） | Domain/MainClientConstants.cs（`#if WINFORMS`） | 1 |
| Win32 Registry（Windows 专属） | DirectDrawCompatibilityChecker | 1 |

---

## 2. 调研范围与方法

- **范围**：`ClientUpdater/`、`DXMainClient/Online/`、`DXMainClient/Domain/**`（不含 `DXGUI/**`）。
- **方法**：grep 基线（`WindowManager|AddCallback|Microsoft.Xna|MonoGame|Rampastring.XNAUI|System.Windows.Forms|Texture2D|SpriteBatch|DllImport|Registry`）+ 逐文件人工核对。
- **分类定义**：
  - **直搬**：无上述任一耦合模式，仅改命名空间/项目引用即可编译。
  - **轻改**：仅涉及 XNA 类型替身（`Color`→`Rgba32`、`Point`→ImageSharp `Point`）、`Texture2D`→PNG 字节、`#if WINFORMS` 条件分支、或对纯 .NET 依赖（如 `GameProcessLogic`）改引用，不涉及线程/UI 依赖。
  - **重耦合**：持有 `WindowManager` 或调用 `AddCallback`，需注入 `IDispatcher` 重构。
  - 原调研曾设"Windows 专属（剥离）"类，经核实 DirectDraw 三文件为**游戏启动配置**而非客户端 UI，已归入直搬/轻改并全部保留（§5.4），不再单列剥离项。

---

## 3. ClientUpdater 模块 —— 零耦合，整目录直搬

**21 文件全部直搬**，新建 `CnCNetClient.Updater`（net10.0）。

| 子目录/文件 | 处理 | 说明 |
|---|---|---|
| `Updater.cs` | 直搬 | 事件驱动（`OnLocalFileVersionsChecked` 等），无 UI/XNA |
| `UpdateMirror.cs` / `UpdateComponent.cs`(CustomComponent) | 直搬 | 纯配置/HTTP |
| `UpdaterFileInfo.cs` / `VersionState.cs` | 直搬 | 纯数据 |
| `Compression/**`（LZMA/LZ/RangeCoder，14 文件） | 直搬 | 纯算法，无外部依赖 |

**适配点（不属于耦合，属工程适配）**
- 依赖 `CnCNetClient.Core`（原 ClientCore 引用）。
- 依赖 `Microsoft.AspNet.WebApi.Client`（HTTP JSON），保留 NuGet 引用。
- 新 exe 名与单目录结构适配（spec §6.7）——此改动在集成阶段，不在类库抽取阶段。

---

## 4. Online 模块 —— 34 直搬 + 2 轻改 + 2 重耦合

**38 文件**，新建 `CnCNetClient.Online`（net10.0）。

### 4.1 直搬（34）

| 分组 | 文件 |
|---|---|
| 事件参数（18） | `EventArguments/*.cs` 全部 |
| IRC 模型（8） | `Channel`、`ChannelUser`、`Connection`、`IRCUser`、`IUserCollection`、`SortedUserCollection`、`UnsortedUserCollection`、`Server` |
| 网络/工具（6） | `CnCNetGameCheck`、`FileHashCalculator`、`IConnectionManager`、`PrivateMessageUser`、`QueuedMessage`、`QueuedMessageType` |
| 私聊（1） | `PrivateMessageHandler`（无 XNA/UI 依赖，构造器改由外部传入 `IDispatcher`/`IMessageView` 场景下仍属直搬） |
| 其他（1） | `RecentPlayer` |

> 注：`PrivateMessageHandler` 虽在 UI 层由 `PrivateMessagingWindow` 组装，但本文件自身无 XNAUI 依赖，归直搬；其与 UI 的装配点（`ChatListBox` 右键、`IMessageView`）在 DXGUI 层处理。

### 4.2 轻改（2）

| 文件 | 改动点 |
|---|---|
| `IRCColor.cs` | `Color` → `Rgba32`（+ `int Code`、`bool IsDark` 保留）；配合 spec §4.2-A 颜色常量表 |
| `ChatMessage.cs` | `Color` → `Rgba32`（构造器 `Color.White` 等默认值走常量表）；其余纯数据 |

### 4.3 重耦合（2）—— 需重构

**`CnCNetManager.cs`**（核心，改动最大）
- 构造器 `public CnCNetManager(WindowManager wm, ...)` → 注入 `IDispatcher`（spec §6.1）。
- `private WindowManager wm;` → `private IDispatcher dispatcher;`。
- **≈30 处 `wm.AddCallback(new Action<...>(DoXxx), args...)`** 统一替换为 `dispatcher.Post(() => DoXxx(args))`（或 `await dispatcher.InvokeAsync(...)`，网络线程回调场景用 `Post`）。
- XNA `Color`：IRC 聊天颜色表（15 条）→ `Rgba32` 常量表；`AssetLoader.GetColorFromString` → Integration 颜色解析服务；`ChatMessage(Color.X, ...)` 全部改常量。
- 类内事件（`ConnectionLost`、`ChatMessageReceived` 等）保持不变，UI 层订阅。

**`CnCNetUserData.cs`**
- 构造器 `public CnCNetUserData(WindowManager windowManager)` → 改为无参/注入 `IUserDataPersistence` 或直接去掉 UI 依赖。
- 关键点：`windowManager.GameClosing += WindowManager_GameClosing`（退出时 `Save()`）→ 改为订阅宿主应用退出事件（Integration 提供 `IApplicationLifetime` 或 `IAppExitNotifier`）。
- 其余为 IRC 用户数据模型 + INI 持久化，无 XNA 类型。

---

## 5. Domain 模块 —— 54 直搬 + 10 轻改 + 2 重耦合

**66 文件**，新建 `CnCNetClient.Domain`（net10.0）。

### 5.1 直搬（54）

| 分组 | 文件 |
|---|---|
| CnCNet 大厅数据（11） | `GameCollection`、`HostedCnCNetGame`、`DefaultCnCNetGame`、`CustomCnCNetGame`(剔除 Texture2D 后)、`CnCNetPlayerCountTask`、`CnCNetTunnel`、`MapEventArgs`、`MapSharer`、`NameValidator`、`SHA1EventArgs`、`TimedHttpClient` |
| LAN（10） | `ClientIntCommandHandler`、`ClientNoParamCommandHandler`、`ClientStringCommandHandler`、`HostedLANGame`、`LANClientCommandHandler`、`LANServerCommandHandler`、`ServerNoParamCommandHandler`、`ServerStringCommandHandler`、`LANPlayerInfo`(剔除线程后)、`NetworkMessageEventArgs` |
| 地图/游戏模式（19） | `Map`(剔除 Point 后)、`MapLoader`(剔除 Texture2D 后)、`MapFileWatcher`、`MapChangeEventArgs`、`MapFileEventArgs`、`CustomMapCache`、`FastMapPreviewExtractor`、`MapPreviewExtractor`、`MapPreviewCacheManager`、`IMapPreviewCacheManager`、`IMapPreviewExtractor`、`GameMode`、`GameModeMap`、`GameModeMapBase`、`GameModeMapCollection`、`IGameModeMap`、`IReadOnlyGameModeMapCollection`、`GameOptionPresets`、`GenericHostedGame` |
| 多人配置（6） | `AllianceHolder`、`PlayerExtraOptions`、`PlayerHouseInfo`、`PlayerInfo`、`TeamStartMapping`、`TeamStartMappingPreset` |
| 单机/任务（4） | `CoopHouseInfo`、`CoopMapInfo`、`Mission`、`SavedGame`、`SavedGamePlayer`(含) |
| 其他（3） | `CustomMissionHelper`、`DiscordHandler`、`FinalSunSettings` |

> 注：表中标"剔除后"的项在 §5.2 中列改动点；此处归类为直搬前提是轻改完成。行数有交叉，以 §1 统计为准（直搬 54）。

### 5.2 轻改（10）—— 类型/条件替换

| 文件 | 改动点 |
|---|---|
| `Multiplayer/MultiplayerColor.cs` | `Color` → `Rgba32` + 常量表；`LANColor.cs` 同 |
| `Multiplayer/Map.cs` | `using Point = Microsoft.Xna.Framework.Point;` → ImageSharp `Point`（int 语义，行为等价）；移除未使用的 `using DXGUI...GameLobby` |
| `Multiplayer/MapLoader.cs` | `GetPreviewTextureFromMap` 返回 `Texture2D` → 返回 `Image<Rgba32>` 或 PNG `byte[]`（配合 MapPreviewCacheManager 缓存）；并行度限制 `MaxDegreeOfParallelism`（spec §6.4） |
| `Multiplayer/CnCNet/CnCNetGame.cs`、`CustomCnCNetGame.cs` | `Lazy<Texture2D?> LoadTexture()` → 加载预览 PNG `byte[]`（`Image<Rgba32>`）；`RegistryInstallPath` 保留（游戏路径探测，仍有用） |
| `Multiplayer/LAN/LANLobbyUser.cs` | `Texture2D gameTexture` → 预览图标识（PNG bytes / 图片 key），由 UI 层渲染 |
| `Domain/MainClientConstants.cs` | `#if WINFORMS` 分支（`MessageBox.Show`）剥离，统一走 `DisplayErrorAction`（已抽象，spec §2.8）；`ProcessLauncher.StartShellProcess` 保留 |
| `DirectDrawWrapperManager.cs` | `using ClientGUI;` 仅为引用 `GameProcessLogic`（纯 .NET）→ 改引用 Integration 中的 `GameProcessLogic`（该文件已计划移入 Integration），无 XNA/UI 依赖，见 §5.4 |
| `DirectDrawCompatibilityChecker.cs` | `CheckAndPromptFix(WindowManager)` 的 `XNAMessageBox`/`AdminRestarter` 弹窗 → 改为非 UI 回调（返回状态由 Blazor 弹窗处理）；Registry 检查/修复逻辑（`Examine`/`Fix`）保留，Windows 平台守卫，见 §5.4 |

### 5.3 重耦合（2）—— 线程调度改 `IDispatcher`

| 文件 | 改动点 |
|---|---|
| `Multiplayer/LAN/LANPlayerInfo.cs` | `UpdatePing(WindowManager wm)` + `wm.AddCallback(PlayerPinged, ...)` → 注入 `IDispatcher`（构造器/属性），`dispatcher.Post(PlayerPinged)` |
| `Multiplayer/CnCNet/TunnelHandler.cs` | 构造器 `TunnelHandler(WindowManager wm, ...)`（现为 `wm.Game` 取参数）→ 注入 `IDispatcher`；3 处 `wm.AddCallback` → `dispatcher.Post` |

### 5.4 DirectDraw 三文件 —— 游戏启动配置，非客户端渲染，全部保留

> **归类澄清（已核实源码与调用链）**：用户指出"DirectDraw 是设置游戏的渲染方式，不影响客户端"，**结论正确**。这三者配置的是**游戏进程**（`game.exe` 通过 ddraw 兼容层渲染）而非客户端 UI（新客户端用 WebView2 渲染，与 DirectDraw 无关）。调用方均在游戏启动/显示设置链路：`DisplayOptionsPanel`（设置→显示下拉）、`MainMenu`（启动前兼容性检查）、`GameProcessLogic`。**故不属"Windows 专属可剥离"，全部保留**，归入游戏启动配置范畴。

| 文件 | 作用 | 处理 | 改动点 |
|---|---|---|---|
| `DirectDrawWrapper.cs` | 解析 `Renderers.ini` 中各渲染器（ddwrapper/cnc-ddraw/DxWnd 等）配置：`DLLPath`、`ConfigFileName`、窗口化键、`UseQres`、`SingleCoreAffinity` | **直搬** | 纯 INI 解析，无 XNA/UI |
| `DirectDrawWrapperManager.cs` | 管理渲染器列表/默认项/选中项；`Save()` 写 `UserINISettings.Renderer` 并设 `GameProcessLogic.UseQres`/`SingleCoreAffinity` | **轻改** | `using ClientGUI;` 仅用于 `GameProcessLogic`（纯 .NET，计划移入 Integration）→ 改引用；无 XNA/UI |
| `DirectDrawCompatibilityChecker.cs` | 检查/修复 Windows 注册表 `AppCompatFlags/Layers` 中针对**游戏 exe** 的兼容模式设置 + 修复 `__COMPAT_LAYER` 环境变量 | **轻改** | 仅 `CheckAndPromptFix(WindowManager)` 用 `XNAMessageBox`/`AdminRestarter` 弹窗 → 改为非 UI 回调（`Check(out bool requireFix, out bool requireAdmin, ...)` 返回状态，由 Blazor 弹窗决定是否调用 `Fix()`）；Registry 逻辑保留 + Windows 平台守卫 |

**依赖关系**：新客户端"设置 → 显示"仍需要渲染器下拉（决定游戏以何种渲染方式/窗口模式启动），因此首版保留到设置页；`CompatibilityChecker` 的注册表修复对游戏启动仍有意义，保留逻辑、替换弹窗交互。

---

## 6. 接口替身与前置改动（`CnCNetClient.Integration`）

抽取前需先就位（或与抽取并行），供重耦合/轻改文件使用：

### 6.1 `IDispatcher`（spec §6.1）

```csharp
public interface IDispatcher
{
    void Post(Action action);            // 等同原 AddCallback（异步，网络线程→UI）
    Task InvokeAsync(Action action);     // 包装 Blazor Dispatcher
}
```
- 注册为单例，内部委托 InfiniFrame 的 `Dispatcher`。
- 替换映射：`wm.AddCallback(new Action<T>(DoX), arg)` → `dispatcher.Post(() => DoX(arg))`。

### 6.2 XNA 类型替身（spec §6.2 / §4.2-A）

| 原类型 | 替身 | 落点 |
|---|---|---|
| `Color` | `SixLabors.ImageSharp` `Rgba32` + `XnaCompat` 颜色常量表（含 IRC 15 色、常用命名色） | `XnaCompat/` |
| `Point` / `Rectangle` / `Size` | ImageSharp 同名类型（int 语义） | `XnaCompat/` |
| `Texture2D`（预览/房间图） | `Image<Rgba32>` / PNG `byte[]`（base64 缓存，spec §6.4） | 各调用点 |
| `AssetLoader.GetColorFromString` | Integration `ColorParser`（集中一处，spec §4.2-A） | `XnaCompat/` |

### 6.3 生命周期替身

| 原依赖 | 替身 |
|---|---|
| `windowManager.GameClosing` | Integration `IAppExitNotifier`（宿主退出事件） |

---

## 7. 抽取执行顺序（确认后按此推进）

1. **Integration 前置**：`IDispatcher`、`XnaCompat`（`Rgba32` 常量表、`ColorParser`、Point/Rectangle/Size 替身）、`IAppExitNotifier`。
2. **ClientUpdater**（全直搬）→ 建 `CnCNetClient.Updater`，引用 Core，编译。
3. **Online 直搬 34 + 轻改 2** → 建 `CnCNetClient.Online`，编译。
4. **Online 重耦合 2**：`CnCNetUserData`（去 WindowManager，接 `IAppExitNotifier`）→ `CnCNetManager`（注入 `IDispatcher`，替换 ≈30 处 `AddCallback`，IRC 色表→常量表）。
5. **Domain 直搬 54 + 轻改 10** → 建 `CnCNetClient.Domain`，编译。
6. **Domain 重耦合 2**：`LANPlayerInfo`、`TunnelHandler` 注入 `IDispatcher`。
7. **验证**：
   - 4 个类库编译通过，0 error；
   - grep 基线：`Microsoft.Xna|Rampastring.XNAUI|System.Windows.Forms|System.Drawing` 在 4 类库中 **0 命中**；
   - `ClientConfiguration` 无 UI 初始化成功（M1.13 出口条件）。

---

## 8. 与 tasks.md 映射

| tasks.md | 本清单覆盖 |
|---|---|
| 1.2 建 Domain/Online/Updater 类库 | §3/§4/§5 全部 |
| 1.6 替换 XNA 类型 | §4.2、§5.2、§6.2 |
| 1.6.1 位图绘制适配 | MapLoader/CnCNetGame 预览（§5.2） |
| 1.7 定义 IDispatcher、替换 CnCNetManager 的 WindowManager | §6.1、§4.3 |
| 1.8 替换全部 AddCallback（grep 基线核对） | §4.3、§5.3（≈34 处） |
| 1.9 聊天颜色表/IRC 颜色迁移 | §4.2、§6.2 |
| 1.13 M1 出口条件 | §7-8 |

---

## 9. 待确认项

- [x] 1. 模块统计与分类（已确认，按此执行）。
- [x] 2. §5.4 DirectDraw 三文件：**全部保留**（属游戏启动配置，非客户端渲染；Wrapper 直搬、Manager/Checker 轻改），已写入清单。
- [ ] 3. 按 §7 顺序开始执行，并在 `tasks.md` 勾选 1.2/1.6/1.7/1.8/1.9。
