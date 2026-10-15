# 插件样例集

两个样例，代表插件的两档能力。**一类必须编译，一类不需要** —— 别混淆。

| 目录 | 类型 | 需要编译 | 产物 |
|---|---|---|---|
| [`HelloPlugin/`](HelloPlugin) | **程序集插件** | **是**（`dotnet build`） | `HelloPlugin.dll`（`plugin.ini` / `plugin.css` 随构建输出） |
| [`ThemeDemo/`](ThemeDemo) | **纯资源插件** | **否** | 无（只有 `plugin.ini` + `plugin.css` 两个文本文件） |

## 通用部署方式

1. 在游戏根目录建 `Plugins\<目录名>\`，把插件文件放进去；
2. 在游戏根 `Resources\ClientDefinitions.ini` 末尾追加：

```ini
[Plugins]
<key>=Plugins/<目录名>
```

`<key>` 是插件标识（用于日志与去重），建议与目录名、`plugin.ini` 的 `Id` 三者保持一致。
空值（`<key>=`）表示该插件不启用。重启客户端生效。

启动日志里应出现 `插件已加载：…`（程序集插件）或 `纯资源插件已启用：…`（纯资源插件）。

## 样例 1 · HelloPlugin（程序集插件）

```powershell
dotnet build samples\HelloPlugin\HelloPlugin.csproj -c Debug
```

构建后从 `samples\HelloPlugin\bin\Debug\net10.0\` 取
`HelloPlugin.dll` + `plugin.ini` + `plugin.css`，部署到 `Plugins\hello-plugin\`。

演示内容：4 个**追加槽位**（顶栏 `layout.topbar` / 主菜单 `menu.entry` / 设置侧栏
`page.settings.aside` / 大厅 `page.lobby.aside`）+ 1 个**自定义页面路由** + 1 个**主菜单项**
+ 1 个**自有 DI 服务** + 插件自带 CSS。

## 样例 2 · ThemeDemo（纯资源插件）

**无需编译** —— 直接把 `samples\ThemeDemo\` 整个目录复制到 `Plugins\theme-demo\` 即可：

```powershell
$root = "<游戏根目录>"
New-Item -ItemType Directory -Force "$root\Plugins\theme-demo" | Out-Null
Copy-Item "samples\ThemeDemo\*" "$root\Plugins\theme-demo\" -Force
```

演示内容：换肤（覆盖 design tokens，底色变紫 / 主色变金）+ 一个可见的注入标记。

两个要点（详见 [plugin-dev.md](../docs/plugin-dev.md) §2.6）：
- 覆盖 tokens 必须**同时覆盖** `:root` / `:root[data-theme="inner"]` / `:root[data-theme="ra"]`，
  否则内页（设置 / 大厅）不生效；
- `body::after` 那段只是"注入成功"的可见标记，正式皮肤里请删掉。

## 关于构建

- 样例**不在主解决方案里**（`CnCNetClient.sln` / `BlazorCncnetClient.slnx` 均未引用），
  所以**不会随 `tools/publish.ps1` 进入发布包**，也不参与主项目构建；
  程序集样例需按上面的命令单独构建。
- 程序集样例通过 `ProjectReference`（`Private=false`）引用契约程序集，**只能在仓库内编译**。
  模拟"外部插件作者"的处境需要改用 `PackageReference` 指向 `dist/nuget/` 产出的 nupkg
  （由 `tools/publish.ps1 -PackPluginSdk` 生成）。
