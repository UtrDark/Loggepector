using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Loggepector.Models;

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(MapCacheState))]
[JsonSerializable(typeof(MapToolsSettings))]
[JsonSerializable(typeof(MapTileManifest))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
