# Blazor CNCNet Client

基于 [CnCNet/xna-cncnet-client](https://github.com/CnCNet/xna-cncnet-client) 重构的
《红色警戒 2 / 尤里的复仇》及其 MOD 联网客户端。核心逻辑（在线大厅、房间、游戏启动、
自更新、设置）沿用官方客户端，联网协议与行为一致；UI 层改为 Blazor + WebView2，
并保留运行时主题、组件替换与插件扩展能力。

- 许可证：GPLv3+，详见 [LICENSE](LICENSE) 与 [NOTICE.md](NOTICE.md)
- 第三方依赖与素材许可：[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
- 项目定位与测试说明：[docs/client-intro.md](docs/client-intro.md)

## 环境要求

| 用途 | 要求 |
|---|---|
| 运行 | Windows 10/11 x64 + WebView2 运行时（Win11 一般已内置） |
| 构建 | .NET 10 SDK（仅面向 win-x64 桌面） |
| 网络 | 在线大厅需访问 cncnet.org；离线 / LAN 无需联网 |

## 快速开始

```powershell
# 调试构建
dotnet build src\BlazorCncnetClient\BlazorCncnetClient\BlazorCncnetClient.csproj -c Debug

# 运行（调试构建需本机 .NET 10 Desktop Runtime）
src\BlazorCncnetClient\BlazorCncnetClient\bin\Debug\net10.0\BlazorCncnetClient.exe

# 单元测试
dotnet test src\BlazorCncnetClient\CnCNetClient.Tests\CnCNetClient.Tests.csproj -c Release

# 发布（Release / win-x64 / 自包含 → dist/publish）
.\tools\publish.ps1
```

完整的目录布局、发布参数与自更新打包流程见 [docs/build-guide.md](docs/build-guide.md)。

## 插件与 UI 定制

所有定制都通过游戏根目录的 `Resources/ClientDefinitions.ini` 声明，不改代码：

| 能力 | 节 | 文档 |
|---|---|---|
| 主题目录整体换肤 | `[Themes]` | [docs/mod-customization.md](docs/mod-customization.md) |
| 关键组件替换、UI 参数 | `[Components]` / `[UI]` | 同上 |
| 外部插件（换肤 / 加组件 / 加页面 / 替换首页） | `[Plugins]` | 同上 §F4、[docs/plugin-dev.md](docs/plugin-dev.md) |

最小启用方式：

```ini
; 游戏根目录 Resources\ClientDefinitions.ini
[Plugins]
my-plugin=Plugins/my-plugin   ; 空值 = 不启用；改完需重启客户端
```

插件分为**纯资源**（只放 `plugin.ini` + `plugin.css`，免编译换肤）与**程序集插件**
（RCL，可追加槽位、替换主菜单主体、提供页面与服务）两档，契约版本 `ApiVersion=1`。

## 示例

| 目录 | 说明 | 需编译 |
|---|---|---|
| [samples/HelloPlugin](samples/HelloPlugin) | 程序集插件：4 个追加槽位 + 独立页面 + 菜单项 + 自有 DI | 是 |
| [samples/Cnc3Home](samples/Cnc3Home) | 程序集插件：替换主菜单主体，three.js 昼夜地球（全本地资源） | 是 |
| [samples/ThemeDemo](samples/ThemeDemo) | 纯资源插件：免编译换肤 | 否 |

示例都不在解决方案内，构建主工程不会自动带出；安装与注册步骤见
[samples/README.md](samples/README.md)。

## 文档索引

- [docs/client-intro.md](docs/client-intro.md) — 项目介绍与测试说明
- [docs/build-guide.md](docs/build-guide.md) — 构建、测试、发布、自更新打包
- [docs/mod-customization.md](docs/mod-customization.md) — 使用者：主题 / 组件 / 插件配置
- [docs/plugin-dev.md](docs/plugin-dev.md) — 开发者：插件契约、槽位、脚本与内容资源
- [docs/splash-screen.md](docs/splash-screen.md) — 启动闪屏配置
- [docs/migration-guide.md](docs/migration-guide.md) — 从原客户端的迁移说明
- [docs/plugin-mechanism.md](docs/plugin-mechanism.md) / [docs/plugin-plan.md](docs/plugin-plan.md) — 插件机制调研与实施计划
- [docs/spec.md](docs/spec.md) — 总体规格

## 目录结构

```
src/BlazorCncnetClient/   # 解决方案与各工程（Core / Online / Domain / Updater / 主工程 / 测试）
samples/                  # 插件示例（不在解决方案内）
docs/                     # 设计与使用文档
tools/                    # 发布与自更新工具
```

## 致谢

- CnCNet / Rampastring：上游客户端与网络协议
- Westwood Studios / EA：Command & Conquer 系列游戏本体
