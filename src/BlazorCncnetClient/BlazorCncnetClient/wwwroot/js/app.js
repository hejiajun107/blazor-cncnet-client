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
    function applyTheme(key) {
        document.documentElement.dataset.theme = key;
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

    /* ── 读数模拟（MainMenu #r1/#r2/#lat） ─────────────────────── */
    function initReadout() {
        const r1 = document.getElementById('r1');
        const r2 = document.getElementById('r2');
        const lat = document.getElementById('lat');
        if (!r1 || !r2 || !lat) return;
        if (REDUCED) return;
        setInterval(() => {
            r1.textContent = (1200 + Math.floor(Math.random() * 300)).toLocaleString();
            r2.textContent = (300 + Math.floor(Math.random() * 60));
            lat.textContent = (16 + Math.floor(Math.random() * 20));
        }, 2400);
    }

    /* ── Toast ─────────────────────────────────────────────────── */
    let toastTimer;
    function toast(msg) {
        const t = document.getElementById('toast');
        if (!t) return;
        t.innerHTML = msg;
        t.classList.add('show');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => t.classList.remove('show'), 1500);
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

    /* ── 登录偏好（M3 过渡：localStorage；M4 4.1.1 改由 UserINISettings 写 settings INI） ── */
    const LOGIN_KEY = 'cncnet.login';
    function saveLoginPrefs(name, remember, persistent, auto) {
        try { localStorage.setItem(LOGIN_KEY, JSON.stringify({ name, remember, persistent, auto })); } catch (e) { /* 忽略持久化失败 */ }
    }
    function loadLoginPrefs() {
        try { return JSON.parse(localStorage.getItem(LOGIN_KEY) || 'null'); } catch (e) { return null; }
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

    window.cncnet = { initStars, initGlobe, initReadout, toast, applyTheme, getTheme, initMenuKeyNav, disposeMenuKeyNav, saveLoginPrefs, loadLoginPrefs, scrollToBottom, getViewport, initLobbyKeyNav, disposeLobbyKeyNav };
})();
