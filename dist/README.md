# 全景图转短视频工具

将本地 2:1 等距柱状全景图（ERP）转换为普通透视镜头 MP4，GPU 投影 + 硬件编码。

## 用法

1. 运行 `app/PanoToVideo.App.exe`
2. 选择全景图（JPG/PNG，宽高比 2:1±1%，宽 6000-16384）
3. 设置参数（时长/旋转度数/FPS/FOV/宽高/俯仰角/方向/小行星开场/预设）
4. 点"导出"，完成后自动打开 MP4

输出到源图同目录 `exports/` 下：`{文件名}_{宽}x{高}_{时长}s_{旋转度}deg.mp4`

## 依赖

- **FFmpeg**：需在 PATH 中（h264_nvenc 硬件编码）。本工具依赖 FFmpeg 进行编码，FFmpeg 遵循 GPL v3 许可，见 `LICENSE-ffmpeg.txt`。
- **.NET 8 桌面运行时**（`--self-contained false` 发布，需目标机已装 .NET 8 桌面运行时）
- **NVIDIA GPU + NVENC**（h264_nvenc 编码）；无 GPU 时回退 FFmpeg 软件编码

## 架构

- GPU 投影：Vortice.Direct3D11 + HLSL（equirect->perspective shader，零拷贝渲染）
- 颜色转换：GPU shader BGRA->NV12/Rec.709 limited
- 编码：FFmpeg h264_nvenc（GPU 投影 NV12 帧 -> 管道 -> 硬件编码）
- 设备探测：DXGI 适配器枚举 + MF 编码器激活探测，选真实独显（RTX 4090 D）

详见 `docs/adr/0001-技术栈与零拷贝路径.md`。

## 性能

PRD 基准（7680×3840 -> 1080×1920、30秒、60FPS、360°、75°FOV、H.264）：
RTX 4090 D 实测 **433 FPS**（纯渲染 4.15s），远超 60 FPS 目标。

## 许可

- 本工具代码：见仓库 LICENSE
- FFmpeg：GPL v3（`LICENSE-ffmpeg.txt`）
- Vortice.Windows：MIT
