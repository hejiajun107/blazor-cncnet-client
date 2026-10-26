# 启动闪屏（LoadingScreen）配置说明

> 对应 tasks 2.3；实现：`Components/Shared/SplashScreen.razor`（UI 覆盖层）+
> `Infrastructure/LoadingScreenService.cs`（配置解析）+
> `Infrastructure/GameContentServer.cs`（游戏目录本地伺服）。

启动闪屏是客户端启动瞬间的全屏过渡画面，在环境检测页渲染前展示，
展示完毕后淡出进入主界面。

## 配置位置

`游戏根目录/Resources/ClientDefinitions.ini`，新增节 `[LoadingScreen]`。
放 `ClientDefinitions.ini` 而非单独 ini，原因：与规划 F3（`ClientDefinitions.ini`
扩展 UI 配置）一致，MOD 作者一处维护 UI 外观；未来 F1 主题目录机制可整体覆盖该节。

## 配置项

```ini
[LoadingScreen]
ShowSplash=true            ; 是否启用启动闪屏（默认 true）
SplashImage=Resources/splash.png   ; 背景图（相对游戏根目录，默认空）
SplashVideo=Resources/splash.mp4   ; 背景视频（可选；存在时优先于图片）
SplashDuration=2.0         ; 展示秒数；<=0 = 初始化完成即关闭（最短约 0.5s）
ShowProgress=true          ; 是否显示初始化进度条（动画式，无真实百分比）
ShowVersion=true           ; 是否显示版本号（BUILD xxx）
SplashText=                ; 底部提示文案（可选；当前直接展示，未接多语言）
```

全部键可省略，缺省即默认值。**无 `SplashImage`/`SplashVideo` 时**显示内置
HUD 风格品牌画面（CNC NET 标题 + 青色动画），即默认可用。

## 路径语义

- 媒体路径**相对游戏根目录**（`GamePath`），如 `Resources/splash.png`。
- 客户端启动进程内本地 HTTP 服务（`http://127.0.0.1:随机端口/`）伺服游戏根目录，
  媒体按独立文件 URL 加载——**不打包进 wwwroot、不转 base64**，MOD 替换同名文件即可换图。
- 路径只读、防目录穿越；仅允许 GET/HEAD。

## 媒体格式与体积建议

| 类型 | 建议格式 | 体积建议 |
|---|---|---|
| 图片 | PNG / WebP / JPG | ≤ 1MB（全屏 1920×1080 建议 ≤ 500KB） |
| 视频 | MP4（H.264，无音轨）或 WebM | ≤ 8MB，时长与 `SplashDuration` 匹配 |

视频播放要求：**无音轨**（或静音），客户端以 `muted + autoplay + loop + playsinline`
加载，无需用户交互；支持 Range 请求（可 seek/拖拽）。注意视频解码会占用启动
时间，默认场景建议以图片为主。

## 示例

带视频闪屏：

```ini
[LoadingScreen]
ShowSplash=true
SplashVideo=Resources/splash.mp4
SplashDuration=3.0
ShowProgress=false
ShowVersion=true
SplashText=WELCOME, COMMANDER
```

## 相关

- F1 主题目录机制落地后，本节点支持按主题覆盖（默认闪屏 + 主题专属闪屏）。
- 演示屏/动画遵循 `prefers-reduced-motion`（进度条动画关闭；淡出过渡全局压缩）。
