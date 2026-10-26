# M4.2 语言切换验收清单

> 范围：spec **§6.8 多语言（双轨制）**。UI 文案走 Resx（运行时切 `CultureInfo.CurrentUICulture`），游戏内容/资源翻译保留原 INI `Translation`；键空间前缀隔离（`Client:UI:` → Resx，其余 → INI）；设置页「显示」分区提供语言下拉，切换即持久化 + 全 UI 即时刷新。
> 涉及文件：`I18N/UIStrings.cs`（新建，Resx 桥接）、`Extensions/StringExtensions.cs`（`L10N` 双轨路由）、`Resources/UI.resx`（neutral=en）+ `Resources/UI.zh-CN.resx`（中文卫星）、`Infrastructure/TranslationService.cs` + `ITranslationService.cs`（新建，scoped）、`Core/I18N/Translation.cs`（`GetDefaultUiLocaleCode`）、`Core/Settings/UserINISettings.cs`（`Translation` 默认值）、`Program.cs`（启动接线 + 注册）、`Components/Pages/Settings.razor`（语言下拉 + 页面本地化）、`Components/_Imports.razor`（命名空间）。
> 状态：实现完成，主工程构建 **0 错误**，无新增警告。
> 说明：`ITranslationService` 为 UI 层服务（scoped，与组件共享同一 WebView 电路）；`UIStrings`/`L10N` 在 Core 层提供键路由，不依赖 UI 层。

---

## 0. 前置准备（必读）

- 环境检测需指向已安装的 RA2/YR 游戏本体（游戏翻译目录 `Resources/Translations/<locale>` 用于枚举游戏内容翻译；缺失时自动回退默认值，不影响验收）。
- 验收辅助：语言切换后检查游戏根目录 `ClientSettings.ini` 的 `[Options] Translation` 是否即时更新。
- 建议先静态核对（1–2，单客户端切换），再做持久化（3）与重启回放（3.2）。

## 1. 设置页语言入口（单客户端静态）

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 1.1 | 设置中心 → 显示分区 | 顶部可见「语言 LANGUAGE · 即切即用」行 + 语言下拉框 | ☐ |
| 1.2 | 语言下拉列表 | 至少含「简体中文（zh-CN）」与「English（en）」；游戏翻译目录存在时叠加对应语言项 | ☐ |
| 1.3 | 默认语言 | 中文系统首次启动默认选中「简体中文」；非中文系统默认 en（跟随系统匹配） | ☐ |

## 2. 双轨切换（核心验收）

| # | 操作 | 预期结果 | 通过 |
|---|------|---------|------|
| 2.1 | 默认中文下打开设置中心 | 标题/返回/导航/分区标题为中文（`设置中心`/`返回`/`显示`/`音频`/…） | ☐ |
| 2.2 | 下拉切到 English | 页面标题/返回/导航/分区标题即时变英文（`Settings Center`/`Back`/`Display`/`Audio`/…），无需刷新 | ☐ |
| 2.3 | 切回简体中文 | 文案即时恢复中文 | ☐ |
| 2.4 | 游戏内容翻译 | 切换时重载 INI `Translation`（游戏目录 `<locale>/Translation.ini` + 主题翻译叠加）；有对应翻译目录时游戏内容文案随之切换 | ☐ |
| 2.5 | 缺失回退 | 无对应游戏翻译时 UI 仍显示默认值，不崩溃（`MissingManifestResourceException`/`CultureNotFoundException` 均兜底） | ☐ |

## 3. 持久化与重启（spec §6.10）

| # | 检查项 | 预期结果 | 通过 |
|---|--------|---------|------|
| 3.1 | 即改即存 | 切换语言后 `ClientSettings.ini` 的 `[Options] Translation` 立即更新为所选区域代码 | ☐ |
| 3.2 | 重启保持 | 重启客户端，首帧即按持久化语言渲染（无「先英文后切中文」闪烁；`Program.cs` 启动时按持久化值设 `CurrentUICulture`） | ☐ |
| 3.3 | 启动默认 | 首次启动（无持久化值）按 `Translation.GetDefaultUiLocaleCode`：中文系统 → zh-CN，非中文 → 跟随系统匹配 | ☐ |

## 4. 双轨隔离（spec §6.8）

| # | 检查项 | 预期结果 | 通过 |
|---|--------|---------|------|
| 4.1 | 键空间隔离 | `Client:UI:` 前缀键走 Resx（`UIStrings`），其余 `Client:*`/`INI:*` 键走 INI `Translation`；互不污染 | ☐ |
| 4.2 | L10N 路由 | `StringExtensions.L10N` 按前缀路由；UI 键缺失回退默认值（`fallback.L10N(UIStrings.KeyPrefix + key)`） | ☐ |
| 4.3 | 中英两套 | Resx neutral=en + zh-CN 卫星均存在且键一一对应（标题/返回/语言/导航/分区） | ☐ |

## 5. 回归（M4.1 / M3 不回归）

| # | 检查项 | 通过 |
|---|--------|------|
| 5.1 | 设置中心其余分区（显示主题/音频/游戏/网络/更新/隐私）即改即存不回归（对照 acceptance-m4.1） | ☐ |
| 5.2 | 登录/大厅/游戏流程不回归（对照 acceptance-m3.x） | ☐ |
| 5.3 | 主题切换（菜单/内页/红警）不回归 | ☐ |
| 5.4 | 构建 0 错误；本次改动无新增警告 | ☐ |

---

## 已知限制

- 目前仅设置中心页面文案做了 Resx 本地化（标题/返回/导航/分区/语言标签）；其余 UI 页面的硬编码中文文案（登录 Modal、大厅、主菜单等）尚未全部迁移到 Resx，后续里程碑逐步迁移。
- 游戏内容翻译依赖游戏目录存在 `Resources/Translations/<locale>/Translation.ini`；缺失时自动回退默认值。
- `TranslationService.Languages` 为启动时枚举一次（内置 UI 语言 + 游戏翻译目录）；新增翻译目录需重启生效。
