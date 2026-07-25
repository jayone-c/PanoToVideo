# 全景图转短视频（PanoToVideo）

Windows 桌面端工具：将符合 2:1 比例的等距柱状全景图批量转换为横屏或竖屏 MP4 视频。程序提供镜头旋转、起始方位、重点区域慢转、导出前预览，以及硬件编码与 CPU 回退。

## 功能概览

- 批量添加、移除、清空队列；入队时校验文件可读性、尺寸和 2:1 全景比例。
- 支持横屏、竖屏，及 1080P、2K、720P 与自定义偶数分辨率。
- 常用镜头设置：视频时长、旋转方向、质量预设；高级设置中可调整 FOV、俯仰、起始方位、FPS 等参数。
- 支持“重点区域慢转”：在指定时间平滑减速、维持慢转、再平滑恢复，成片总旋转角度保持不变。
- 提供静态时间轴预览与低分辨率播放预览；从 0 秒播放时会先绘制起始方位画面。
- 优先使用可用的 GPU 硬件编码器（NVENC / AMF / QSV）；不可用或运行失败时明确提示并回退至 CPU `libx264`。
- 检测 FFmpeg 是否可用；缺失时在界面中说明安装原因和处理方式。
- 设置保存在 `%LOCALAPPDATA%\PanoToVideo\AppSettings.json`，不会写入仓库。

## 运行要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10/11 x64 |
| 运行包 | 发布的单文件 EXE 已包含 .NET 运行时 |
| 编码工具 | 需安装 [FFmpeg](https://ffmpeg.org/download.html)，并将 `ffmpeg.exe` 所在目录加入系统 `PATH` |
| GPU（可选） | 支持 NVENC、AMF 或 QSV 的显卡及对应驱动；无可用硬件时自动使用 CPU |

启动后如果显示“未检测到 FFmpeg”，安装 FFmpeg 并配置 `PATH`，完全关闭程序后重新打开即可。

## 输入图像规格

| 项目 | 要求 |
| --- | --- |
| 格式 | 可由 Windows 图像解码器读取的图片，例如 JPG、PNG |
| 宽高比 | 2:1，允许误差 ±1%（1.980–2.020） |
| 最小尺寸 | 5000 × 2500 像素 |
| 最大宽度 | 16384 像素 |

不符合要求的图片不会加入队列，界面会显示实际尺寸、比例或解码失败原因。

## 使用方法

1. 点击“添加图片”，选择一张或多张全景图。
2. 在左侧设置时长、旋转方向、输出方向、分辨率和质量预设；需要时展开“高级镜头与性能设置”。
3. 选中队列项，在预览区查看指定时间点画面。拖动“起始方位”会同时改变导出和预览的起始位置。
4. 在右侧确认编码方式、文件名、保存位置和预计体积，点击“开始导出”。
5. 导出完成后使用“打开”进入输出目录。

默认输出目录与首张原图所在目录一致；也可以在“导出目录”卡片中单独选择。

## 开发环境与构建

需要 .NET 8 SDK。解决方案位于 `src/PanoToVideo.sln`。

```powershell
dotnet restore src/PanoToVideo.sln
dotnet build src/PanoToVideo.App/PanoToVideo.App.csproj -c Debug
dotnet test src/PanoToVideo.Tests/PanoToVideo.Tests.csproj -c Debug
```

生成 Windows x64 自包含单文件客户端：

```powershell
dotnet publish src/PanoToVideo.App/PanoToVideo.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeAllContentForSelfExtract=true `
  -p:PublishReadyToRun=true `
  -p:DebugType=None -p:PublishDebugSymbols=false `
  -o dist/pano-to-video-win-x64
```

发布目录 `dist/` 已被 Git 忽略，应通过 Releases、网盘或其他制品渠道分发，而非提交到源代码仓库。

## 项目结构

```text
src/
  PanoToVideo.App/       WPF 界面、交互与设置持久化
  PanoToVideo.Core/      参数、校验、队列、调度与纯逻辑
  PanoToVideo.Render/    D3D11 投影、GPU 探测与 FFmpeg 导出执行器
  PanoToVideo.Tests/     xUnit 自动化测试
  PanoToVideo.sln        Visual Studio / .NET 解决方案
docs/adr/                架构决策记录
```

## 架构说明

全景图投影和镜头方位由 `PanoToVideo.Core` 中的共享计算逻辑生成，静态预览、播放预览和正式导出使用同一套方位调度。编码优先选择已探测到的硬件编码能力；CPU 回退会在界面和任务日志中明确标识。

更多技术取舍见：

- [ADR 0001：技术栈与零拷贝路径](docs/adr/0001-技术栈与零拷贝路径.md)
- [ADR 0002：设备选择与回退策略](docs/adr/0002-设备选择与回退策略.md)

## 提交前检查

```powershell
git status
dotnet test src/PanoToVideo.Tests/PanoToVideo.Tests.csproj --no-restore --disable-build-servers -v:minimal
git diff --check
```

请勿提交 `dist/`、`bin/`、`obj/`、本地照片、测试导出视频或 `%LOCALAPPDATA%\PanoToVideo` 中的个人设置。
