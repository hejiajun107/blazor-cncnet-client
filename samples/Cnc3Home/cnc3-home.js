/* ==========================================================================
 * CNC3 风格主页：three.js 轨道地球
 *
 * - 由 Cnc3HomeMenu.razor 在首次渲染后调用 window.cnc3Home.mount(canvasId)
 * - three.js 与贴图全部为插件本地文件（assets/），不依赖 CDN
 * - 贴图延迟加载：加载完成前显示 CSS 降级球，完成后淡入 WebGL 画面
 * - prefers-reduced-motion 时不旋转、不做逐帧循环；卸载时完整释放 GPU 资源
 * ========================================================================== */
(function () {
    'use strict';

    var scriptSrc = document.currentScript && document.currentScript.src ? document.currentScript.src : '';
    var baseUrl = scriptSrc ? new URL('.', scriptSrc).href : '';

    var reduceMotion = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
    var threePromise = null;
    var active = null;
    var mountToken = 0;

    function warn(message, error) {
        if (window.console && console.warn) {
            console.warn('[cnc3-home] ' + message, error || '');
        }
    }

    function loadThree() {
        if (window.THREE) {
            return Promise.resolve(window.THREE);
        }

        if (threePromise) {
            return threePromise;
        }

        threePromise = new Promise(function (resolve, reject) {
            var script = document.createElement('script');
            script.src = baseUrl + 'assets/three.min.js';
            script.async = true;
            script.onload = function () {
                if (window.THREE) {
                    resolve(window.THREE);
                } else {
                    reject(new Error('three.js 已加载但未暴露 THREE'));
                }
            };
            script.onerror = function () {
                reject(new Error('three.js 加载失败'));
            };
            document.head.appendChild(script);
        });

        return threePromise;
    }

    function loadTexture(THREE, file) {
        return new Promise(function (resolve) {
            new THREE.TextureLoader().load(
                baseUrl + 'assets/textures/' + file,
                function (texture) {
                    texture.anisotropy = 4;
                    resolve(texture);
                },
                undefined,
                function (error) {
                    warn('贴图加载失败：' + file, error);
                    resolve(null);
                });
        });
    }

    /* ------------------------------ 着色器 ------------------------------ */

    var EARTH_VERTEX = [
        'varying vec2 vUv;',
        'varying vec3 vN;',
        'varying vec3 vV;',
        'void main() {',
        '  vUv = uv;',
        '  vN = normalize(normalMatrix * normal);',
        '  vec4 mv = modelViewMatrix * vec4(position, 1.0);',
        '  vV = normalize(-mv.xyz);',
        '  gl_Position = projectionMatrix * mv;',
        '}'
    ].join('\n');

    var EARTH_FRAGMENT = [
        'uniform sampler2D dayMap;',
        'uniform sampler2D nightMap;',
        'uniform sampler2D specMap;',
        'uniform vec3 sunDir;',
        'uniform vec3 atmoColor;',
        'varying vec2 vUv;',
        'varying vec3 vN;',
        'varying vec3 vV;',
        'void main() {',
        '  vec3 n = normalize(vN);',
        '  float lambert = dot(n, sunDir);',
        '  float day = smoothstep(-0.18, 0.34, lambert);',
        '  vec3 dayCol = texture2D(dayMap, vUv).rgb;',
        '  dayCol *= 0.42 + 0.72 * clamp(lambert, 0.0, 1.0);',
        '  vec3 nightCol = texture2D(nightMap, vUv).rgb;',
        // 城市灯光统一染成青色，贴合本客户端幽能主题
        '  vec3 nightTint = nightCol * vec3(0.42, 1.0, 1.05) * 1.9;',
        '  vec3 color = mix(nightTint, dayCol, day);',
        '  float spec = texture2D(specMap, vUv).g;',
        '  vec3 h = normalize(sunDir + vV);',
        '  float gloss = pow(max(dot(n, h), 0.0), 40.0) * spec * day;',
        '  color += vec3(0.55, 0.92, 1.0) * gloss * 0.55;',
        '  float rim = pow(1.0 - max(dot(n, vV), 0.0), 3.0);',
        '  color += atmoColor * rim * 0.32;',
        '  gl_FragColor = vec4(color, 1.0);',
        '}'
    ].join('\n');

    var CLOUD_FRAGMENT = [
        'uniform sampler2D cloudMap;',
        'uniform vec3 sunDir;',
        'uniform float opacity;',
        'varying vec2 vUv;',
        'varying vec3 vN;',
        'varying vec3 vV;',
        'void main() {',
        '  vec4 cloud = texture2D(cloudMap, vUv);',
        '  vec3 n = normalize(vN);',
        '  float lit = smoothstep(-0.12, 0.42, dot(n, sunDir));',
        '  vec3 color = mix(vec3(0.05, 0.14, 0.18), vec3(0.86, 0.99, 1.0), lit);',
        '  float alpha = cloud.a * (0.06 + 0.66 * lit) * opacity;',
        '  gl_FragColor = vec4(color, alpha);',
        '}'
    ].join('\n');

    var GLOW_FRAGMENT = [
        'uniform vec3 glowColor;',
        'varying vec3 vN;',
        'varying vec3 vV;',
        'void main() {',
        '  vec3 n = normalize(vN);',
        '  float intensity = pow(max(0.52 - dot(n, vec3(0.0, 0.0, 1.0)), 0.0), 4.0);',
        '  gl_FragColor = vec4(glowColor, clamp(intensity, 0.0, 1.0) * 0.62);',
        '}'
    ].join('\n');

    /* ------------------------------ 场景 ------------------------------ */

    function makeHaloTexture(THREE) {
        var canvas = document.createElement('canvas');
        canvas.width = 256;
        canvas.height = 256;

        var ctx = canvas.getContext('2d');
        var gradient = ctx.createRadialGradient(128, 128, 30, 128, 128, 128);
        gradient.addColorStop(0, 'rgba(33, 230, 255, 0.30)');
        gradient.addColorStop(0.55, 'rgba(33, 230, 255, 0.09)');
        gradient.addColorStop(1, 'rgba(33, 230, 255, 0)');
        ctx.fillStyle = gradient;
        ctx.fillRect(0, 0, 256, 256);

        return new THREE.CanvasTexture(canvas);
    }

    function buildScene(THREE, canvas, textures) {
        var renderer = new THREE.WebGLRenderer({
            canvas: canvas,
            antialias: true,
            alpha: true,
            powerPreference: 'high-performance'
        });
        renderer.setClearColor(0x000000, 0);
        renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
        renderer.setSize(canvas.clientWidth || 1, canvas.clientHeight || 1, false);

        var scene = new THREE.Scene();
        var camera = new THREE.PerspectiveCamera(34, 1, 0.1, 100);
        camera.position.set(0, 0.06, 6.7);
        camera.lookAt(0, 0, 0);

        // 星空（远平面点缀，色彩与 UI 青色统一）
        var starCount = 520;
        var starPositions = new Float32Array(starCount * 3);
        for (var i = 0; i < starCount; i++) {
            starPositions[i * 3] = (Math.random() - 0.5) * 15;
            starPositions[i * 3 + 1] = (Math.random() - 0.5) * 11;
            starPositions[i * 3 + 2] = -6 - Math.random() * 12;
        }

        var starGeometry = new THREE.BufferGeometry();
        starGeometry.setAttribute('position', new THREE.BufferAttribute(starPositions, 3));
        var stars = new THREE.Points(starGeometry, new THREE.PointsMaterial({
            color: 0x9fe9ff,
            size: 0.028,
            transparent: true,
            opacity: 0.55,
            depthWrite: false
        }));
        scene.add(stars);

        // 地球（球心在原点，轴倾角轻微）
        var earthGroup = new THREE.Group();
        earthGroup.rotation.z = -0.10;
        earthGroup.rotation.x = 0.08;
        scene.add(earthGroup);

        var sunDir = new THREE.Vector3(-0.55, 0.26, 0.79).normalize();
        var atmoColor = new THREE.Color(0x21e6ff);

        var earthMaterial = new THREE.ShaderMaterial({
            uniforms: {
                dayMap: { value: textures.day },
                nightMap: { value: textures.night },
                specMap: { value: textures.spec },
                sunDir: { value: sunDir },
                atmoColor: { value: atmoColor }
            },
            vertexShader: EARTH_VERTEX,
            fragmentShader: EARTH_FRAGMENT
        });

        var earth = new THREE.Mesh(new THREE.SphereGeometry(1, 96, 96), earthMaterial);
        earthGroup.add(earth);

        // 云层：略大球体，低透明度，随地球缓慢相对漂移
        var cloudMaterial = new THREE.ShaderMaterial({
            uniforms: {
                cloudMap: { value: textures.clouds },
                sunDir: { value: sunDir },
                opacity: { value: 0.46 }
            },
            vertexShader: EARTH_VERTEX,
            fragmentShader: CLOUD_FRAGMENT,
            transparent: true,
            depthWrite: false
        });

        var clouds = new THREE.Mesh(new THREE.SphereGeometry(1.012, 72, 72), cloudMaterial);
        earthGroup.add(clouds);

        // 大气辉光：背面渲染 + 加法混合，仅在球体边缘可见
        var glowMaterial = new THREE.ShaderMaterial({
            uniforms: { glowColor: { value: atmoColor } },
            vertexShader: EARTH_VERTEX,
            fragmentShader: GLOW_FRAGMENT,
            side: THREE.BackSide,
            blending: THREE.AdditiveBlending,
            transparent: true,
            depthWrite: false
        });

        var glow = new THREE.Mesh(new THREE.SphereGeometry(1.10, 64, 64), glowMaterial);
        earthGroup.add(glow);

        // 柔光层：径向渐变贴图，压低整体硬边
        var haloTexture = makeHaloTexture(THREE);
        var halo = new THREE.Sprite(new THREE.SpriteMaterial({
            map: haloTexture,
            color: 0xffffff,
            blending: THREE.AdditiveBlending,
            transparent: true,
            depthWrite: false,
            opacity: 0.58
        }));
        halo.scale.set(3.35, 3.35, 1);
        halo.position.set(0, 0, -0.4);
        scene.add(halo);

        // 轨道环（缓慢转动，强化“轨道视角”）
        var ringMaterial = new THREE.MeshBasicMaterial({
            color: 0x21e6ff,
            transparent: true,
            opacity: 0.22,
            side: THREE.DoubleSide,
            blending: THREE.AdditiveBlending,
            depthWrite: false
        });

        var ring = new THREE.Mesh(new THREE.RingGeometry(1.42, 1.428, 180), ringMaterial);
        ring.rotation.x = 1.28;
        ring.rotation.y = 0.22;
        scene.add(ring);

        var ringOuterMaterial = ringMaterial.clone();
        ringOuterMaterial.opacity = 0.1;
        var ringOuter = new THREE.Mesh(new THREE.RingGeometry(1.66, 1.664, 180), ringOuterMaterial);
        ringOuter.rotation.x = 1.36;
        ringOuter.rotation.y = -0.18;
        scene.add(ringOuter);

        return {
            renderer: renderer,
            scene: scene,
            camera: camera,
            earth: earth,
            clouds: clouds,
            ring: ring,
            ringOuter: ringOuter,
            stars: stars,
            textures: [textures.day, textures.night, textures.spec, textures.clouds, haloTexture],
            canvas: canvas,
            raf: 0,
            resizeObserver: null,
            resize: null
        };
    }

    function mount(canvasId) {
        var canvas = document.getElementById(canvasId);
        if (!canvas) {
            return Promise.resolve(false);
        }

        if (active && active.canvas === canvas) {
            return Promise.resolve(true);
        }

        dispose();

        var token = ++mountToken;
        var host = canvas.closest ? canvas.closest('.c3-globe') : canvas.parentElement;

        return loadThree()
            .then(function (THREE) {
                return Promise.all([
                    loadTexture(THREE, 'earth_day.jpg'),
                    loadTexture(THREE, 'earth_night.png'),
                    loadTexture(THREE, 'earth_specular.jpg'),
                    loadTexture(THREE, 'earth_clouds.png')
                ]).then(function (loaded) {
                    return { THREE: THREE, loaded: loaded };
                });
            })
            .then(function (result) {
                var textures = {
                    day: result.loaded[0],
                    night: result.loaded[1],
                    spec: result.loaded[2],
                    clouds: result.loaded[3]
                };

                if (!textures.day) {
                    throw new Error('地球日照贴图缺失');
                }

                // 夜景 / 高光 / 云层缺失时用日照贴图兜底，保证场景可用
                if (!textures.night) {
                    textures.night = textures.day;
                }
                if (!textures.spec) {
                    textures.spec = textures.day;
                }
                if (!textures.clouds) {
                    textures.clouds = textures.day;
                }

                var state = buildScene(result.THREE, canvas, textures);

                // 加载期间组件已卸载或重新挂载：丢弃本次场景，避免复活已销毁的 canvas
                if (token !== mountToken) {
                    state.scene.traverse(function (object) {
                        if (object.geometry) {
                            object.geometry.dispose();
                        }
                        if (object.material) {
                            var materials = Array.isArray(object.material) ? object.material : [object.material];
                            for (var m = 0; m < materials.length; m++) {
                                materials[m].dispose();
                            }
                        }
                    });
                    for (var t = 0; t < state.textures.length; t++) {
                        if (state.textures[t]) {
                            state.textures[t].dispose();
                        }
                    }
                    state.renderer.dispose();
                    return false;
                }

                active = state;

                function resize() {
                    if (!active) {
                        return;
                    }

                    var width = canvas.clientWidth || 1;
                    var height = canvas.clientHeight || 1;
                    active.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
                    active.renderer.setSize(width, height, false);
                    active.camera.aspect = width / height;
                    active.camera.updateProjectionMatrix();
                }

                state.resize = resize;
                resize();

                if (window.ResizeObserver) {
                    state.resizeObserver = new ResizeObserver(resize);
                    state.resizeObserver.observe(canvas);
                } else {
                    window.addEventListener('resize', resize);
                }

                canvas.addEventListener('webglcontextlost', function (event) {
                    event.preventDefault();
                    if (active) {
                        cancelAnimationFrame(active.raf);
                        active.raf = 0;
                    }
                    if (host) {
                        host.classList.remove('is-live');
                    }
                }, false);

                if (host) {
                    host.classList.add('is-live');
                }

                var clock = new THREE.Clock();

                function frame() {
                    if (!active) {
                        return;
                    }

                    active.raf = requestAnimationFrame(frame);

                    if (document.hidden) {
                        clock.getDelta();
                        return;
                    }

                    var delta = Math.min(clock.getDelta(), 0.05);

                    if (!reduceMotion) {
                        active.earth.rotation.y += delta * 0.05;
                        active.clouds.rotation.y += delta * 0.058;
                        active.ring.rotation.z += delta * 0.02;
                        active.ringOuter.rotation.z -= delta * 0.014;
                    }

                    active.renderer.render(active.scene, active.camera);
                }

                if (reduceMotion) {
                    // 静态渲染一帧即可：不启动逐帧循环
                    active.renderer.render(active.scene, active.camera);
                } else {
                    frame();
                }

                return true;
            })
            .catch(function (error) {
                warn('3D 地球未启用，保留 CSS 降级画面', error);
                if (host) {
                    host.classList.remove('is-live');
                }
                return false;
            });
    }

    function dispose() {
        mountToken++;
        var state = active;
        active = null;

        if (!state) {
            return;
        }

        if (state.raf) {
            cancelAnimationFrame(state.raf);
        }

        if (state.resizeObserver) {
            state.resizeObserver.disconnect();
        } else if (state.resize) {
            window.removeEventListener('resize', state.resize);
        }

        state.scene.traverse(function (object) {
            if (object.geometry) {
                object.geometry.dispose();
            }

            if (object.material) {
                var materials = Array.isArray(object.material) ? object.material : [object.material];
                for (var i = 0; i < materials.length; i++) {
                    materials[i].dispose();
                }
            }
        });

        for (var i = 0; i < state.textures.length; i++) {
            if (state.textures[i]) {
                state.textures[i].dispose();
            }
        }

        state.renderer.dispose();

        try {
            state.renderer.forceContextLoss();
        } catch (error) {
            // 部分 WebView 版本不支持强制释放，忽略
        }

        var host = state.canvas.closest ? state.canvas.closest('.c3-globe') : null;
        if (host) {
            host.classList.remove('is-live');
        }
    }

    window.cnc3Home = {
        mount: mount,
        dispose: dispose
    };
})();
