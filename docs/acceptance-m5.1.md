# M5.1 动效打磨验收清单

> 范围：spec §5.5 UI 动效规范（基础清单 + 科幻风专属清单）+ checklist D1.7/G2.1。
> 原则：交互反馈 ≤300ms、仅 transform/opacity、尊重 prefers-reduced-motion、时长/缓动统一 tokens。
> 涉及文件：`wwwroot/css/app.css`（token 化 + mbtn:active + lg-input.err）、`Components/Shared/LoginModal.razor`（错误描边）。
> 状态：**实现完成，待运行验收**（主工程构建 0 错误；本次改动 0 新增 CS 警告；动效为 CSS 层改动无逻辑风险）。

---

## 1. 基础动效清单（spec §5.5）

| # | 场景 | 要求 | 实现 | 通过 |
|---|------|------|------|------|
| 1.1 | hover / active / focus | 微缩放/阴影/描边过渡 ≤120-200ms ease-out | 按钮/卡片/条目 hover 均有；mbtn 补 `:active{translateX(-4px) scale(.97)}`；输入框 focus 青色描边发光 | ☐ |
| 1.2 | 页面/视图切换 | fade-slide 240ms ease-out | `.page-anim` `pageIn`（translateY(10px)→0 + 淡入），MainLayout 按路由 @key 重放 | ☐ |
| 1.3 | 列表增删（房间/玩家） | 入场滑入/淡入 200ms | `.item-in` stagger；房间/玩家走 Virtualize 批量更新（高频场景逐条动画刻意不做，M4.9 节流配合） | ☐ |
| 1.4 | 聊天消息 | 批量更新不卡顿 | 无逐条入场动画（性能优先，66ms 节流后整批 diff），自动滚动到底 | ☐ |
| 1.5 | 弹窗/右键菜单 | scale+fade 240ms ease-out-back；关闭淡出 | `pop-in`（msgbox/ctx-menu/pm-panel/cr-panel/gd-panel/cmd-hint）+ `pop-out` 关闭 120ms | ☐ |
| 1.6 | 侧栏/抽屉 | translateX 200ms | `slide-in-r` 定义就绪（当前无抽屉场景，留作 MOD 组件用） | ☐ |
| 1.7 | 连接状态 | 脉冲呼吸灯 | `blink`/`dotPulse`/`pulse`（1.1-1.8s）覆盖 HUD/大厅/加载页 | ☐ |
| 1.8 | 加载/进度 | spinner/雷达/渐隐 | `gj-spinner`、`gload-radar` 雷达扫描 2.4s、`shimmer` 地图占位 | ☐ |
| 1.9 | 通知/Toast | 滑入+自动消失 240ms | `.toast` 上滑淡入 + JS 自动消失 | ☐ |
| 1.10 | 表单错误 | shake + 描边变红 300ms | `.shake` 300ms + 新增 `.lg-input.err` 红描边（LoginModal 昵称错误时挂载） | ☐ |

## 2. 科幻风专属动效（theme-d）

| # | 场景 | 要求 | 通过 |
|---|------|------|------|
| 2.1 | 全息地球（主菜单） | canvas 点云旋转 + 轨道卫星 + 徽记辉光 + 雷达扫掠 5s | ☐ |
| 2.2 | 背景氛围 | 星粒上浮、极光 drift 22s、网格/扫描线/暗角静态叠加 | ☐ |
| 2.3 | 面板扫光线 | 顶边 1px 青线 sweep 4s（panel/env-check/msgbox） | ☐ |
| 2.4 | 按钮 hover | 左移 6px + 外发光 + 图标点亮 + `›` 箭头 + 背后扫光 | ☐ |
| 2.5 | 脉冲/呼吸 | 状态点/连接灯呼吸 | ☐ |
| 2.6 | HUD 读数 | 数值周期性跳动（原型 2.4s；计数过渡 WA 留作可选增强） | ☐ |

## 3. 规范一致性

| # | 检查项 | 通过 |
|---|--------|------|
| 3.1 | 交互时长均 ≤300ms（除装饰性循环动画） | ☐ |
| 3.2 | 动效仅 transform/opacity（无 layout 属性动画） | ☐ |
| 3.3 | 时长/缓动统一走 tokens（`--t-*`/`--ease-*`），无散落硬编码（`.mbtn::after` 扫光 .4s 为装饰性过渡，保留） | ☐ |
| 3.4 | `prefers-reduced-motion`：全局 `animation-duration:.001s!important` + 装饰性循环动画关闭 | ☐ |
| 3.5 | 主题切换过渡 `theme-fade`（仅 opacity 240ms）生效，红警主题/内页主题切换不闪烁 | ☐ |

## 4. 回归

| # | 检查项 | 通过 |
|---|--------|------|
| 4.1 | 主菜单/大厅/房间/设置/统计/战役各页 hover 与点击反馈正常（token 化后无行为变化） | ☐ |
| 4.2 | 登录失败：输入框红描边 + shake 提示；成功后描边恢复 | ☐ |
| 4.3 | 1280×720 与 1920×1080 下动效正常 | ☐ |
| 4.4 | 构建 0 错误；本次改动无新增 CS 警告 | ☐ |

---

## 已知限制

- 聊天消息无逐条入场动画（高频虚拟化场景，spec 允许批量更新优先性能）；`.pm-msg`（私聊）保留 `slideInUp` 入场。
- HUD 读数保持原型 2.4s 周期性跳动，Web Animations 计数过渡留作可选增强（数据为实时变化值，计数过渡收益低）。
- `slide-in-r` 侧栏动效已定义但当前无抽屉场景；MOD 组件（F2）落地时可直接复用。
