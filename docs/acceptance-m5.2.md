# M5.2 打包脚本 + WebView2 运行时检测 · 验收

> 关联任务：tasks.md 5.2 / 5.2.1
> 日期：2026-09-08

## 一、验收目标

1. 提供可重复执行的打包脚本，一键产出可分发目录。
2. 客户端在缺失 WebView2 Runtime 的机器上给出明确引导（而非晦涩异常/白屏）。
3. 离线资源最终核对：发布产物 `wwwroot` 完整自包含，无 CDN / 网络加载依赖。

## 二、交付物

| 项 | 位置 | 说明 |
|---|---|---|
| WebView2 检测 | `src/.../Infrastructure/WebView2RuntimeGuard.cs` | 静态类：`Detect()` 探测 + `EnsureAvailable()` 引导 |
| 启动接线 | `src/.../Program.cs` | Serilog 初始化后、窗口创建前调用 `EnsureAvailable()`，缺失即退出 |
| 打包脚本 | `tools/publish.ps1` | 发布 + 校验 + 报告（UTF-8 BOM，Windows PowerShell 5.1 兼容） |
| 发布产物 | `dist/publish/`（gitignore 已排除） | 411 文件 / 135,789 KB，自包含 win-x64 |

## 三、验收结果

### 3.1 WebView2 运行时检测 ✅

- [x] 探测逻辑：扫描 Evergreen 默认安装目录（机器级 `ProgramFiles(x86)\Microsoft\EdgeWebView\Application`、`ProgramFiles\...` + 用户级 `LocalAppData\...`），取版本号最大的目录名（形如 `130.0.2849.56`）。
- [x] 幂等：`RuntimeVersion ??=` 缓存，重复调用不重复扫描。
- [x] 缺失引导：原生 `MessageBoxW`（user32 P/Invoke，未引入 WinForms 依赖）弹窗说明 + 「是否打开官方下载页」，确认后 `Process.Start` 打开 `https://go.microsoft.com/fwlink/p/?LinkId=2124703`（WebView2 Evergreen 独立安装器），随后进程退出。
- [x] 时机：先于 `appBuilder.Build()` / 窗口创建，避免 Photino 在 WebView2 缺失时抛出晦涩异常。
- [x] 平台守卫：非 Windows 直接放行（Linux 走 WebKit2GTK，不依赖 WebView2）。
- [x] 日志：检测到/未检测到均写 Serilog（控制台 + 文件）。

### 3.2 打包脚本 ✅

- [x] 参数化：`-Configuration`（默认 Release）、`-Runtime`（默认 win-x64）、`-SelfContained`（默认 true，`-SelfContained:$false` 切框架依赖）、`-OutputDir`（默认 dist）。
- [x] 工程定位：兼容 `<root>/src/...` 与 `<root>/blazor-cncnet-client/src/...` 两种布局。
- [x] 校验项：
  - 关键文件 6 项全 [OK]：`BlazorCncnetClient.exe`、`wwwroot/index.html`、`css/app.css`、`js/app.js`、`_framework/blazor.webview.js`、`fonts/Orbitron-400.woff2`。
  - 外部 URL 扫描：`wwwroot` 下 html/css/js 无外部 URL 引用（排除框架常量文本：localhost/127.0.0.1 回退、SVG 命名空间 `www.w3.org`、微软文档链接 `aka.ms`）→ 结果「无外部 URL 引用 — 完整自包含」。
- [x] 报告落盘：`dist/_publish-report.txt`（时间/配置/关键文件/扫描/体积/结论）。
- [x] 实跑通过：`dotnet publish` 0 错误，发布 411 文件 / 135,789 KB（自包含）。

### 3.3 离线资源最终核对 ✅

- [x] `wwwroot` 仅含：`index.html`、`css/app.css`、`js/app.js`、`fonts/`（8 个本地字体：NotoSansSC.ttf + Orbitron 4 个 woff2 + Rajdhani 3 个 ttf）。
- [x] `index.html` 无任何外链（无 CDN、无远程字体、无外网脚本）。
- [x] 发布产物扫描确认：除框架常量文本外无 `http(s)://` 引用 → 完整自包含（网络功能如 CnCNet/LAN 属运行时功能，非资源依赖）。

## 四、构建状态

- 主工程 Debug 构建：**0 错误**（12 个 NU1900 为 NuGet 漏洞数据缓存访问警告，与代码无关，既有）。
- Release 发布构建：0 错误。

## 五、遗留 / 后续

- WebView2 缺失引导的最终人工确认（弹窗文案/下载页打开）需在**无 WebView2 的干净环境**冒烟验证 —— 归 5.4 Windows 发布候选验收（本机已装 WebView2，无法直接演示缺失路径；检测逻辑与目录探测已覆盖 Evergreen 默认安装位置）。
- `publish.ps1` 仅覆盖 win-x64；Linux 发布与编译确认归 5.4。
