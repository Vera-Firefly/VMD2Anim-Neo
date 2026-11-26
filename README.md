# VMD2Anim-Neo

English · [简体中文](README.zh-CN.md)

A Windows desktop converter for MMD VMD motions to Unity `.anim` files. Supports batch conversion, drag and drop, Humanoid and FK output, and independent output modes, Facial files and source IK settings for each motion. Unity does not need to be open during conversion.

## Download and run

Download `VMD2Anim-Neo-win-x64.zip` from [Releases](https://github.com/Vera-Firefly/VMD2Anim-Neo/releases), extract the entire archive, and run `VMD2Anim-Neo.exe`. Keep the `Core` folder and license files beside the app. Do not run it inside the ZIP.

- Windows x64. The default release package includes the .NET runtime.
- Acrylic is available on Windows 11 22H2 and later when system transparency effects are enabled. Other systems use a light background.
- If no release is available, download the `VMD2Anim-Neo-win-x64` artifact from a successful [Actions build](https://github.com/Vera-Firefly/VMD2Anim-Neo/actions/workflows/windows.yml). Extract the artifact, then extract the app ZIP inside it.

## Convert motions

1. Choose a source **PMX** model. Its skeleton and IK constraints are required.
2. Click **Add VMD**, or drop one or more VMD files into the motion queue. Dropping a PMX also sets the source model.
3. Choose the target skeleton and default animation output mode. Choose **Source PMX skeleton** if you have no target model.
4. Set the output folder, sample rate and position scale, then click **Convert**.
5. Click **Open output folder**, import the generated `.anim` files into Unity, and assign them to the matching character's Animator.

Successful motions leave the queue automatically. Failed, cancelled and unprocessed motions stay for retry. Open **Conversion log** to review results and errors.

Existing output files are preserved: duplicate names receive a numeric suffix. **Cancel** stops the current conversion and retains completed results. Select motions and click **Remove selected** to remove them from the queue; the pending total updates immediately.

## Target skeletons and output modes

| Target | File and purpose |
| --- | --- |
| Source PMX skeleton | No target file required. Produces Humanoid · Source IK animation. |
| Target FBX model | Select an `.fbx`. Keep its Unity `.meta` alongside it, if available, to retain bone mapping and import settings. |
| HPS calibrated rig | Select the HPS rig JSON exported from Unity to use the target Avatar's calibration. |
| Skeleton descriptor | Select a skeleton descriptor JSON exported by VMD-Converter-Core. |

With a target selected, Humanoid and FK modes offer Normal, Legs, Arms and Full body fitting. Humanoid output is intended for the matching Humanoid character. FK curves bind to target bone paths, so the playback character must keep the matching hierarchy and names. Fitting modes still produce full-body motion.

## Per-motion settings

Expand **Motion settings** in a queue row:

- **Animation output**: choose an independent mode, or **Follow global settings** to use the output mode on the right. Without a target, only source Humanoid output is available. Removing the target resets incompatible overrides to the global default.
- **Facial / Expressions**: choose, paste or drop a separate Facial VMD. **Clear** restores the default. **Settings → Advanced parameters → Default Facial VMD** applies when no per-motion file is set. Leave both blank to use expressions from the motion itself. Enable **Bake expressions / BlendShape** to export expression curves.
- **Source IK**: after choosing the PMX, click **Read source IK**. Each bone can **Follow VMD**, **Force on** or **Force off**. **All source IK** changes all bones for this motion. Other motions keep their own settings. Changing PMX requires reading IK and configuring overrides again.

## Settings

Open **Settings** in the left navigation to adjust language, expression baking, advanced parameters and the conversion Core.

The interface defaults to **System default**: Chinese Windows display languages use Simplified Chinese; other languages use English. You can choose English or Simplified Chinese manually. Switching takes effect immediately. Click **Save settings** to save, or close the app or start a conversion to save automatically.

The default conversion values are a useful starting point. Advanced parameters include:

- **Coordinate conversion / Position scale**: animation orientation and movement scale. Position scale remains in the workspace.
- **Duration**: convert only the specified number of seconds from the start; blank converts the full motion.
- **Solver passes / Frame threads / Pose cache budget**: solving cost and resource usage.
- **Upper-body tangent error**: controls upper-body curve interpolation; `0` uses linear interpolation.
- **Bake expressions / BlendShape**: export expression curves, optionally using a separate Facial VMD and morph-map JSON.
- **Maintain FK rotation continuity**: continuity processing for FK rotations.

Under **Conversion Core**, select a complete Core runtime or copy a PowerShell conversion command for the selected motion. The copied command uses the original output name; check for existing files before running it manually.

## Troubleshooting

**Configure Core or missing runtime libraries**

Extract the complete app package again and check for the `Core` folder. Alternatively, choose `VmdConverterCore.exe` under **Settings → Conversion Core**. Its Shared, Hps, UnityHps.Core and UnityHps.Fbx libraries must be in the same folder.

**Unexpected scale, orientation or bone motion**

Check that PMX and VMD match, then review target type, position scale and coordinate conversion. Keep Unity `.meta` files with target FBX models. Use an HPS rig when Avatar calibration is needed. Try a short duration before converting the full motion.

**The queue is empty after reopening**

Only settings are saved, not the motion queue. Settings are stored at `%LOCALAPPDATA%\VMD2Anim-Neo\settings.json`.

## About and license

The **About** page provides the app version, Core revision, user guide, project links and license. VMD2Anim Neo uses [Apache-2.0](LICENSE). The app package includes licenses and notices for Core, Unity-HPS and ufbx.

## Build from source

See [Build and packaging](docs/BUILD.md). The conversion engine is linked through the [VMD-Converter-Core](https://github.com/Vera-Firefly/VMD-Converter-Core) Git submodule.
