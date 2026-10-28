# 运行与构建说明

> 对应 tasks 5.5；面向测试人员与二次开发者。

## 环境要求

| 项目 | 要求 |
|---|---|
| 运行 | Windows 10/11 x64（内核越新越好）；**WebView2 运行时**（Win11 一般已内置；缺失时客户端启动会弹原生提示框引导安装） |
| 构建 | .NET 10 SDK（含 Razor 编译器）；Windows（当前主工程仅面向 win-x64 桌面） |
| 网络 | 在线大厅需可访问 cncnet.org；离线/LAN 可不联网 |

无需安装 .NET Runtime：正式包为**自包含**发布（框架随包带出）。

## 目录布局

```
src/BlazorCncnetClient/
├── CnCNetClient.Core/        # 核心逻辑（INI/配置/路径，net10.0 类库）
├── CnCNetClient.Tools/       # 工具集（Rampastring.Tools 去 XNA 化的独立工程）
├── CnCNetClient.Integration/ # 适配层（IDispatcher / ImageSharp 类型替身 / 路径抽象）
├── CnCNetClient.Domain/      # 领域逻辑（地图/游戏模式等）
├── CnCNetClient.Online/      # 在线逻辑（IRC 连接/大厅）
├── CnCNetClient.Updater/     # 自更新（版本比对/LZMA 解压/文件校验）
├── BlazorCncnetClient/       # 主工程（WinExe，net10.0，Razor + Photino(InfiniFrame)）
└── CnCNetClient.Tests/       # 单元测试（xunit，30 用例）
tools/
├── publish.ps1               # 打包脚本（Release/win-x64/自包含）
├── serve-update.ps1          # 自更新本地镜像伺服（薄包装 update-e2e serve）
└── update-e2e/               # 自更新发布链路工具（stage/verify/serve）
```

## 构建

### GitHub Actions 自动编译与下载

仓库的 [Build and package 工作流](../.github/workflows/build.yml) 在每次 `push` 时运行，
也可在 GitHub 的 Actions 页面手动触发。工作流使用 Windows runner 和 .NET 10 SDK，
先还原依赖，再以 Release 配置编译 `CnCNetClient.sln`，覆盖主客户端、依赖库、Launcher
及测试工程，然后调用 `tools/publish.ps1` 生成自包含的 Windows x64 发布包。
编译或发布校验失败会使工作流失败；此流程不执行单元测试。

下载位置：**Actions → Build and package → 对应运行记录 → Artifacts**，选择
`BlazorCncnetClient-win-x64.zip`。ZIP 保留 `Launcher.exe` 和 `ClientApp/` 布局，
包含 .NET 运行库、客户端资源与许可证，保留 30 天。仅发布目录进入压缩包，
构建日志、校验报告和本机配置不随包提供。工作流仅在 runner 的仓库目录内生成和上传产物。

### 调试构建

```powershell
dotnet build src\BlazorCncnetClient\BlazorCncnetClient\BlazorCncnetClient.csproj -c Debug
```

产物在 `bin\Debug\net10.0\`，直接运行 `BlazorCncnetClient.exe` 即可（调试构建需本机
已有 .NET 10 Desktop Runtime）。

### 单元测试

```powershell
dotnet test src\BlazorCncnetClient\CnCNetClient.Tests\CnCNetClient.Tests.csproj -c Release
```

覆盖：`CCIniFile` 继承合并、`ClientConfiguration` 加载、`MapLoader` 加载、
`Connection` 连接解析、`Updater` 版本比对（禁用并行以隔离静态状态）。

### 发布（推荐 `tools/publish.ps1`）

```powershell
# 默认：Release / win-x64 / 自包含 → dist/publish（可直接分发）
.\tools\publish.ps1

# 框架依赖（目标机需装 .NET 10 Desktop Runtime，体积更小）
.\tools\publish.ps1 -SelfContained:$false

# 自定义输出目录
.\tools\publish.ps1 -OutputDir out

# 打包 + 生成服务器端自更新清单/归档（M5.3，发布到镜像服用）
.\tools\publish.ps1 -StageUpdate -ServerVersion 0.3.0
```

脚本会生成 `dist/_publish-report.txt` 校验报告：关键文件（exe / wwwroot 资源 / 字体）、
**外部 URL 扫描**（保证完全离线自包含）、体积汇总；任一项失败即中止。

也可手写等价命令：

```powershell
dotnet publish src\BlazorCncnetClient\BlazorCncnetClient\BlazorCncnetClient.csproj `
  -c Release -r win-x64 --self-contained:true -o dist\publish
```

## 部署（测试/分发）

1. 将 `dist/publish` **整体**复制到游戏目录所在树（客户端通过查找 `Resources` 目录定位
   游戏根；典型：`游戏目录\Resources\` 与 `游戏目录\客户端\` 同级，或直接放在游戏目录）。
2. 保持原生 DLL（`Photino.Native.dll`、`WebView2Loader.dll`）与 exe 同目录——缺失会启动失败。
3. 双击 `BlazorCncnetClient.exe`。首次启动会做环境检测（未找到 Resources 时给出中文提示）。

> 杀软/SmartScreen 可能拦截未签名 exe：属正常现象，需放行或「解除锁定」。
> 启动闪一下即退/无响应时：查看 `logs/client-*.log`；异常会先落盘日志再弹原生提示框。

## 运行期文件

| 路径 | 说明 |
|---|---|
| `logs/client-yyyyMMdd.log` | 客户端日志（Serilog 按天滚动，保留 14 天自动清理） |
| `logs/connection.log` | 连接日志（超 2MB 归档为 `connection.old.log` 重建；超 14 天清理） |
| `settings.ini` | 用户设置（`[MultiPlayer] Theme` 等，原版语义，跨客户端一致） |
| `游戏目录/Resources/ClientDefinitions.ini` | 客户端定义（UI/主题/组件/闪屏扩展配置，见 MOD 定制说明） |

## 常见问题

- **启动闪一下即退 / 无反应**：查 `logs/`；确认 WebView2 已装；确认原生 DLL 在 exe 同目录；
  确认未被杀软隔离。
- **白屏/界面不显示**：确认 WebView2 运行时完整；日志是否有 JS/加载错误。
- **找不到游戏资源**：客户端须位于含 `Resources` 目录的目录树下。
