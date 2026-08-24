#nullable enable
using CnCNetClient.Core.Caching;

using SixLabors.ImageSharp;

namespace CnCNetClient.Domain.Multiplayer;

public interface IMapPreviewCacheManager : ICacheManager<Map, Image> { }