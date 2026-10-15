# publish.ps1 — 打包脚本（Windows x64 发布候选，Launcher + ClientApp\ 布局）
#
# 用法：
#   .\tools\publish.ps1                          # 默认：Release / win-x64 / 自包含 / dist
#   .\tools\publish.ps1 -SelfContained:$false    # 框架依赖（目标机需装 .NET 10 Desktop Runtime）
#   .\tools\publish.ps1 -OutputDir out           # 自定义输出目录（相对仓库根）
#   .\tools\publish.ps1 -PackPluginSdk           # 额外打包插件契约 nupkg → <OutputDir>/nuget（不进入 publish）
#
# 产物（安装根）：
#   <OutputDir>/publish\Launcher.exe     — NativeAOT 原生外壳启动器（留在根，无运行时依赖）
#   <OutputDir>/publish\ClientApp\       — 客户端自包含整体（含我的 exe + 运行库 + DLL + wwwroot）
#   <OutputDir>/_publish-report.txt      — 打包校验报告
#   <OutputDir>/nuget\*.nupkg            — （仅 -PackPluginSdk）插件 SDK，供插件作者 dotnet add package
#
# 目录定位说明：客户端 FindGamePath 会从 ClientApp\ 向上找 Resources 目录取父目录为游戏根
# （与官方 xna-cncnet-client 同款契约）。故将本输出整体放入游戏目录（Resources 所在树），
# 根目录下的 Launcher.exe 即入口，游戏文件/资源在安装根、程序在 ClientApp\、用户数据在 GamePath\Client\。

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true,
    [string]$OutputDir = "dist",
    [switch]$StageUpdate,
    [string]$ServerVersion = "",
    [switch]$PackPluginSdk
)

$ErrorActionPreference = "Stop"

# ── 路径 ─────────────────────────────────────────────
$root     = Split-Path -Parent $PSScriptRoot      # 仓库根（含 src）
# 兼容两种布局：<root>/src/... 或 <root>/blazor-cncnet-client/src/...
$candidates = @(
    (Join-Path $root "src\BlazorCncnetClient\BlazorCncnetClient\BlazorCncnetClient.csproj"),
    (Join-Path $root "blazor-cncnet-client\src\BlazorCncnetClient\BlazorCncnetClient\BlazorCncnetClient.csproj")
)
$csproj  = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csproj) { throw "找不到主工程（已探测：$($candidates -join ' / ')）" }

# Launcher 工程与主工程同层（src\BlazorCncnetClient\CnCNetClient.Launcher）
$launcherProj = Join-Path (Split-Path -Parent (Split-Path -Parent $csproj)) "CnCNetClient.Launcher\CnCNetClient.Launcher.csproj"
if (-not (Test-Path $launcherProj)) { throw "找不到 Launcher 工程：$launcherProj" }

$outRoot    = Join-Path $root $OutputDir
$publish    = Join-Path $outRoot "publish"          # 安装根
$clientApp  = Join-Path $publish "ClientApp"        # 客户端自包含整体
$tmpClient  = Join-Path $outRoot "_tmp-client"      # 客户端发布暂存（随后整体移入 ClientApp）

# ── 0) 清空上一轮安装根（避免残留旧布局文件混入校验）────────
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

# ── 1) 发布客户端到暂存目录 ─────────────────────────
Write-Host "==> dotnet publish 客户端 (配置=$Configuration 运行时=$Runtime 自包含=$SelfContained)"
if (Test-Path $tmpClient) { Remove-Item -Recurse -Force $tmpClient }
& dotnet publish $csproj -c $Configuration -r $Runtime "--self-contained:$SelfContained" -o $tmpClient --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 客户端失败（exit=$LASTEXITCODE）" }

# ── 2) 整体移入 ClientApp\（安装根只留 Launcher.exe）────────
if (Test-Path $clientApp) { Remove-Item -Recurse -Force $clientApp }
New-Item -ItemType Directory -Path $clientApp -Force | Out-Null
Get-ChildItem $tmpClient | ForEach-Object { Move-Item $_.FullName -Destination $clientApp -Force }
Remove-Item $tmpClient -Force

# ── 3) 发布 Launcher（NativeAOT → 安装根）────────────
Write-Host "==> dotnet publish Launcher (NativeAOT → 安装根)"
& dotnet publish $launcherProj -c $Configuration -r $Runtime -o $publish -p:PublishAot=true --nologo
if ($LASTEXITCODE -ne 0) { throw "Launcher 发布失败（exit=$LASTEXITCODE）" }

# 根目录只留 exe；去掉 pdb
$launcherPdb = Join-Path $publish "Launcher.pdb"
if (Test-Path $launcherPdb) { Remove-Item $launcherPdb -Force }

# ── 校验报告 ─────────────────────────────────────────
$report = New-Object System.Collections.Generic.List[string]
$report.Add("CNCNet Client 打包校验报告 (Launcher + ClientApp 布局)")
$report.Add("时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$report.Add("配置: $Configuration / $Runtime / 自包含=$SelfContained")
$report.Add("安装根: $publish")
$report.Add("客户端: $clientApp")
$report.Add("")

$fail = 0
$wwwroot = Join-Path $clientApp "wwwroot"

# 1) 关键文件
$report.Add("── 关键文件 ──")
$required = @(
    (Join-Path $publish "Launcher.exe"),
    (Join-Path $clientApp "BlazorCncnetClient.exe"),
    (Join-Path $wwwroot "index.html"),
    (Join-Path $wwwroot "css\app.css"),
    (Join-Path $wwwroot "js\app.js"),
    (Join-Path $wwwroot "_framework\blazor.webview.js"),
    (Join-Path $wwwroot "fonts\Orbitron-400.woff2")
)
foreach ($f in $required) {
    $ok = Test-Path $f
    if (-not $ok) { $fail++ }
    $resolved = Resolve-Path $f -ErrorAction SilentlyContinue
    $display = if ($resolved) { $resolved.Path } else { $f }
    $report.Add(("{0}  {1}" -f ($(if ($ok) { "[OK] " } else { "[缺失]" })), $display))
}
$report.Add("")

# 2) 仅根目录应只有 Launcher.exe（检查有没有多余顶层文件，排除 Launcher.pdb）
$report.Add("── 安装根顶层（应仅 Launcher.exe）──")
$topFiles = Get-ChildItem $publish -File | Where-Object { $_.Name -ne "Launcher.pdb" }
foreach ($tf in $topFiles) {
    if ($tf.Name -ne "Launcher.exe") { $fail++ }
}
if ($topFiles.Count -eq 0) { $fail++ }
$topFiles | ForEach-Object { $report.Add(("{0}" -f $_.Name)) }
$report.Add("")

# 3) 离线资源：扫描 ClientApp\wwwroot 下外部 URL（CDN / 网络字体等）。
#    排除框架常量文本：localhost/127.0.0.1 回退、SVG 命名空间（www.w3.org）、微软文档链接（aka.ms）。
#    排除第三方打包库 pixi.min.js：其内部含 opensource 许可证头 + createObjectURL 内嵌 worker 的
#    http 字符串，均为打包数据、非运行时外部网络依赖，属误报。
$report.Add("── 离线资源（wwwroot 外部 URL 扫描）──")
$extRefs = 0
Get-ChildItem $wwwroot -Recurse -Include *.html,*.css,*.js -File | Where-Object { $_.Name -ne "pixi.min.js" } | ForEach-Object {
    $hits = Select-String -Path $_.FullName -Pattern 'https?://(?!localhost|127\.0\.0\.1|www\.w3\.org|aka\.ms|www\.opensource\.org)' -CaseSensitive:$false -AllMatches |
        ForEach-Object { $_.Matches.Count } | Measure-Object -Sum
    if ($hits.Sum -gt 0) {
        $extRefs += $hits.Sum
        $rel = $_.FullName.Substring($wwwroot.Length + 1)
        $report.Add(("外部引用: {0} 命中 {1} 处" -f $rel, $hits.Sum))
    }
}
if ($extRefs -eq 0) { $report.Add("无外部 URL 引用 — 完整自包含") }
else { $fail++ }
$report.Add("")

# 4) 体积
$sizeKB = [Math]::Round(((Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum) / 1KB, 0)
$fileCount = (Get-ChildItem $publish -Recurse -File).Count
$report.Add("── 体积 ──")
$report.Add("文件数: $fileCount  总大小: $sizeKB KB")
$report.Add("")
$report.Add(($(if ($fail -eq 0) { "校验通过：无缺失文件、无外部引用、安装根干净。" } else { "校验失败：发现 $fail 项问题。" })))

$reportPath = Join-Path $outRoot "_publish-report.txt"
$report | Set-Content -Path $reportPath -Encoding UTF8
$report | ForEach-Object { Write-Host $_ }

if ($fail -ne 0) { throw "打包校验未通过，详见 $reportPath" }

# ── 插件 SDK（F4，可选）：打包契约 nupkg，供插件作者 dotnet add package 引用。
#    输出到 <OutputDir>/nuget（与 publish 平级 → 不进入发布校验范围）。────────
if ($PackPluginSdk) {
    $sdkProj = Join-Path (Split-Path -Parent (Split-Path -Parent $csproj)) "CnCNetClient.Plugin.Abstractions\CnCNetClient.Plugin.Abstractions.csproj"
    if (-not (Test-Path $sdkProj)) { throw "找不到插件契约工程：$sdkProj" }
    $nugetOut = Join-Path $outRoot "nuget"
    Write-Host ""
    Write-Host "==> dotnet pack 插件 SDK（$sdkProj → $nugetOut）"
    & dotnet pack $sdkProj -c $Configuration -o $nugetOut --nologo
    if ($LASTEXITCODE -ne 0) { throw "插件 SDK 打包失败（exit=$LASTEXITCODE）" }
    Write-Host "插件 SDK（nupkg）: $nugetOut"
}

Write-Host ""
Write-Host "打包完成：$publish"
Write-Host "提示：将 publish 目录整体放入游戏目录（Resources 所在目录树），双击 Launcher.exe 启动即可；WebView2 缺失时客户端会引导安装。"

# M5.3 自更新发布链路：生成服务器端清单/归档（update-e2e stage）
if ($StageUpdate) {
    $tool = Join-Path $root "tools\update-e2e\bin\Release\net10.0\update-e2e.exe"
    if (-not (Test-Path $tool)) {
        Write-Host "==> 构建 update-e2e 工具"
        & dotnet build (Join-Path $root "tools\update-e2e\UpdateE2E.csproj") -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "update-e2e 构建失败" }
    }
    Write-Host ""
    Write-Host "==> 生成服务器端清单/归档（update-e2e stage）"
    $stageArgs = @("stage", "-publish", $publish, "-out", (Join-Path $outRoot "updatestage"))
    if ($ServerVersion) { $stageArgs += @("-version", $ServerVersion) }
    else { $stageArgs += @("-version", "0.0.0") }
    & $tool @stageArgs
    if ($LASTEXITCODE -ne 0) { throw "服务器端清单生成失败" }
}