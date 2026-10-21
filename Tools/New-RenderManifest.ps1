[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,
    [Parameter(Mandatory = $true)]
    [string]$TraceCsv,
    [Parameter(Mandatory = $true)]
    [int[]]$RendererProcessId,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$Commit,
    [Parameter(Mandatory = $true)]
    [string[]]$MapPath,
    [Parameter(Mandatory = $true)]
    [string]$RendererPath,
    [string]$Output = (Join-Path $PSScriptRoot 'render-manifest.json'),
    [string[]]$AdditionalPath = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sourceDirectory = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $SourceRoot).Path).TrimEnd('\', '/')
$sourcePrefix = $sourceDirectory + [IO.Path]::DirectorySeparatorChar
$traceFile = (Resolve-Path -LiteralPath $TraceCsv).Path
$rendererFile = (Resolve-Path -LiteralPath $RendererPath).Path
$outputFile = [IO.Path]::GetFullPath($Output)
$processIds = [Collections.Generic.HashSet[int]]::new()
foreach ($processId in $RendererProcessId) {
    if ($processId -le 0) { throw 'Renderer process IDs must be positive.' }
    [void]$processIds.Add($processId)
}
$accessed = [Collections.Generic.Dictionary[string, Collections.Generic.HashSet[int]]]::new([StringComparer]::OrdinalIgnoreCase)
$reasons = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$rows = 0
$matchedEvents = 0
Import-Csv -LiteralPath $traceFile | ForEach-Object {
    $row = $_
    if ($rows -eq 0) {
        foreach ($column in @('PID', 'Process Name', 'Operation', 'Path', 'Result')) {
            if ($null -eq $row.PSObject.Properties[$column]) {
                throw "Missing CSV column: $column. Export all events from Process Monitor as CSV."
            }
        }
    }
    $rows++
    $eventProcessId = 0
    if ([int]::TryParse($row.PID, [ref]$eventProcessId) -and $processIds.Contains($eventProcessId) -and
        $row.Result -eq 'SUCCESS' -and $row.Operation -eq 'ReadFile') {
        $eventPath = [string]$row.Path
        if ($eventPath.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            $fullPath = [IO.Path]::GetFullPath($eventPath)
            if (-not $fullPath.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Trace path escapes source directory: $eventPath"
            }
            if (-not [IO.File]::Exists($fullPath)) { throw "Traced file no longer exists: $fullPath" }
            $relative = $fullPath.Substring($sourcePrefix.Length).Replace('\', '/')
            if (-not $accessed.ContainsKey($relative)) {
                $accessed[$relative] = [Collections.Generic.HashSet[int]]::new()
            }
            [void]$accessed[$relative].Add($eventProcessId)
            $reasons[$relative] = 'renderer-read'
            $matchedEvents++
        }
    }
}
if ($rows -eq 0) { throw 'The trace CSV is empty.' }
if ($matchedEvents -eq 0) { throw 'No successful renderer reads found under SourceRoot. Check the trace and process IDs.' }
foreach ($required in (@('tgstation.dme') + $MapPath)) {
    $normalized = $required.Replace('\', '/')
    if (-not $accessed.ContainsKey($normalized)) {
        throw "Required input was not read in the trace: $normalized. Capture a complete render of every requested map."
    }
}
foreach ($extra in $AdditionalPath) {
    if ([IO.Path]::IsPathRooted($extra)) { throw "AdditionalPath must be relative: $extra" }
    $fullPath = [IO.Path]::GetFullPath((Join-Path $sourceDirectory $extra))
    if (-not $fullPath.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "AdditionalPath escapes source directory: $extra"
    }
    if (-not [IO.File]::Exists($fullPath)) { throw "Additional file does not exist: $extra" }
    $relative = $fullPath.Substring($sourcePrefix.Length).Replace('\', '/')
    if (-not $accessed.ContainsKey($relative)) {
        $accessed[$relative] = [Collections.Generic.HashSet[int]]::new()
        $reasons[$relative] = 'application-input'
    }
}
$files = @(
    foreach ($relative in ($accessed.Keys | Sort-Object)) {
        $fullPath = Join-Path $sourceDirectory $relative
        $before = [IO.FileInfo]::new($fullPath)
        $length = $before.Length
        $modified = $before.LastWriteTimeUtc
        $gitHash = [Security.Cryptography.SHA1]::Create()
        $fileHash = [Security.Cryptography.SHA256]::Create()
        $stream = [IO.File]::Open($fullPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            $header = [Text.Encoding]::UTF8.GetBytes("blob $length" + [char]0)
            [void]$gitHash.TransformBlock($header, 0, $header.Length, $header, 0)
            $buffer = [byte[]]::new(81920)
            while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                [void]$gitHash.TransformBlock($buffer, 0, $read, $buffer, 0)
                [void]$fileHash.TransformBlock($buffer, 0, $read, $buffer, 0)
            }
            [void]$gitHash.TransformFinalBlock([byte[]]::new(0), 0, 0)
            [void]$fileHash.TransformFinalBlock([byte[]]::new(0), 0, 0)
            $blobSha1 = [BitConverter]::ToString($gitHash.Hash).Replace('-', '').ToLowerInvariant()
            $sha256 = [BitConverter]::ToString($fileHash.Hash).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $stream.Dispose()
            $gitHash.Dispose()
            $fileHash.Dispose()
        }
        $after = [IO.FileInfo]::new($fullPath)
        if ($after.Length -ne $length -or $after.LastWriteTimeUtc -ne $modified) {
            throw "File changed while hashing: $relative"
        }
        [ordered]@{
            path = $relative
            size = $length
            gitBlobSha1 = $blobSha1
            sha256 = $sha256
            reason = $reasons[$relative]
            processIds = @($accessed[$relative] | Sort-Object)
        }
    }
)
$manifest = [ordered]@{
    schemaVersion = 1
    repository = 'ss220club/BandaStation'
    commit = $Commit.ToLowerInvariant()
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    dependencyMethod = 'successful-readfile-trace'
    coverage = 'observed-inputs; clean-directory replay not performed'
    maps = @($MapPath | ForEach-Object { $_.Replace('\', '/') } | Sort-Object -Unique)
    renderer = [ordered]@{
        file = [IO.Path]::GetFileName($rendererFile)
        sha256 = (Get-FileHash -LiteralPath $rendererFile -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    fileCount = $files.Count
    totalBytes = ($files | Measure-Object -Property size -Sum).Sum
    files = $files
}
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFile))
$temporary = $outputFile + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
[IO.File]::WriteAllText($temporary, ($manifest | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $outputFile -Force
Write-Output "Manifest: $outputFile"
Write-Output "Files: $($files.Count); bytes: $($manifest.totalBytes)"
