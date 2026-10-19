# 第三方组件声明（THIRD-PARTY NOTICES）

本文件汇总 Blazor CNCNet Client 分发包中包含/引用的第三方组件及其许可证，
用于满足各开源许可证的署名与随附声明要求。本项目自身以 GPLv3+ 授权，
见 [LICENSE](LICENSE) 与 [NOTICE.md](NOTICE.md)。

许可证信息核对自本地 NuGet 缓存（`.nuget-packages/<id>/<ver>/*.nuspec`
的 `license` 元数据与包内 LICENSE 文件），核对日期：2026-10-16。

---

## 1. 运行时 NuGet 依赖

### Apache License 2.0

| 包 | 版本 |
|---|---|
| Serilog | 4.4.0 |
| Serilog.AspNetCore | 5.0.0 |
| Serilog.Extensions.Hosting | 10.0.0 |
| Serilog.Sinks.Async | 2.1.0 |
| Serilog.Sinks.Console | 5.0.0 |
| Serilog.Sinks.File | 7.0.0 |
| Serilog.Extensions.Logging / Serilog.Settings.Configuration / Serilog.Formatting.Compact / Serilog.Sinks.Debug | 传递依赖 |
| InfiniLore.InfiniFrame / .Blazor / .BlazorWebView / .NativeBridge / .Js / .Shared | 0.60.0（nuspec 为 file 型声明，包内 `LICENSE` 实为 Apache-2.0 全文） |
| FluentValidation | 传递依赖 |
| xunit 全家（xunit / xunit.assert / xunit.core / xunit.abstractions / xunit.analyzers / xunit.extensibility.* / xunit.runner.visualstudio） | 2.9.2 等（仅测试） |

Apache-2.0 要求保留一份许可证副本与声明。全文见
<https://www.apache.org/licenses/LICENSE-2.0>。

### MIT License

| 包 | 版本 | 版权 |
|---|---|---|
| Newtonsoft.Json | 13.0.4 | Copyright (c) 2007 James Newton-King |
| Newtonsoft.Json.Bson | 传递依赖 | James Newton-King |
| DiscordRichPresence | 1.2.1.24 | — |
| lzo.net | 0.0.6 | <https://github.com/zivillian/lzo.net> |
| JetBrains.Annotations | 传递依赖 | JetBrains s.r.o. |
| Microsoft.AspNetCore.* / Microsoft.Extensions.* / Microsoft.JSInterop | 共享框架 | Microsoft |
| Microsoft.NET.Test.Sdk / Microsoft.TestPlatform.* | 17.11.1（仅测试） | Microsoft |

### Six Labors Split License 1.0

| 包 | 版本 |
|---|---|
| SixLabors.ImageSharp | 3.1.11 |

该许可证对**非商业用途**为 MIT 条款、对商业用途另有条件。若本项目用于商业分发，
需自行评估是否满足其商业条款。全文随包分发于 `sixlabors.imagesharp/3.1.11/LICENSE`，
亦见 <https://github.com/SixLabors/LICENSE>。

### Mozilla Public License 2.0（文件级 copyleft）

| 包 | 版本 |
|---|---|
| OpenMcdf | 3.1.4 |

MPL-2.0 是**文件级**弱 copyleft：修改 OpenMcdf 源文件需以 MPL-2.0 开源，
但与本项目其余代码组合分发不传染。本项目未修改其源码，仅按二进制形式引用。
全文见 <https://www.mozilla.org/en-US/MPL/2.0/>。

### 三重许可：MPL-1.1 / GPL-2.0 / LGPL-2.1（自行选择其一）

| 包 | 版本 |
|---|---|
| Ude.NetStandard | 1.2.0 |

字符编码检测库（源自 Jon Dahl 的 Ude）。nuspec 中 `license` 指向包内 `COPYING`
（23 字节存根 "See license directory"），真实文本在上游仓库 `license/` 目录。
本项目**按 LGPL-2.1（或其后续版本）选项**引用该二进制。
上游：<https://github.com/rmtew/ude> 。
> 注：LGPL-2.1 与本项目的 GPLv3 组合分发是兼容的（GPLv3 吸收 LGPL-2.1 条款）。

### .NET Library EULA（Microsoft 专有条款）

| 包 | 版本 |
|---|---|
| Microsoft.AspNet.WebApi.Client | 6.0.0 |

用于 Updater 的 JSON 媒体格式化。许可证全文随包分发于
`microsoft.aspnet.webapi.client/6.0.0/NET_Library_EULA_ENU.txt`，
亦见 <https://www.nuget.org/packages/Microsoft.AspNet.WebApi.Client/6.0.0>。

---

## 2. 打包进 `wwwroot` 的第三方 JavaScript

| 组件 | 版本 | 许可证 | 位置 |
|---|---|---|---|
| PixiJS | 8.20.1 | MIT | `ClientApp/wwwroot/lib/pixi.min.js`（全息地球视口渲染） |

许可证头保留在该文件内：

```
/*!
 * PixiJS - v8.20.1
 * PixiJS is licensed under the MIT License.
 * http://www.opensource.org/licenses/mit-license
 */
```

MIT 全文：<https://opensource.org/license/mit> 。

---

## 3. 字体（SIL Open Font License 1.1）

| 字体 | 版权 |
|---|---|
| Orbitron 400/500/700/900（woff2） | Copyright 2018 The Orbitron Project Authors (https://github.com/theleagueof/orbitron), with Reserved Font Name: "Orbitron". |
| Rajdhani Regular/Medium/Bold（ttf） | Copyright (c) 2014 Indian Type Foundry (info@indiantypefoundry.com) |
| Noto Sans SC（ttf） | (c) 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source'. |

版权行取自各字体文件 name 表（ID 0）。OFL-1.1 全文随字体分发于
`ClientApp/wwwroot/fonts/OFL.txt`，亦见 <https://openfontlicense.org/>。

---

## 4. 发布产物中的原生 DLL

| 文件 | 来源 | 许可证 |
|---|---|---|
| `InfiniFrame.Native.dll` / `InfiniFrame.NativeBridge.dll` | InfiniLore.InfiniFrame 0.60.0 | 见第 1 节（包内 LICENSE） |
| `Microsoft.DiaSymReader.Native.amd64.dll` | Microsoft | MIT |
| `System.IO.Compression.Native.dll` | .NET 运行时（dotnet/runtime） | MIT |
| `WebView2Loader.dll` | Microsoft Edge WebView2 | 视同 .NET Library / MIT（随 .NET SDK 分发） |

自包含发布的其余 `System.*` / `Microsoft.*` 运行时程序集均来自 .NET Runtime，
许可证为 MIT，见 <https://github.com/dotnet/runtime> 。

---

## 5. 上游项目

| 项目 | 许可证 | 说明 |
|---|---|---|
| [CnCNet/xna-cncnet-client](https://github.com/CnCNet/xna-cncnet-client) | GPLv3+ | Copyright (C) 2013-2025 CnCNet, Rampastring。本项目 Domain/Online/Core/Tools/Integration/Updater 等模块的衍生来源，详见 [NOTICE.md](NOTICE.md) |

---

## 维护说明

新增/升级依赖后，请重新核对本文件：

```powershell
$p = "..\.nuget-packages"   # 本地 NuGet 缓存（仓库上级目录）
Get-ChildItem $p -Directory | ForEach-Object {
  Get-ChildItem $_.FullName -Recurse -Filter *.nuspec -EA 0 | ForEach-Object {
    $md = ([xml](Get-Content $_.FullName -Raw)).package.metadata
    "{0} | {1}" -f $md.id, $md.license
  }
} | Sort-Object -Unique
```
