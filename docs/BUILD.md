# Build and packaging

## Requirements

- Windows x64
- .NET SDK 10
- Visual Studio 2022 or later C++ x64 build tools, including MSVC and the Windows SDK
- Git

## Get the source

```powershell
git clone --recurse-submodules https://github.com/Vera-Firefly/VMD2Anim-Neo.git
cd VMD2Anim-Neo
```

For an existing clone:

```powershell
git submodule update --init --recursive
```

`external/VMD-Converter-Core` is a Git submodule pinned to a specific revision. Initialize it recursively to retrieve Core and Unity-HPS.

## Build the complete app

From the repository root:

```powershell
./scripts/Build.ps1
```

The script runs the desktop bridge checks, builds the native FBX reader and HPS adapter, publishes Core and the desktop app, and verifies that the packaged Core starts. The default output is `artifacts/desktop/VMD2Anim-Neo.exe`, with the .NET runtime included.

The output folder must be empty. For a subsequent build, choose another folder:

```powershell
./scripts/Build.ps1 -OutputDirectory artifacts/desktop-next
```

For a smaller package that requires .NET 10 Desktop Runtime on the user's machine:

```powershell
./scripts/Build.ps1 -OutputDirectory artifacts/desktop-framework -FrameworkDependent
```

Distribute the entire output folder, including `Core`, both READMEs, `LICENSE`, dependency licenses and notices, and `build-manifest.json`. The manifest records the app version, app commit, Core commit and runtime type.

## Package

```powershell
./scripts/Package.ps1
# Package a different build:
./scripts/Package.ps1 -InputDirectory artifacts/desktop-next -OutputDirectory artifacts/package-next
```

The script verifies required files and writes `VMD2Anim-Neo-win-x64.zip` with a SHA-256 checksum. The package output folder must also be empty.

## Development and testing

After a full build, run the UI with:

```powershell
dotnet run --project src/Vmd2Anim.Neo
```

Run bridge checks separately:

```powershell
dotnet run --project tests/Vmd2Anim.Neo.Tests -c Release
```

Run short conversion checks using your own model and motion:

```powershell
dotnet run --project tests/Vmd2Anim.Neo.Tests -c Release -- --integration <VmdConverterCore.exe> <source.pmx> <motion.vmd> [target.fbx]
```

## GitHub Actions

`.github/workflows/windows.yml` tests, builds and packages on pushes and pull requests to `main`, and on manual dispatch. Successful builds provide the `VMD2Anim-Neo-win-x64` artifact for 14 days.

A `v*` tag triggers a GitHub Release after the build succeeds, uploading the ZIP and SHA-256 file. The tag must match the app version. Tags containing a hyphen, such as `v1.0.0-rc.1`, create prereleases. Release notes come from `docs/releases/<tag>.md` when present. Rerunning a tag build updates attachments with the same names.

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The workflow recursively checks out the pinned Core submodule. Only the release job has permission to write releases.
