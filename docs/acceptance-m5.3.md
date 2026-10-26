# M5.3 自更新发布链路（服务器端清单/归档）演练 · 验收

> 关联任务：tasks.md 5.3 / 5.3.1 / 5.3.2 / 5.3.3
> 日期：2026-09-08

## 一、验收目标

1. 验证「发布目录 → 服务器端清单/归档」工具链可用（`stage`）。
2. 验证客户端更新闭环端到端可用（版本检查 / 下载 / 解压 / 标识符校验），无需真实公网服务器（`verify`）。
3. 提供本地镜像伺服能力，供 GUI 人工验收（`serve`）。
4. `publish.ps1` 一键集成打包 + 服务器端清单生成（`-StageUpdate`）。

## 二、交付物

| 项 | 位置 | 说明 |
|---|---|---|
| 演练工具 | `tools/update-e2e/`（net10.0 控制台，引用 `CnCNetClient.Updater`） | `stage` / `verify` / `serve` 三子命令 |
| 发布集成 | `tools/publish.ps1` | 新增 `-StageUpdate` / `-ServerVersion`，publish 后自动调用 `stage` |
| 本地伺服包装 | `tools/serve-update.ps1` | 薄包装 `serve`（默认端口 8899） |
| 发布产物 | `dist/publish/` | 411 文件 / 135,789 KB（自包含 win-x64，M5.2 校验通过） |
| 服务器镜像 | `dist/updatestage/` | 407 文件 / 135,333 KB（404 个发布文件 + `version` + `preupdateexec` + `updateexec`） |

## 三、验收结果

### 3.1 `stage`：发布目录 → 服务器端清单/归档 ✅

- [x] 清单格式与原版 `Updater` 读取逻辑一致：
  - `[DTA] Version`（`-version` 必填）、`UpdaterVersion`、`ManualDownloadURL`（可选）。
  - `[FileVersions]`：`相对路径=MD5十进制标识符,大小KB`——标识符用 `MD5` 逐字节 `ToString()` 拼接，与 `Updater.GetUniqueIdForFile` 同算法。
  - `[ArchivedFiles]`（`-archive` 时）：`.lzma` 归档的标识符/体积，与 `[FileVersions]` 双轨并存。
- [x] 排除规则：`.pdb` 调试符号、`logs/` 目录不进入清单。
- [x] `preupdateexec` / `updateexec` 空脚本自动生成——**原版要求镜像根存在**，缺失时客户端下载 404 会中止更新（演练中发现并修复的问题，见 3.5）。
- [x] 实跑：`dist/publish`（411 文件）→ `dist/updatestage`（407 文件，含 3 个清单/脚本文件），`[FileVersions]` 共 404 项，体积 135,333 KB。

### 3.2 `verify`：闭环验证 ✅

进程内 `HttpListener` 模拟镜像（`http://127.0.0.1:8899/`），构造临时游戏目录 `dist/e2e-game`（含 `ClientSettings.ini`、`Resources/UpdaterConfig.ini` 指向本地镜像），驱动 `Updater` API 全流程。

- [x] **up-to-date 场景**：本地 version 置为服务器同版本（1.0.0）→ 版本检查 → 判 `UPTODATE`，`ServerGameVersion == 1.0.0`。**PASS（exit 0）**。
- [x] **outdated 场景**：本地 version 置 0.0.1 → 版本检查 → 判 `OUTDATED` 且 `UpdateSizeInKb > 0` → `StartUpdate` 下载 → 404 个文件全部下载 → 每个文件经 `GetUniqueIdForFile` 校验（日志逐文件 `... is intact.`）→ 全部落位 `Updater/` 临时目录 → 触发 `updateexec` → **PASS（exit 0）**。
- [x] 下载按 `mirrorURL + 文件名 + .lzma` 构造 URL 的协议路径已覆盖（`[ArchivedFiles]` 归档下载与解压逻辑复用原版 `Updater`，本次演练未开 `-archive`，留待真实发布时回归）。

### 3.3 `serve`：本地镜像伺服 ✅

- [x] `tools/serve-update.ps1 -Stage dist/updatestage -Port 8899` 即起本地静态服务器。
- [x] 路径穿越防护：规范化路径后校验 `StartsWith(_root)`，越界一律 404。
- [x] 用途：GUI 人工验收（设置中心 → 立即检查 → `UpdaterConfig.ini` 镜像列表指向本地 URL）。

### 3.4 `publish.ps1 -StageUpdate` 集成 ✅

- [x] `.\tools\publish.ps1 -StageUpdate -ServerVersion 1.0.0`：publish 校验通过后自动构建/复用 `update-e2e`，执行 `stage`，产出 `dist/updatestage`。
- [x] 未指定 `-ServerVersion` 时默认 `0.0.0`（占位，避免漏参）。
- [x] `dist/updatestage` 已纳入发布产物校验范围（`_publish-report.txt` 体积/文件数核对）。

### 3.5 演练中发现并修复的问题 ✅

- **preupdateexec 缺失导致更新中止**：原版 `Updater.ExecutePreUpdateScriptAsync` 会先下载镜像根的 `preupdateexec`，服务器目录缺该文件时 404 抛异常，更新流程中断。修复：`stage` 自动生成空脚本（INI 注释占位），根因写入 `stage` 注释与本文档，防止真实发布遗漏。

## 四、构建状态

- `tools/update-e2e`：Release 构建 0 错误。
- 主工程 Debug / Release：0 错误（12 个 NU1900 为 NuGet 漏洞数据缓存访问警告，与代码无关，既有）。

## 五、遗留 / 后续

- **SecondStageUpdater 替换重启链路**（下载完成后退出主进程、替换文件、重启）属 GUI 人工验收范围：`verify` 刻意止步于「文件下载 + 校验就位」，避免自动化流程中真替换 exe。验收路径：`serve-update.ps1` 起镜像 → 设置中心「更新」→ 立即检查 → 立即更新 → 观察重启后版本号变化（归 5.4 Windows 发布候选验收）。
- **`.lzma` 归档路径**（`-archive`）：协议与 `[ArchivedFiles]` 解析已按原版实现对齐，真实发布启用归档时需回归一次下载/解压闭环。
- **真实发布服务器**：演练全程本地伺服；正式部署时把 `dist/updatestage` 内容整体上传镜像根目录即可（`version` + 全部文件，保持相对路径）。
