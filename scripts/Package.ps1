param(
    [string]$InputDirectory = 'artifacts/desktop',
    [string]$OutputDirectory = 'artifacts/package'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = [IO.Path]::GetFullPath((Join-Path $repo $InputDirectory))
$destination = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
if ($destination.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
    $destination.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package output must be outside the desktop directory.'
}
foreach ($name in @(
    'VMD2Anim-Neo.exe', 'README.md', 'README.zh-CN.md', 'LICENSE', 'docs/BUILD.md', 'build-manifest.json',
    'Core/VmdConverterCore.exe', 'Core/VmdConverterCore.Shared.dll',
    'Core/VmdConverterCore.Hps.dll', 'Core/UnityHps.Core.dll', 'Core/UnityHps.Fbx.dll',
    'VMD-Converter-Core-LICENSE.txt', 'Unity-HPS-LICENSE.txt', 'Unity-HPS-NOTICE.txt', 'ufbx-LICENSE.txt'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
        throw "Incomplete desktop package: $name"
    }
}
if ((Test-Path -LiteralPath $destination) -and @(Get-ChildItem -LiteralPath $destination -Force).Count -gt 0) {
    throw "Package output directory must be empty: $destination"
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$archive = Join-Path $destination 'VMD2Anim-Neo-win-x64.zip'
Compress-Archive -Path (Join-Path $source '*') -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
Write-Host "Ready: $archive"
