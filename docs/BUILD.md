# 构建与打包

## 环境

- Windows x64。
- .NET SDK 10。
- Visual Studio 2022 或更新版本的 C++ x64 构建工具，包含 MSVC 与 Windows SDK。
- Git。

## 获取源码

```powershell
git clone --recurse-submodules https://github.com/Vera-Firefly/VMD2Anim-Neo.git
cd VMD2Anim-Neo
```

已克隆仓库时执行：

```powershell
git submodule update --init --recursive
```

`external/VMD-Converter-Core` 是 Git submodule，依赖版本由仓库中的 gitlink 固定。请使用 submodule 获取核心，不要把核心源码复制到桌面项目。

## 完整构建

在仓库根目录执行：

```powershell
./scripts/Build.ps1
```

脚本依次运行桌面桥接测试、编译原生 FBX 读取库和 HPS 适配器，发布 Core 与桌面程序，并检查 Core 能否运行。默认生成自带 .NET 运行时的 `artifacts/desktop/VMD2Anim-Neo.exe`。

输出目录必须为空。再次构建时指定新的目录：

```powershell
./scripts/Build.ps1 -OutputDirectory artifacts/desktop-next
```

需要由用户自行安装 .NET 10 Desktop Runtime 的精简包时：

```powershell
./scripts/Build.ps1 -OutputDirectory artifacts/desktop-framework -FrameworkDependent
```

程序目录应整体分发，包括 `Core`、许可证和 `build-manifest.json`。清单记录桌面版本、桌面提交、Core 提交及运行时类型。

## 打包

```powershell
./scripts/Package.ps1
# 对其他构建目录打包：
./scripts/Package.ps1 -InputDirectory artifacts/desktop-next -OutputDirectory artifacts/package-next
```

脚本检查必要文件后生成 `VMD2Anim-Neo-win-x64.zip` 与对应的 `.sha256` 校验文件。打包输出目录也必须为空。

## 开发与测试

完成一次完整构建后，开发界面可使用：

```powershell
dotnet run --project src/Vmd2Anim.Neo
```

单独运行桥接测试：

```powershell
dotnet run --project tests/Vmd2Anim.Neo.Tests -c Release
```

使用自己的模型与动作进行短片段集成测试：

```powershell
dotnet run --project tests/Vmd2Anim.Neo.Tests -c Release -- --integration <VmdConverterCore.exe> <source.pmx> <motion.vmd> [target.fbx]
```

## GitHub Actions

`.github/workflows/windows.yml` 在推送到 `main`、向 `main` 提交 PR，以及手动运行时执行完整测试、构建和打包。成功后可从工作流页面下载 `VMD2Anim-Neo-win-x64` 产物，保留 14 天。

推送 `v*` 标签时，同一工作流会在构建成功后创建 GitHub Release，并上传 ZIP 和 SHA-256 文件。带连字符的版本标签（例如 `v0.1.0-rc.1`）发布为预发布版本。重新运行相同标签的工作流会更新该 Release 的同名附件。

```powershell
git tag v0.1.0
git push origin v0.1.0
```

工作流递归检出固定的 Core submodule，无需额外配置发布密钥；仅发布任务拥有写入 Release 的权限。
