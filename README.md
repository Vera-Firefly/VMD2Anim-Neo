# VMD2Anim-Neo

Windows 图形化 VMD → Unity `.anim` 转换工具，支持批量转换、拖放文件、Humanoid / FK 输出，以及每个动作独立的源 IK 设置。转换时无需打开 Unity。

## 下载与运行

在 [Releases](https://github.com/Vera-Firefly/VMD2Anim-Neo/releases) 下载 `VMD2Anim-Neo-win-x64.zip`，完整解压后运行 `VMD2Anim-Neo.exe`。请保留同目录的 `Core` 文件夹及许可证文件，不要在压缩包内直接运行。

- 支持 Windows x64；正式发布包自带 .NET 运行时，无需另装 .NET。
- Windows 11 22H2 及以上开启系统透明效果后显示亚克力背景；其他环境使用浅色背景。
- 尚无 Release 时，可在 [Actions](https://github.com/Vera-Firefly/VMD2Anim-Neo/actions/workflows/windows.yml) 的成功构建中下载 `VMD2Anim-Neo-win-x64` 产物。解开产物后，再解压其中的程序 ZIP。

## 转换动作

1. 选择源 **PMX** 模型。PMX 提供源骨架与 IK 约束，是转换的必需文件。
2. 点击 **添加 VMD**，或把一个或多个 VMD 拖入动作队列。拖入 PMX 也可设置源模型。
3. 选择目标骨架与动画输出模式。没有目标模型时，选择 **使用源 PMX 骨架**。
4. 设置输出目录、采样率和位置缩放，点击 **开始转换**。
5. 完成后点击 **打开输出目录**，将生成的 `.anim` 导入 Unity，并配置到对应角色的 Animator。

成功的动作自动移出队列，全部成功后列表清空。失败、取消和未处理的动作保留，便于重试。转换结果与错误详情可在 **运行日志** 中查看。

同名输出自动编号，保留已有文件。点击 **取消** 会停止当前转换，已完成的结果继续保留。选中队列动作后可点击 **移除所选**，下方待转换总数随之更新。

## 目标骨架与输出模式

| 目标 | 文件与用途 |
| --- | --- |
| 使用源 PMX 骨架 | 无需目标文件，输出 Humanoid · 源 IK 动画。 |
| 目标 FBX 模型 | 选择 `.fbx`；如有 Unity `.meta`，请保留在 FBX 旁边，以便读取骨骼映射与导入设置。 |
| HPS 标定 Rig | 选择从 Unity 导出的 HPS rig JSON，使用目标 Avatar 的标定信息。 |
| 骨架 Descriptor | 选择由 VMD-Converter-Core 导出的骨架描述 JSON。 |

选择目标骨架后，可使用 Humanoid 或 FK 的正常、下肢、上肢、全身拟合模式。Humanoid 输出用于对应的 Humanoid 角色；FK 曲线绑定目标骨骼路径，播放对象需保留对应层级和命名。拟合模式仍输出全身动作。

## 每个动作独立设置 IK

选择 PMX 后点击 **读取源 IK**，再展开队列中动作的 **源 IK**：

- **跟随 VMD**：使用动作文件中的 IK 开关。
- **强制开启**：在该动作中启用指定源 IK。
- **强制关闭**：在该动作中禁用指定源 IK。

**全部源 IK** 可批量设置当前动作，也可逐个骨骼调整。不同动作的设置互不影响。更换 PMX 后需要重新读取 IK，并重新设置覆盖。

## 高级设置

通常可先使用默认值。需要时展开 **高级参数**：

- **坐标转换 / 位置缩放**：调整动画朝向和位移比例。
- **限定时长**：只转换开头指定秒数；留空转换完整动作。
- **求解轮数 / 帧线程数 / 姿态缓存预算**：调整求解成本与资源占用。
- **上肢插值误差**：控制上肢曲线插值；设为 `0` 使用线性插值。
- **烘焙表情 / BlendShape**：导出表情曲线，可配合附加表情 VMD 与形态键映射 JSON。
- **保持 FK 旋转连续性**：用于 FK 旋转曲线的连续处理。

## 常见问题

**提示“待配置 Core”或缺少运行库**

重新完整解压发布包，确认程序旁存在 `Core` 文件夹。也可展开 **转换核心**，选择完整运行目录中的 `VmdConverterCore.exe`，其 Shared、Hps、UnityHps.Core、UnityHps.Fbx 库需放在同一目录。

**动画比例、朝向或骨骼效果不符合预期**

检查 PMX 与 VMD 是否匹配、目标骨架类型、位置缩放和坐标转换。目标 FBX 可带上 Unity `.meta`；需要 Avatar 标定信息时使用 HPS rig。先设置短的限定时长检查结果，再转换整段动作。

**重新打开后队列为空**

程序仅记忆设置，动作队列不会保存。设置位于 `%LOCALAPPDATA%\VMD2Anim-Neo\settings.json`。

## 从源码构建

参阅 [构建与打包](docs/BUILD.md)。转换核心通过 [VMD-Converter-Core](https://github.com/Vera-Firefly/VMD-Converter-Core) Git submodule 引入。
