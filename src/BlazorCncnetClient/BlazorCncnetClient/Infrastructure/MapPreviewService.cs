using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CnCNetClient.Domain.Multiplayer;
using SixLabors.ImageSharp;

namespace BlazorCncnetClient.Infrastructure
{
    /// <summary>
    /// 地图预览服务（M3.4 spec §6.4）：按广播中的地图 SHA1/名称解析本地地图，
    /// 用 ImageSharp（复用 MapLoader 的 MapPreviewCacheManager / FastMapPreviewExtractor）
    /// 提取预览图 → PNG base64 data URL，供 <see cref="MapThumbnail"/> 直接 <c>&lt;img&gt;</c> 渲染。
    /// 全局限流：一次仅提取一张（CPU 密集），结果按 hash/名称 永久缓存，避免大厅列表重复解码。
    /// </summary>
    public sealed class MapPreviewService : IDisposable
    {
        private readonly MapLoader _mapLoader;
        private readonly ConcurrentDictionary<string, string> _previewCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _gate = new(1, 1);
        private int _loadStarted;

        public MapPreviewService(MapLoader mapLoader)
        {
            _mapLoader = mapLoader;
            // 地图集合尚未加载时（懒实例化 + 后台 LoadMapsAsync 未完成）由首张预览请求兜底触发加载。
            EnsureMapsLoaded();
        }

        /// <summary>地图是否已就绪（首次加载完成）。</summary>
        public bool IsMapsReady => _mapLoader.GameModeMaps.Count > 0;

        /// <summary>
        /// 取预览 data URL；找不到本地地图或提取失败返回 null（组件显示「无预览」占位）。
        /// </summary>
        public async Task<string?> GetPreviewUrlAsync(string? mapHash, string? mapName = null)
        {
            if (string.IsNullOrEmpty(mapHash) && string.IsNullOrEmpty(mapName))
                return null;

            var key = !string.IsNullOrEmpty(mapHash) ? mapHash : mapName!;
            if (_previewCache.TryGetValue(key, out var cached))
                return cached;

            await _gate.WaitAsync();
            try
            {
                if (_previewCache.TryGetValue(key, out cached))
                    return cached;

                var url = await Task.Run(() => ResolveAndEncode(mapHash, mapName));
                if (!string.IsNullOrEmpty(url))
                    _previewCache.TryAdd(key, url);
                return url;
            }
            finally
            {
                _gate.Release();
            }
        }

        private string? ResolveAndEncode(string? mapHash, string? mapName)
        {
            var map = FindMap(mapHash, mapName);
            if (map is null)
            {
                CnCNetClient.Tools.Logger.Log($"MapPreviewService: map not found hash='{mapHash}' name='{mapName}'");
                return null;
            }

            Image? image = null;
            try
            {
                // syncLoadOnCacheMiss: 命中缓存立即返回；未命中走 FastMapPreviewExtractor 提取（复用 MapPreviewCacheManager）
                image = _mapLoader.GetPreviewTextureFromMap(map, syncLoadOnCacheMiss: true);
                CnCNetClient.Tools.Logger.Log($"MapPreviewService: map='{map.Name}' previewPath='{map.PreviewPath}' immediate={map.IsImmediatePreviewImageAvailable()} image={(image != null ? "OK" : "NULL")}");
                if (image is null)
                    return null;

                using var ms = new MemoryStream();
                image.SaveAsPng(ms);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                // 单张预览失败不影响大厅；返回 null 走占位
                CnCNetClient.Tools.Logger.Log($"MapPreviewService: encode failed for '{map?.Name}': {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            finally
            {
                image?.Dispose();
            }
        }

        /// <summary>解析地图：优先按 SHA1 哈希精确匹配，其次按名称/路径回退匹配（官方 + 自定义地图均已载入集合）。</summary>
        private Map? FindMap(string? mapHash, string? mapName)
        {
            if (!string.IsNullOrEmpty(mapHash))
            {
                var byHash = _mapLoader.FindMapByHash(mapHash);
                if (byHash is not null)
                    return byHash;
            }

            if (!string.IsNullOrEmpty(mapName))
            {
                foreach (var gameModeMap in _mapLoader.GameModeMaps)
                {
                    var map = gameModeMap.Map;
                    if (string.Equals(map.Name, mapName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(map.UntranslatedName, mapName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(map.BaseFilePath, mapName, StringComparison.OrdinalIgnoreCase))
                        return map;
                }
            }

            return null;
        }

        private void EnsureMapsLoaded()
        {
            if (Interlocked.Exchange(ref _loadStarted, 1) != 0)
                return;
            try
            {
                _ = _mapLoader.LoadMapsAsync();
            }
            catch (Exception ex)
            {
                CnCNetClient.Tools.Logger.Log($"MapPreviewService: 后台地图加载启动失败: {ex.Message}");
            }
        }

        public void Dispose() => _gate.Dispose();
    }
}
