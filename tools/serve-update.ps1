# serve-update.ps1 — M5.3 本地更新镜像服务器（GUI 人工验收用）
#
# 用法：
#   .\tools\serve-update.ps1                          # 伺服 dist/updatestage → http://127.0.0.1:8899/
#   .\tools\serve-update.ps1 -Stage dist/updatestage -Port 8899
#
# 验收步骤（设置中心 → 更新 → 立即检查）：
#   1. 将本地镜像 URL 加入游戏目录 Resources\UpdaterConfig.ini 的 [DownloadMirrors]：
#        0=http://127.0.0.1:8899/,Local Mirror,Local
#   2. 启动客户端 → 设置中心「更新」→ 立即检查/立即更新，观察版本比对与下载进度。
#   3. 结束服务器：Ctrl+C。

param(
    [string]$Stage = "dist/updatestage",
    [int]$Port = 8899
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root "tools\update-e2e\bin\Release\net10.0\update-e2e.exe"

if (-not (Test-Path $tool)) {
    Write-Host "==> 构建 update-e2e 工具"
    & dotnet build (Join-Path $root "tools\update-e2e\UpdateE2E.csproj") -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "update-e2e 构建失败" }
}

$stageDir = Join-Path $root $Stage
if (-not (Test-Path $stageDir)) {
    throw "服务器目录不存在（先运行 tools\publish.ps1 -StageUpdate 或 update-e2e stage）：$stageDir"
}

& $tool serve -stage $stageDir -port $Port
