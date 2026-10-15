// CNCNet 客户端 · 幽能星港（theme-d）前端动效
// 本地引入（无 CDN）。星粒背景 + 全息地球 + toast。
(function () {
    'use strict';

    const REDUCED = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    /* ── 主题调色板（canvas 跟随 CSS 变量换色） ───────────────── */
    // 读 :root 的 CSS 变量，主题切换后 canvas 星粒/地球自动换色；未变时免重复读取。
    let PAL = { cyan: '#21e6ff', cyanSoft: '#7af3ff', gold: '#ffd34d', text: '#dff6ff' };
    let PAL_THEME = null;
    function refreshPalette() {
        const theme = document.documentElement.dataset.theme || 'menu';
        if (theme === PAL_THEME) return false;
        PAL_THEME = theme;
        const cs = getComputedStyle(document.documentElement);
        const v = (n, fb) => (cs.getPropertyValue(n) || '').trim() || fb;
        PAL = { cyan: v('--cyan', '#21e6ff'), cyanSoft: v('--cyan-soft', '#7af3ff'),
                gold: v('--gold', '#ffd34d'), text: v('--text', '#dff6ff') };
        return true;
    }

    /* ── 主题（IThemeProvider 桥接） ───────────────────────────── */
    const THEME_KEY = 'cncnet.theme';
    let themeUrls = {}; // F1：外部主题（MOD 主题目录）key → theme.css 的 http URL
    function setThemeUrls(urls) { themeUrls = urls || {}; }
    function applyTheme(key) {
        const url = themeUrls[key];
        let link = document.getElementById('ext-theme');
        if (url) {
            // 外部主题：注入 <link> 覆盖设计 tokens，并清除内置 data-theme 以整体换肤
            document.documentElement.removeAttribute('data-theme');
            if (!link) {
                link = document.createElement('link');
                link.id = 'ext-theme';
                link.rel = 'stylesheet';
                document.head.appendChild(link);
            }
            link.href = url;
        } else {
            if (link) link.remove();
            if (key && key !== 'menu') document.documentElement.dataset.theme = key;
            else document.documentElement.removeAttribute('data-theme');
        }
        try { localStorage.setItem(THEME_KEY, key); } catch (e) { /* 忽略持久化失败 */ }
        // 主题过渡：整体淡入一次（仅 opacity，尊重 reduced-motion）
        const b = document.body;
        b.classList.remove('theme-fade');
        void b.offsetWidth; // 强制 reflow 以重置动画
        b.classList.add('theme-fade');
    }
    function getTheme() {
        try { return localStorage.getItem(THEME_KEY) || 'menu'; } catch (e) { return 'menu'; }
    }
    // 主题首帧前回显（M4.1 spec §6.10：localStorage 仅作缓存，正式以 settings INI 为准）：
    // 在 Blazor 首帧渲染前应用缓存主题，避免启动闪变；首帧后 ThemeProvider 以 INI 校正。
    (function () {
        const cached = getTheme();
        if (cached) document.documentElement.dataset.theme = cached;
    })();

    /* ── 插件资源注入（F4：外部插件目录的 css/js，经 GameContentServer 伺服） ────
       与 applyTheme 的 <link> 注入同款链路；幂等：同 key 重复调用只更新 href，
       插件资源加载失败仅留 console/toast，不影响页面。 */
    const pluginLinks = new Map();
    function injectCss(key, url) {
        if (!key || !url) return;
        let el = pluginLinks.get(key);
        if (!el) {
            el = document.createElement('link');
            el.rel = 'stylesheet';
            el.dataset.plugin = key;
            el.onerror = () => console.warn('[plugin] 样式加载失败：' + key + ' → ' + url);
            document.head.appendChild(el);
            pluginLinks.set(key, el);
        }
        el.href = url;
    }
    function injectJs(url) {
        if (!url) return;
        const s = document.createElement('script');
        s.src = url;
        s.defer = true;
        s.dataset.plugin = '1';
        s.onerror = () => console.warn('[plugin] 脚本加载失败：' + url);
        document.head.appendChild(s);
    }

    /* ── 背景星粒（MainLayout #bg-canvas） ─────────────────────── */
    function initStars(canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        let bw, bh, stars;

        function resize() {
            refreshPalette();
            bw = canvas.width = window.innerWidth;
            bh = canvas.height = window.innerHeight;
            stars = Array.from({ length: Math.min(220, (window.innerWidth / 7) | 0) }, () => ({
                x: Math.random() * bw,
                y: Math.random() * bh,
                z: Math.random() * 1 + .3,
                c: Math.random() < .7 ? PAL.cyan : PAL.cyanSoft,
                s: Math.random() * 1.5 + .4
            }));
        }
        resize();
        window.addEventListener('resize', resize);

        if (REDUCED) return;

        (function loop() {
            if (refreshPalette()) {
                for (const p of stars) p.c = Math.random() < .7 ? PAL.cyan : PAL.cyanSoft;
            }
            ctx.clearRect(0, 0, bw, bh);
            for (const p of stars) {
                p.y -= p.s * p.z;
                if (p.y < 0) { p.y = bh; p.x = Math.random() * bw; }
                ctx.globalAlpha = .6 * p.z;
                ctx.fillStyle = p.c;
                ctx.beginPath();
                ctx.arc(p.x, p.y, p.s, 0, 7);
                ctx.fill();
            }
            ctx.globalAlpha = 1;
            requestAnimationFrame(loop);
        })();
    }

    /* ── 全息地球（MainMenu #globe） ───────────────────────────── */
    function initGlobe(canvasId) {
        const cv = document.getElementById(canvasId);
        if (!cv) return;
        const cx = cv.getContext('2d');
        let cw, ch, orb = [];

        function resize() {
            const r = cv.getBoundingClientRect();
            cw = cv.width = r.width * devicePixelRatio;
            ch = cv.height = r.height * devicePixelRatio;
            orb = Array.from({ length: 5 }, () => ({
                a: Math.random() * 6.28,
                sp: .3 + Math.random() * .6,
                r: .7 + Math.random() * .25,
                h: (Math.random() - .5) * 1.4
            }));
        }
        resize();
        window.addEventListener('resize', resize);

        if (REDUCED) return;

        function draw(t) {
            if (!cv.isConnected) return; // M4.9：canvas 已移出 DOM（离开主菜单）即停止 rAF，避免空转
            refreshPalette();
            const W = cw, H = ch, cxp = W / 2, cyp = H / 2, R = Math.min(W, H) * .34;
            cx.clearRect(0, 0, W, H);
            // 经纬点云
            for (let lat = -80; lat <= 80; lat += 8) {
                for (let lon = 0; lon < 360; lon += 5) {
                    const phi = lat * Math.PI / 180, th = lon * Math.PI / 180 + t * .4;
                    const x = Math.cos(phi) * Math.sin(th), y = Math.sin(phi), z = Math.cos(phi) * Math.cos(th);
                    const y2 = y * 0.5 - z * 0.86, z2 = y * 0.86 + z * 0.5;
                    const sx = cxp + x * R, sy = cyp - y2 * R, d = (z2 + 1) / 2;
                    cx.globalAlpha = .15 + d * .75;
                    cx.fillStyle = d > .5 ? PAL.cyanSoft : PAL.cyan;
                    cx.beginPath();
                    cx.arc(sx, sy, (.6 + d * 1.6) * devicePixelRatio, 0, 7);
                    cx.fill();
                }
            }
            // 轨道卫星
            for (const o of orb) {
                const a = o.a + t * o.sp;
                const x = Math.cos(a) * o.r * R, y = Math.sin(a) * o.r * R * 0.5 + o.h * R * .3;
                cx.globalAlpha = 1;
                cx.fillStyle = PAL.gold;
                cx.shadowColor = PAL.gold;
                cx.shadowBlur = 10 * devicePixelRatio;
                cx.beginPath();
                cx.arc(cxp + x, cyp + y, 2.2 * devicePixelRatio, 0, 7);
                cx.fill();
                cx.shadowBlur = 0;
            }
            // 中心徽记
            cx.globalAlpha = .9;
            cx.fillStyle = PAL.text;
            cx.font = '800 ' + (R * .55) + 'px Orbitron,sans-serif';
            cx.textAlign = 'center';
            cx.textBaseline = 'middle';
            cx.shadowColor = PAL.cyan;
            cx.shadowBlur = 24 * devicePixelRatio;
            cx.fillText('CN', cxp, cyp);
            cx.shadowBlur = 0;
            requestAnimationFrame(draw);
        }
        requestAnimationFrame(draw);
    }

    /* ── 雷达扫描光效（MainMenu #radar-scan，PixiJS 本地渲染） ──────
       2026-09-12：由 CSS conic-gradient 改为 PixiJS canvas——conic 从中心辐射
       天然呈扇形/三角形，且 CSS 背景旋转难以做成"从中心发出"的扫描线；
       PixiJS 用 Graphics 扇形 + Container 绕 pivot(中心) 旋转，从中心伸出的
       细光束绕中心扫过，即经典雷达扫描。库：本地 lib/pixi.min.js（无 CDN）。 */
    function initRadarScan(canvasId) {
        const cv = document.getElementById(canvasId);
        if (!cv) return;
        if (typeof PIXI === 'undefined') return; // 库缺失时静默跳过，不影响页面

        const ROT_SECONDS = 9;   // 9s 一圈（缓速扫描）
        const BEAM_OVER = 1.05;  // 光束长到容器对角线外（防穿帮，被八边形裁切优雅截断）
        let app = null, sweep = null, beamG, R = 0;
        let ro = null, destroyed = false;

        function hexNum() { return parseInt((PAL.cyan || '#21e6ff').replace('#', ''), 16); }

        // 从中心 (0,0) 伸出的扇形光束（半角 halfDeg 度、长 R），沿**角度方向**渐变：
        // 扫过方向的前缘亮、尾部渐隐（雷达余晖感）。用 M 段楔形近似平滑渐变。
        // alphaFn(u)：u∈[0,1] 从尾部到前缘，返回该处透明度。
        function buildSector(halfDeg, alphaFn) {
            const g = new PIXI.Graphics();
            const a0 = -halfDeg * Math.PI / 180; // 尾部边（后扫侧）
            const a1 = halfDeg * Math.PI / 180;  // 前缘边（前扫侧）
            const M = 16;
            const c = hexNum();
            for (let i = 0; i < M; i++) {
                const u0 = i / M, u1 = (i + 1) / M;
                const al = alphaFn((u0 + u1) / 2);
                if (al <= 0.002) continue;
                const aa = a0 + (a1 - a0) * u0;
                const ab = a0 + (a1 - a0) * u1;
                g.moveTo(0, 0).lineTo(Math.cos(aa) * R, Math.sin(aa) * R)
                    .lineTo(Math.cos(ab) * R, Math.sin(ab) * R).closePath();
                g.fill({ color: c, alpha: al });
            }
            return g;
        }

        function recolor() {
            // 单层光束：尾部微亮 → 前缘最亮（角度方向线性渐亮），无内/外层断层
            beamG = buildSector(7, u => .03 + .21 * u);
            sweep.removeChildren();
            sweep.addChild(beamG);
        }

        function onResize() {
            if (!app || destroyed) return;
            const r = cv.getBoundingClientRect();
            if (r.width < 2 || r.height < 2) return;
            app.renderer.resize(r.width, r.height);
            R = Math.hypot(r.width, r.height) * .5 * BEAM_OVER;
            sweep.position.set(r.width / 2, r.height / 2);
            recolor();
        }

        async function setup() {
            const r = cv.getBoundingClientRect();
            if (r.width < 2 || r.height < 2) return;
            R = Math.hypot(r.width, r.height) * .5 * BEAM_OVER;
            refreshPalette(); // 首帧即用当前主题色

            try {
                app = new PIXI.Application();
                await app.init({
                    canvas: cv,
                    width: r.width,
                    height: r.height,
                    backgroundAlpha: 0,
                    antialias: true,
                    resolution: window.devicePixelRatio || 1,
                    autoDensity: true
                });
            } catch (e) {
                app = null;
                return;
            }

            sweep = new PIXI.Container();
            sweep.position.set(r.width / 2, r.height / 2);
            app.stage.addChild(sweep);
            recolor();

            ro = new ResizeObserver(onResize);
            ro.observe(cv);

            if (REDUCED) return; // 静态光束，不旋转（尊重 prefers-reduced-motion）

            app.ticker.add((t) => {
                if (!cv.isConnected) { destroy(); return; } // 离开主菜单即停（同 initGlobe）
                if (refreshPalette()) recolor(); // 主题切换即时换色
                sweep.rotation = (sweep.rotation + t.deltaMS / 1000 * (Math.PI * 2 / ROT_SECONDS)) % (Math.PI * 2);
            });
        }

        function destroy() {
            if (destroyed) return;
            destroyed = true;
            ro?.disconnect();
            app?.destroy(true, { children: true });
            app = null;
        }

        setup();
    }

    /* ── 读数模拟（MainMenu #r2/#lat；#r1 在线人数为 r9-1 真实数据，由 setPlayerCount 写入） ── */
    function initReadout() {
        const r2 = document.getElementById('r2');
        const lat = document.getElementById('lat');
        if (!r2 || !lat) return;
        if (REDUCED) return;
        setInterval(() => {
            r2.textContent = (300 + Math.floor(Math.random() * 60));
            lat.textContent = (16 + Math.floor(Math.random() * 20));
        }, 2400);
    }

    /* r9-1：CnCNet 在线人数（真实数据，.NET 订阅 CnCNetPlayerCountTask 后写入） */
    function setPlayerCount(n) {
        const el = document.getElementById('r1');
        if (!el) return;
        el.textContent = n > 0 ? n.toLocaleString() : 'N/A';
    }

    /* ── Toast ─────────────────────────────────────────────────── */
    // kind: 可选 ''(默认青) | 'err'(红) | 'warn'(琥珀)，驱动断线/重连提示样式（M3.10）
    let toastTimer;
    function toast(msg, kind) {
        const t = document.getElementById('toast');
        if (!t) return;
        t.innerHTML = msg;
        t.className = 'toast show' + (kind ? ' ' + kind : '');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => t.classList.remove('show'), 1800);
    }

    /* ── 主菜单键盘导航（ESC 返回首页，实现「可退」） ──────────── */
    let menuKeyHandler;
    function initMenuKeyNav(dotNetRef) {
        disposeMenuKeyNav();
        menuKeyHandler = (e) => {
            if (e.key === 'Escape') dotNetRef.invokeMethodAsync('OnEscKey');
        };
        window.addEventListener('keydown', menuKeyHandler);
    }
    function disposeMenuKeyNav() {
        if (menuKeyHandler) {
            window.removeEventListener('keydown', menuKeyHandler);
            menuKeyHandler = null;
        }
    }

    /* ── 聊天区滚动到底部（在线大厅） ─────────────────────────── */
    function scrollToBottom(el) {
        if (!el) return;
        el.scrollIntoView({ block: 'nearest', behavior: REDUCED ? 'auto' : 'smooth' });
    }

    /* ── 视口尺寸（右键菜单坐标钳制） ────────────────────────── */
    function getViewport() {
        return { width: window.innerWidth, height: window.innerHeight };
    }

    /* ── 大厅键盘导航（ESC 关闭右键菜单/私聊窗，不导航） ────── */
    let lobbyKeyHandler;
    function initLobbyKeyNav(dotNetRef) {
        disposeLobbyKeyNav();
        lobbyKeyHandler = (e) => {
            if (e.key === 'Escape') dotNetRef.invokeMethodAsync('OnLobbyEsc');
        };
        window.addEventListener('keydown', lobbyKeyHandler);
    }
    function disposeLobbyKeyNav() {
        if (lobbyKeyHandler) {
            window.removeEventListener('keydown', lobbyKeyHandler);
            lobbyKeyHandler = null;
        }
    }

    /* ── 房主不活跃检测活动上报（原版 MouseMove → Reset；页面级 mousemove/keydown 节流回调） ── */
    let hostActivityHandler;
    let hostActivityTimer;
    function initHostActivityWatcher(dotNetRef) {
        disposeHostActivityWatcher();
        hostActivityHandler = () => {
            if (hostActivityTimer) return;
            hostActivityTimer = setTimeout(() => {
                hostActivityTimer = null;
                dotNetRef.invokeMethodAsync('OnHostActivity');
            }, 250);
        };
        window.addEventListener('mousemove', hostActivityHandler);
        window.addEventListener('keydown', hostActivityHandler);
    }
    function disposeHostActivityWatcher() {
        if (hostActivityHandler) {
            window.removeEventListener('mousemove', hostActivityHandler);
            window.removeEventListener('keydown', hostActivityHandler);
            hostActivityHandler = null;
        }
        if (hostActivityTimer) {
            clearTimeout(hostActivityTimer);
            hostActivityTimer = null;
        }
    }

    /* ── 复制文本到剪贴板（地图名复制；WebView2 优先 Clipboard API，降级 execCommand） ── */
    function copyText(text) {
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(text).catch(() => fallbackCopy(text));
        } else {
            fallbackCopy(text);
        }
    }
    function fallbackCopy(text) {
        try {
            const ta = document.createElement('textarea');
            ta.value = text;
            ta.style.position = 'fixed';
            ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.select();
            document.execCommand('copy');
            document.body.removeChild(ta);
        } catch (e) { /* 忽略复制失败 */ }
    }

    // 地图预览对齐：出生点覆盖层（.spawn-layer，inset:0）与缩略图必须完全重合。
    // 缩略图以 object-fit:contain 渲染并保留原始宽高比；若容器只固定一个比例（如 802/324）
    // 或仅改 aspect-ratio，一旦容器宽度/高度受父级 max-width/max-height 约束发生截断，
    // contain 就会在容器内留白 → 出生点百分比坐标与图片像素错位。
    // 改为：按图片自然宽高比 + 父容器可用空间（clientWidth/clientHeight）直接算出容器实际宽高，
    // 使图片铺满容器无留白，覆盖层坐标与图片一一对应（多页共用，无 JS 时忽略）。
    // 窗口缩放/布局变化时经 ResizeObserver 重新对齐；首次 onload 若布局未就绪则稍后重试。
    function fitMapPreview(img) {
        try {
            if (!img || !img.isConnected || !img.naturalWidth || !img.naturalHeight) return;
            const box = img.closest('.cfg-map-preview');
            const wrap = box ? box.parentElement : null;
            if (!box || !wrap) return;
            const availW = wrap.clientWidth;
            const availH = wrap.clientHeight;
            if (!availW || !availH) {
                if (!img._fitRetried) {
                    img._fitRetried = true;
                    setTimeout(() => fitMapPreview(img), 80);
                }
                return;
            }
            const r = img.naturalWidth / img.naturalHeight;
            let w, h;
            if (availW / availH > r) { h = availH; w = Math.round(h * r); }
            else { w = availW; h = Math.round(w / r); }
            box.style.width = w + 'px';
            box.style.height = h + 'px';
            box.style.aspectRatio = '';
            if (!box._fitObserved) {
                box._fitObserved = true;
                try { new ResizeObserver(() => fitMapPreview(img)).observe(wrap); }
                catch (e) { /* 旧浏览器无 ResizeObserver：保持初始对齐 */ }
            }
        } catch (e) { /* 忽略 */ }
    }

    /* ── FPS 统计（真实渲染帧率：rAF 由 Chromium 渲染器驱动，计数即实际合成帧）。
        纯本地、零网络。每秒结算一次并做 EMA 平滑，写回所有 [data-fps] 元素。
        窗口最小化时 Chromium 会暂停 rAF，读数停更属正常行为。 */
    let _fpsFrames = 0;
    let _fpsSmooth = 0;
    (function fpsLoop() { _fpsFrames++; requestAnimationFrame(fpsLoop); })();
    setInterval(() => {
        const raw = _fpsFrames;
        _fpsFrames = 0;
        _fpsSmooth = _fpsSmooth === 0 ? raw : _fpsSmooth * 0.75 + raw * 0.25;
        const v = Math.round(_fpsSmooth);
        document.querySelectorAll('[data-fps]').forEach(el => { el.textContent = String(v); });
    }, 1000);

    /* ── 全局悬浮顶栏（原版 TopBar：鼠标移到顶部滑出，移开 1s 收回；F2/F3/F4/F12 快捷键） ── */
    let _topBarHideTimer = null;
    function initTopBar(dotnetRef) {
        const bar = document.getElementById('hud-topbar');
        if (!bar) return;
        const show = () => {
            if (_topBarHideTimer) { clearTimeout(_topBarHideTimer); _topBarHideTimer = null; }
            bar.classList.add('show');
        };
        const hide = () => {
            if (_topBarHideTimer) clearTimeout(_topBarHideTimer);
            _topBarHideTimer = setTimeout(() => bar.classList.remove('show'), 1000);
        };
        // 触发区 56px（顶部滑出判定）；顶栏展开后鼠标悬停在其矩形内则保持显示
        const onMove = (e) => {
            if (e.clientY <= 56) { show(); return; }
            const r = bar.getBoundingClientRect();
            if (r.height > 0 && e.clientY >= r.top && e.clientY <= r.bottom && e.clientX >= r.left && e.clientX <= r.right) { show(); return; }
            hide();
        };
        // 原版 TopBar 键盘监听：F2 遭遇战大厅 / F3 CnCNet 大厅 / F4 私信 / F12 选项
        const onKey = (e) => {
            let k = null;
            if (e.keyCode === 113) k = 2;        // F2
            else if (e.keyCode === 114) k = 3;   // F3
            else if (e.keyCode === 115) k = 4;   // F4
            else if (e.keyCode === 123) k = 12;  // F12
            if (k !== null) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnTopBarHotKey', k);
            }
        };
        window.addEventListener('mousemove', onMove);
        window.addEventListener('keydown', onKey);
        bar._topBarCleanup = () => {
            window.removeEventListener('mousemove', onMove);
            window.removeEventListener('keydown', onKey);
            if (_topBarHideTimer) { clearTimeout(_topBarHideTimer); _topBarHideTimer = null; }
        };
    }
    function disposeTopBar() {
        const bar = document.getElementById('hud-topbar');
        if (bar && bar._topBarCleanup) { bar._topBarCleanup(); delete bar._topBarCleanup; }
    }

    window.cncnet = { initStars, initGlobe, initRadarScan, initReadout, setPlayerCount, toast, applyTheme, getTheme, setThemeUrls, injectCss, injectJs, initMenuKeyNav, disposeMenuKeyNav, scrollToBottom, getViewport, initLobbyKeyNav, disposeLobbyKeyNav, initHostActivityWatcher, disposeHostActivityWatcher, copyText, fitMapPreview, initTopBar, disposeTopBar };
})();
