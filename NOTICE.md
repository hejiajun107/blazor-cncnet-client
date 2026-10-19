# 声明（NOTICE）

## 本项目的许可证

本项目以 **GNU General Public License version 3**
（GNU 通用公共许可证第 3 版，GPLv3+）授权，全文见本目录 [LICENSE](LICENSE)。

```
Blazor CNCNet Client
Copyright (C) 2026 Blazor CNCNet Client contributors
```

## 上游致谢与衍生声明

本项目的 Domain / Online / Core / Tools / Integration / Updater 等模块
**基于 [CnCNet/xna-cncnet-client](https://github.com/CnCNet/xna-cncnet-client)
的代码修改、移植与去 XNA 化而来**，属其衍生作品，因此整体沿用其 GPLv3+ 许可证：

```
CnCNet XNA Client
Copyright (C) 2013-2025 CnCNet, Rampastring
https://github.com/CnCNet/xna-cncnet-client
License: GNU General Public License version 3 or later (GPLv3+)
```

依据 GPLv3 §5 的修改声明：本仓库对上游代码进行了大量修改（移除 XNA/MonoGame 依赖、
改写为 Blazor + WebView2 桌面壳、重构 UI 层、新增插件机制等），
**修改过的文件已在文件头或版本历史中标注**；完整变更历史见 git log。

上游部分原始文件的版权声明保留在各源文件头部。若个别文件未标注，
其来源仍以上述上游仓库为准。

## 字体（OFL-1.1）

`src/BlazorCncnetClient/BlazorCncnetClient/wwwroot/fonts/` 下的 Orbitron、Rajdhani、Noto Sans SC 字体以
**SIL Open Font License 1.1** 授权，版权行与许可证全文见
[wwwroot/fonts/OFL.txt](src/BlazorCncnetClient/BlazorCncnetClient/wwwroot/fonts/OFL.txt)。

## 第三方组件

本项目依赖的全部 NuGet 包、打包进 `wwwroot` 的第三方 JS 库以及
发布产物中的原生 DLL，其许可证汇总见
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

> 以上三个文件（`LICENSE`、`NOTICE.md`、`THIRD-PARTY-NOTICES.md`）
> 会由 `tools/publish.ps1` 一并复制进发布目录 `dist/publish/`。
