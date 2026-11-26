param(
    [string]$OutputDirectory = 'artifacts/desktop',
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and $PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Build on Windows with the .NET 10 SDK and Visual Studio C++ x64 tools.'
}
$repo = Split-Path $PSScriptRoot -Parent
$core = Join-Path $repo 'external/VMD-Converter-Core'
$destination = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
if (-not (Test-Path -LiteralPath (Join-Path $core 'VmdConverterCore.csproj'))) {
    throw 'Initialize the dependency first: git submodule update --init --recursive'
}
if ((Test-Path -LiteralPath $destination) -and @(Get-ChildItem -LiteralPath $destination -Force).Count -gt 0) {
    throw "Output directory must be empty. Choose a new directory: $destination"
}
function Assert-Success([string]$step) { if ($LASTEXITCODE -ne 0) { throw "$step failed (exit $LASTEXITCODE)." } }
Push-Location $repo
try {
    & dotnet run --project tests/Vmd2Anim.Neo.Tests -c Release
    Assert-Success 'Desktop bridge tests'
    & (Join-Path $core 'third_party/Unity-HPS/scripts/Build-Native.ps1')
    Assert-Success 'Native FBX reader build'
    & dotnet build (Join-Path $core 'src/Modules/Hps/VmdConverterCore.Hps.csproj') -c Release --warnaserror
    Assert-Success 'HPS adapter build'
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    & dotnet publish (Join-Path $core 'VmdConverterCore.csproj') -c Release -r win-x64 --self-contained $selfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $destination 'Core')
    Assert-Success 'Core publish'
    foreach ($name in @('VmdConverterCore.Hps.dll','UnityHps.Core.dll','UnityHps.Fbx.dll')) {
        Copy-Item -LiteralPath (Join-Path $core "src/Modules/Hps/bin/Release/net10.0/$name") -Destination (Join-Path $destination 'Core')
    }
    & dotnet publish src/Vmd2Anim.Neo/Vmd2Anim.Neo.csproj -c Release -r win-x64 --self-contained $selfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $destination
    Assert-Success 'Desktop publish'
    Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $core 'LICENSE') -Destination (Join-Path $destination 'VMD-Converter-Core-LICENSE.txt')
    foreach ($name in @('LICENSE','NOTICE')) {
        Copy-Item -LiteralPath (Join-Path $core "third_party/Unity-HPS/$name") -Destination (Join-Path $destination "Unity-HPS-$name.txt")
    }
    Copy-Item -LiteralPath (Join-Path $core 'third_party/Unity-HPS/third_party/ufbx/LICENSE') -Destination (Join-Path $destination 'ufbx-LICENSE.txt')
    $revision = & git -C $core rev-parse HEAD
    Assert-Success 'Submodule revision'
    $desktopRevision = & git -C $repo rev-parse HEAD
    Assert-Success 'Desktop revision'
    [xml]$project = Get-Content -LiteralPath (Join-Path $repo 'src/Vmd2Anim.Neo/Vmd2Anim.Neo.csproj') -Raw
    @{ coreRevision = $revision; coreRepository = 'https://github.com/Vera-Firefly/VMD-Converter-Core'; desktopRevision = $desktopRevision; desktopVersion = [string]$project.Project.PropertyGroup.Version; selfContained = -not [bool]$FrameworkDependent; runtime = 'win-x64' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'build-manifest.json') -Encoding utf8
    & (Join-Path $destination 'Core/VmdConverterCore.exe') --version
    Assert-Success 'Published core check'
    Write-Host "Ready: $destination/VMD2Anim-Neo.exe"
}
finally { Pop-Location }
