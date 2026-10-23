using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Loggepector.Models;

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(LogisSessionSettings))]
[JsonSerializable(typeof(string[]), TypeInfoPropertyName = "LogisStrings")]
[JsonSerializable(typeof(Dictionary<string, string[]>), TypeInfoPropertyName = "LogisFolders")]
[JsonSerializable(typeof(MapCacheState))]
[JsonSerializable(typeof(MapToolsSettings))]
[JsonSerializable(typeof(MapTileManifest))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
