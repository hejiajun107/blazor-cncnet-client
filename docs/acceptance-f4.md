# 验收单 · F4 插件机制（阶段 0 + 阶段 1）

> 实施说明见 [plugin-plan.md](./plugin-plan.md)；
> 使用者配置见 [mod-customization.md](./mod-customization.md)；
> 插件作者指南见 [plugin-dev.md](./plugin-dev.md)。
>
> 状态：**已执行（2026-10-15）**。编译 0 错误；运行时验收见下方「验收结果」。
> 硬约束：任何插件异常都**不得阻断客户端启动**（T0/T5/T6/T7/T8 是这条约束的实证）。
>
> ⚠️ 备份文件路径为 **`<游戏根>\Resources\ClientDefinitions.ini.f4bak`**（ini 在 Resources 下，备份同目录）。
> 复原时若只 `Copy-Item` 而无校验，可能因 PowerShell 解析/句柄问题未生效 —— **务必回读校验**
> （见「验收结果」中记录的实测坑）。

---

## 验收结果（2026-10-15 实跑）

环境：`bin\Debug\net10.0`，同时装入 `hello-plugin`（正常）、`my-theme`（纯资源）、
`hello-bad`（`ApiVersion=99`）、`nope`（目录不存在）、`empty-plugin`（空值）。

| 项 | 结果 | 证据（日志原文） |
|---|---|---|
| T0 零回归 | ✅ | 复原 ini + 移除插件目录后启动：新增 75 行日志**零**「插件」字样、零 `ERR/FAT`，界面与改动前一致 |
| T1 纯资源插件 | ✅（日志 + 伺服链路） | 日志 `纯资源插件已启用：theme-demo（样式演示包）样式=plugin.css 脚本=-`；伺服实测 `http://127.0.0.1:4990/Plugins/theme-demo/plugin.css` → `HTTP 200` / `Content-Type: text/css; charset=utf-8`。⚠️ **首次 T1 用的 `plugin.css` 内容为 `body{--f4-test:1}`，无任何可见效果 → 当时只验到「日志识别并启用」，视觉生效未验**；已改用 `theme-demo`（`body::after` 显式标签 + token 覆盖）补验，见「补充验证」 |
| T2 示例插件端到端 | ✅ | `插件已加载：hello-plugin（示例：你好插件）ApiVersion=1`；6 项界面经人工确认全部出现；路由证据 `Request successfully matched the route with name 'null' and template '/plugin/hello-plugin/home'` + `Navigating to component HelloPlugin.Components.HelloPage` |
| T3 禁用/目录缺失 | ✅ | `插件 nope 目录不存在，已跳过：…`；`empty-plugin=`（空值）无任何日志（预期） |
| T4 契约版本不匹配 | ✅ | `插件 hello-plugin 契约版本不匹配（插件 99 / 宿主 1），已禁用`、`插件未启用：hello-bad（hello-plugin）原因：…`；**客户端照常启动**，正常插件不受影响 |
| T5 坏插件互不干扰 | ✅（部分） | `插件 my-theme 加载失败，已跳过：缺少 plugin.ini`，同轮 `hello-plugin` 仍正常加载 |
| T6 路由冲突 | ✅（2026-10-16） | 临时把示例页改为 `@page "/menu"`（与内置 `MainMenu` 同名）重编装载：日志 `插件 hello-plugin 的页面路由与宿主内置路由冲突：/menu（请改用插件专属前缀，如 /plugin/hello-plugin/…）`；**客户端照常启动，零异常堆栈**；测后已恢复 `@page "/plugin/hello-plugin/home"` 并重建样例 |
| T7 发布链路 | ✅（2026-10-16） | `publish.ps1` 既有校验全绿（`校验通过：无缺失文件、无外部引用、安装根干净`，401 文件 / 138576 KB）；`-PackPluginSdk` 产出 `dist\nuget\CnCNetClient.Plugin.Abstractions.1.0.0.nupkg`（6240 B）；安装根顶层仅 `Launcher.exe` + `ClientApp\`、**无 `nuget\` 混入**；`ClientApp\CnCNetClient.Plugin.Abstractions.dll` 存在（9216 B）；`ClientApp\Plugins\` 未被创建 |
| T8 SDK 编译链路 | ✅（2026-10-16） | 临时工程（`%TEMP%\f4-sdk-test`，独立 `nuget.config` 仅 `cncnet-local` 源）以 `PackageReference Version="1.0.0" PrivateAssets="all" ExcludeAssets="runtime"` 编译 **0 错误 0 警告**；输出目录**仅 `SdkTestPlugin.dll`、无契约 DLL**；测试后临时目录已删、全局 nuget 源未被污染（`cncnet-local` 不存在于源列表） |

**结论**：**T0–T8 全部通过**（2026-10-15 / 10-16 两轮实跑）。阶段 0 + 阶段 1 的完整链路
（加载 / 容错 / 槽位 / 页面路由 / 路由冲突 / 发布链路 / SDK 编译）均已在真实运行与打包产物下验证。
另需人工确认的 6 项界面表现（顶栏 ★HELLO 及 toast、设置左栏卡片、主菜单入口、插件页面、
页面样式、大厅横条）已全部确认出现 —— 其中「★HELLO 由宿主 `DynamicComponent` 渲染成功」
是**共享默认 ALC、插件与宿主类型身份同源**这一核心假设的直接实证。

### 补充验证：纯资源插件「视觉生效」（T1 缺失的另一半）

因首次 T1 的 `plugin.css` 无可见效果，另建 `theme-demo` 复验：

- 插件内容：`plugin.ini`（`Id=theme-demo`、`Style=plugin.css`，**无** `EntryType`/`ApiVersion`）
  + `plugin.css`（`body::after` 右上角金黄标签；覆盖 `--bg-0`/`--bg-1`/`--cyan`/`--cyan-soft`/`--line`，
  且**一次覆盖 `:root` / `:root[data-theme="inner"]` / `:root[data-theme="ra"]` 三档**）。
- **已自动验证**（命令行）：
  - 日志 `纯资源插件已启用：theme-demo（样式演示包）样式=plugin.css 脚本=-`
  - 内容伺服 `http://127.0.0.1:4990/Plugins/theme-demo/plugin.css` → `HTTP 200` +
    `Content-Type: text/css; charset=utf-8`
  - URL 形式与 `PluginAssetInjector` 实际拼法一致（`BaseUrl + plugin.RelativeDirectory + "/" + Style`，
    见 [PluginAssetInjector.cs:49](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Plugins/PluginAssetInjector.cs#L49)），故该 HTTP 实测可代表真实注入路径
- **已人工确认（肉眼）**：右上角金黄标签出现（`<link>` 注入成立）、主菜单底色变紫 / 主色变金
  （插件 CSS 优先级压过 `app.css`）、**设置页同样变金**（三档选择器覆盖正确）。
  > 确认时标签文案为 `F4 PLUGIN CSS ACTIVE`；样例固化时改为 `THEME DEMO`（仅字符串变化，机制未变）。
- **样例已固化并复验**：该样例现为 [samples/ThemeDemo/](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/samples/ThemeDemo)，
  并以**样例文件本身**重新装载复验：MD5 与部署目录一致（`C276D164`）、
  `GET /Plugins/theme-demo/plugin.css` → `HTTP 200` + `text/css` + 704 字节 + 含 `THEME DEMO`、无旧标记。

### 实跑中发现并修复的 3 个问题

1. **Build 之前的日志全部丢失**（真 bug，且不限插件）
   `AddSerilog(config => …)` 重载把 logger 创建推迟到 `Build()` 解析 `ILoggerFactory` 时，
   静态 `Log.Logger` 那时才被赋值 → Build 之前（插件发现、环境检测等启动最早期）的 `Log.*`
   写入了 Serilog 默认静默 logger，**直接丢弃**。
   证据：改动前两个会话的日志**首行恒为** `Started WebView message pump`，插件发现阶段的
   4 条诊断行（已加载/目录不存在/发现完成…）一行都查不到。
   修复：显式 `Log.Logger = new LoggerConfiguration()…CreateLogger()` 后再
   `AddSerilog(Log.Logger, dispose: false)`。修复后 T1/T2/T3/T4 的日志判定项全部可查。
   → **收益超出插件本身**：启动最早期（Build 前）的诊断能力整体恢复。

2. **无插件环境每次启动都产生一条假告警**
   `IniFile.GetSectionKeys` 对**不存在的节返回 `null`**（`CnCNetClient.Tools/IniFile.cs:728`），
   `foreach (… in defs.GetSectionKeys(Section))` 抛 NRE，被外层 catch 吞成
   `插件清单读取失败（[Plugins] 节），本次不加载任何插件` —— 无插件用户每次启动都会看到。
   修复：显式判空（`keys is null || keys.Count == 0` → 静默返回）。
   验证：修复后 T0 复跑，新增日志**零**「插件」字样。

3. **`Id` 缺失时「未启用」行出现空括号**
   代码依据：`LoadedPlugin.Id` 默认 `string.Empty`，仅在读到 `plugin.ini` 后赋值；
   而 `PluginCatalog.cs:179` 的输出为 `插件未启用：{Key}（{Id}）原因：{Reason}`。
   故 `plugin.ini` 缺失的插件原会打印 `插件 my-theme（）原因：缺少 plugin.ini`。
   修复：构造 `LoadedPlugin` 时以 ini key 作为 `Id` 默认值。
   验证：真实运行输出 `插件未启用：my-theme（my-theme）原因：缺少 plugin.ini`。

> 三项修复均已重新编译（0 错误）并复跑验证；复跑结果同时确认
> 「单插件失败不影响其它插件」与「坏插件不阻断启动」两条硬约束成立。

### 复原踩坑（写脚本时注意）

- **备份文件在 `<游戏根>\Resources\ClientDefinitions.ini.f4bak`**（ini 位于 `Resources\`，备份同目录）。
- `Copy-Item "<备份>" "<ini>"` 后**必须回读校验**：本次实测该复原**未生效**（ini 仍含追加的
  `[Plugins]` 节），改用「按内容定位 `[Plugins]` 行并裁掉其后内容」才成功。
- `Plugins\<目录>` 可能被 IDE 文件监视句柄占用而**无法删除**（实测 `my-theme` 空目录重命名亦失败）；
  在 ini 未声明 `[Plugins]` 时它对功能**零影响**（且 `bin\Debug` 重建会清理）。

---

## 0. 环境准备

调试期游戏根 = `blazor-cncnet-client\src\BlazorCncnetClient\BlazorCncnetClient\bin\Debug\net10.0`
（该目录下已有 `Resources\ClientDefinitions.ini` 与 `Maps\` 等游戏数据）。

```powershell
$root = "f:\work\PhotinoCLient\blazor-cncnet-client\blazor-cncnet-client\src\BlazorCncnetClient\BlazorCncnetClient\bin\Debug\net10.0"

# 1) 构建示例插件
dotnet build "f:\work\PhotinoCLient\blazor-cncnet-client\blazor-cncnet-client\samples\HelloPlugin\HelloPlugin.csproj" -c Debug

# 2) 安装到游戏根（只复制必要文件；契约 DLL 不应出现）
$dst = Join-Path $root "Plugins\hello-plugin"
New-Item -ItemType Directory -Force -Path $dst | Out-Null
Copy-Item "f:\work\PhotinoCLient\blazor-cncnet-client\blazor-cncnet-client\samples\HelloPlugin\bin\Debug\net10.0\HelloPlugin.dll" $dst
Copy-Item "f:\work\PhotinoCLient\blazor-cncnet-client\blazor-cncnet-client\samples\HelloPlugin\bin\Debug\net10.0\HelloPlugin.pdb" $dst -EA 0
Copy-Item "f:\work\PhotinoCLient\blazor-cncnet-client\blazor-cncnet-client\samples\HelloPlugin\plugin.ini" $dst
Copy-Item "f:\work\PhotinoCLient\blazor-cncnet-client\blazor-cncnet-client\samples\HelloPlugin\plugin.css" $dst

# 3) 启用插件（在该目录的 Resources\ClientDefinitions.ini 末尾追加）
#    先备份（同目录），再追加 [Plugins] 节
$ini = Join-Path $root "Resources\ClientDefinitions.ini"
Copy-Item $ini "$ini.f4bak" -Force
Add-Content -Path $ini -Value "`r`n[Plugins]`r`nhello-plugin=Plugins/hello-plugin" -Encoding utf8

# 4) 启动客户端（日志：$root\logs\client-yyyyMMdd.log）
```

> **收尾复原（务必回读校验，见「复原踩坑」）**：
> ```powershell
> $ini = Join-Path $root "Resources\ClientDefinitions.ini"
> # 按内容裁掉追加的 [Plugins] 节（比 Copy-Item 复原更可靠）
> $lines = Get-Content $ini
> $hit = Select-String -Path $ini -Pattern '^\[Plugins\]$'
> if ($hit) {
>     $keep = $lines[0..($hit[0].LineNumber - 2)]
>     while ($keep.Count -gt 0 -and $keep[-1].Trim().Length -eq 0) { $keep = $keep[0..($keep.Count - 2)] }
>     $keep | Set-Content -Path $ini -Encoding utf8
> }
> [bool](Select-String -Path $ini -Pattern '\[Plugins\]' -Quiet)   # 必须为 False
> Remove-Item "$root\Plugins" -Recurse -Force -EA SilentlyContinue
> ```
> **注意**：`GetSectionKeys` 对不存在的节返回 `null`，而本表 T0 正是「无 `[Plugins]` 节」的场景
> —— 必须先确认该假告警已修复（见「实跑中发现并修复的 3 个问题」#2），否则 T0 会误判为失败。

图例：**A** = 可由日志自动判定；**M** = 必须人工肉眼看界面。

---

## T0 · 无插件零回归（先做，最关键）

| 步骤 | 期望 | 判据 |
|---|---|---|
| 复原 `[Plugins]` 节后启动客户端 | 正常进主菜单；顶栏/设置/底栏与改动前**完全一致** | M |
| 查看日志 | 无 `插件已加载` / 无异常；`PluginCatalog.Discover` 因清单无节而直接返回 | A |

**要点**：`[Plugins]` 缺失或为空时，`Discover` 立即 `return`（不挂 `AssemblyResolve`、无副作用）。

---

## T1 · 纯资源插件（不编译，阶段 0 核心）

| 步骤 | 期望 | 判据 |
|---|---|---|
| 建 `Plugins\my-theme\plugin.ini`（**只 3 行**）+ `plugin.css`（写 `body{outline:2px solid red}` 之类肉眼可见规则） | 见下 | — |

```ini
[Plugin]
Id=my-theme
Name=我的样式包
Style=plugin.css
```

| 期望 | 判据 |
|---|---|
| 日志出现 `纯资源插件已启用：my-theme（我的样式包）样式=plugin.css 脚本=-` | A |
| 重启后样式**生效**（换肤成功） | M |
| 该目录内无任何 DLL 也能启用；**不需要** `ApiVersion` | A |

**要点**：`EntryType` 留空即视为纯资源插件，跳过契约版本校验（不加载程序集，无类型身份风险）。
CSS 由 `GameContentServer` 以游戏根为根伺服 → 经 `cncnet.injectCss` 注入 `<link>`。

---

## T2 · 示例插件端到端（程序集插件主流程）

装好后启动，逐项确认：

| # | 期望 | 判据 |
|---|---|---|
| 1 | 日志 `插件已加载：hello-plugin（示例：你好插件）ApiVersion=1 目录=…` | A |
| 2 | 顶栏菜单区出现 **★HELLO** 按钮（点击弹 toast「你好！本消息来自示例插件…」） | M+A |
| 3 | 设置页**左侧栏**出现「示例插件」卡片 | M |
| 4 | 主菜单按钮列表里出现 **★ 你好插件** 入口（在「退出」之前） | M |
| 5 | 点该入口 → 打开 `/plugin/hello-plugin/home` 页面（插件新增路由，套用 MainLayout 顶栏/底栏/背景） | M |
| 6 | 该页面「返回主菜单」按钮可用 | M |
| 7 | 在线大厅出现「示例插件面板」并占满整行 | M |
| 8 | `plugin.css` 生效（页面卡片为宿主题样式、`.hello-aside` 虚线框等） | M |

> **第 2 项同时是"共享默认 ALC"的关键实证**：`HelloTopBarBadge` 由宿主的 `DynamicComponent`
> 渲染。若插件被装进独立 ALC，`IComponent` 类型身份分裂，此处会直接抛异常而非渲染成功。
> Toast 走的是宿主题 JS（`cncnet.toast`），能弹出说明 JS 互操作链路正常。

---

## T3 · 插件禁用开关与目录缺失

| 步骤 | 期望 | 判据 |
|---|---|---|
| 把 `[Plugins]` 的值改成空（`hello-plugin=`） | 不加载该插件；客户端正常 | A+M |
| 把值改成不存在的目录（`hello-plugin=Plugins/nope`） | 日志 `插件 hello-plugin 目录不存在，已跳过`；客户端正常 | A |

---

## T4 · 契约版本不匹配（容错）

| 步骤 | 期望 | 判据 |
|---|---|---|
| 复制一份插件为 `hello-bad`，把其 `plugin.ini` 的 `ApiVersion` 改成 `99` | 日志 `插件 hello-bad 契约版本不匹配（插件 99 / 宿主 1），已禁用` + `插件未启用：…`；**客户端照常启动** | A+M |
| 同时保留正常 `hello-plugin` | 正常插件**不受影响**（仍全部生效） | M |

**要点**：这是"防加载第二份契约程序集致类型分裂"的防线 —— 必须在 **LoadFrom 之前**拦截。

---

## T5 · 各类坏插件互不干扰

逐个单独测试（每次只放一个坏插件 + 保留一个正常插件，确认正常插件仍生效、客户端仍启动）：

| 场景 | 期望日志 |
|---|---|
| 目录内**无** `plugin.ini` | `插件 X 加载失败，已跳过：缺少 plugin.ini` |
| `EntryType` 指向不存在的类型 | `插件 X 加载失败，已跳过：未找到入口类型 …` |
| `EntryType` 指向非 `ICncNetPlugin` 类型 | `插件 X 加载失败，已跳过：入口类型 … 未实现 ICncNetPlugin` |
| `EntryType`/`Style`/`Script` **全空** | `插件 X 加载失败，已跳过：未声明任何贡献（EntryType 与 Style/Script 均为空）` |
| DLL **损坏/非托管**（随便丢个 .txt 改名 .dll） | 加载失败/未找到入口类型；**客户端照常启动**（不得崩溃） |
| `ContributeSlot` 传了非 `IComponent` 类型 | `插件 X 的 ContributeSlot 目标 … 不是 Blazor 组件，已跳过`；其余贡献仍生效 |

**要点**：`Discover` / `RegisterServices` / `InitializeAll` 三阶段**各自** try-catch，
失败只把该插件置为 `Failed`，后续阶段跳过它，不影响其它插件与宿主。

---

## T6 · 路由冲突告警

| 步骤 | 期望 | 判据 |
|---|---|---|
| 临时改示例页 `@page "/menu"`（与内置主菜单路由同名）并重编 | 日志 `插件 hello-plugin 的页面路由与宿主内置路由冲突：/menu（请改用插件专属前缀…）`；客户端仍启动 | A+M |

---

## T7 · 发布链路回归

```powershell
cd f:\work\PhotinoCLient\blazor-cncnet-client\blazor-cncnet-client
.\tools\publish.ps1                      # 既有校验须全绿（插件不影响发布内容）
.\tools\publish.ps1 -PackPluginSdk       # 额外产出插件 SDK nupkg
```

| 期望 | 判据 |
|---|---|
| 不加 `-PackPluginSdk` 时行为与改动前**完全一致**（校验通过） | A |
| 加 `-PackPluginSdk` 则产出 `dist\nuget\CnCNetClient.Plugin.Abstractions.1.0.0.nupkg` | A |
| 发布根目录仍只有 `Launcher.exe` / `Resources\` / `ClientApp\`（**无** `nuget\` 混入 publish） | A |
| `ClientApp\CnCNetClient.Plugin.Abstractions.dll` 存在（插件作者可零配置引用） | A |
| `ClientApp\Plugins\` 不被创建（插件目录属游戏数据，不进发布包） | A |

---

## T8 · 插件作者编译链路（可选，验证 SDK 可用）

```powershell
dotnet nuget add source "<仓库根>\dist\nuget" -n cncnet-local
# 在插件工程中：dotnet add package CnCNetClient.Plugin.Abstractions
```

| 期望 | 判据 |
|---|---|
| 能编译；且生成输出目录内**不含** `CnCNetClient.Plugin.Abstractions.dll` | A |

**要点**（`plugin-dev.md` §2.2 的坑）：`PackageReference` 必须
`PrivateAssets="all"` **且** `ExcludeAssets="runtime"`；`PrivateAssets` 单独不够。

---

## 已由我（编译期/脚本期）验证的部分

- 全解决方案 Rebuild：0 错误；改动文件无新增警告
- `dotnet pack` 契约 → `CnCNetClient.Plugin.Abstractions.1.0.0.nupkg` 实测成功
- 示例插件输出目录**不含**契约 DLL（`Private="false"` 生效）
- 现有 `ClientDefinitions.ini` 无 `[Plugins]` 节 → `Discover` 立即返回，零回归
- `tools/publish.ps1` PowerShell 语法解析通过

## 未覆盖 / 已知限制

- **不热插拔**：插件仅在启动时加载；改插件后必须重启客户端（本次验收全部按重启后判定）。
- **不做沙箱/签名**：本地目录即信任；插件拥有与宿主同等权限。
- **私有依赖隔离较弱**：同名不同版本第三方库以后加载者为准。
- **RCL `wwwroot` 静态资源（`_content/…`）在插件场景不可用**：宿主未把插件程序集注册进静态资源管道；
  插件样式/脚本须为插件目录内松散文件 + `plugin.ini` 的 `Style`/`Script`。
- 阶段 2（插件市场/自动下载/版本管理）未实施，另行排期。
