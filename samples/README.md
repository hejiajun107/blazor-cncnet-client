# 插件样例集

三个样例，代表插件的三档能力。**程序集插件必须编译，纯资源插件不需要** —— 别混淆。

| 目录 | 类型 | 需要编译 | 产物 |
|---|---|---|---|
| [`HelloPlugin/`](HelloPlugin) | **程序集插件**（追加槽位） | **是**（`dotnet build`） | `HelloPlugin.dll`（`plugin.ini` / `plugin.css` 随构建输出） |
| [`Cnc3Home/`](Cnc3Home) | **程序集插件**（替换主菜单 + three.js 地球） | **是**（`dotnet build`） | `Cnc3Home.dll` + `plugin.ini` / `plugin.css` / `cnc3-home.js` / `assets/` |
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

## 样例 1.5 · Cnc3Home（替换主菜单 + 3D 地球）

```powershell
dotnet build samples\Cnc3Home\Cnc3Home.csproj -c Debug
```

构建后把 `samples\Cnc3Home\bin\Debug\net10.0\` **整个目录**复制到 `Plugins\cnc3-home\`
（DLL / `plugin.ini` / CSS / JS / `assets\` 贴图缺一不可），并在 `ClientDefinitions.ini` 追加
`cnc3-home=Plugins/cnc3-home`。

演示内容：通过 `page.mainmenu` 槽位**替换整个主菜单主体**，左侧为 three.js 昼夜地球
（日照 / 夜景灯光 / 云层 / 大气辉光 / 轨道环），右侧为 C&C3 式斜切指令列与二级飞出菜单；
菜单动作全部复用宿主服务（登录弹窗 / 设置 / 附加功能 / 退出确认）。
WebGL 不可用或 `prefers-reduced-motion` 时自动降级为静态星空画面，菜单不受影响。

> three.js 采用 r149 UMD 版本（非 ESM，MIT 许可，文件头保留 SPDX 声明），贴图走插件本地
> `assets\textures\`；宿主 `GameContentServer` 已返回 `Access-Control-Allow-Origin: *`，
> WebGL 纹理读取不会触发跨源限制。
>
> 第三方素材：`assets\textures\earth_*.{jpg,png}` 来自 three.js 官方示例（NASA 公有领域地球影像），
> `assets\bg-city.jpg` 为 AI 生成背景，`assets\three.min.js` 为 MIT 许可。二次分发时请保留以上声明。

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
