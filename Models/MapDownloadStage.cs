using System;
using System.Collections.Generic;

namespace Loggepector.Models;

public sealed record MapDownloadFile(string Name, Uri Url, long? Size = null)
{
    public string SizeLabel => Size is { } size ? $"{size / 1048576.0:N1} МБ" : string.Empty;
}

public sealed record MapDownloadStage(string Title, string Description, IReadOnlyList<MapDownloadFile> Files);
