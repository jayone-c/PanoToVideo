# 全景图转短视频工具 - 上下文

## 是什么
本地 Windows 桌面工具，将 2:1 等距柱状全景图（ERP）批量转换为普通透视镜头 MP4。不是剪辑软件。
上游 PRD：`全景图转短视频工具-PRD.md`；执行规划：`全景图转短视频工具-开发规划.md`。

## 技术栈（渐进路线，规划 §1.1）
- UI：WPF（.NET 8）
- GPU 投影：C# + Vortice.Windows（D3D11 + HLSL）
- 硬件编码：Media Foundation（零拷贝可行性待阶段0探针）
- CPU 回退：FFmpeg 子进程（v360 + 软/硬编码）

## 当前阶段
- **阶段0（进行中）**：工程地基 + Core 层 TDD。
- 阶段0 GPU 零拷贝探针：独立会话推进（产出 ADR 0001）。
- 阶段1-4：Core 完成后按规划推进。

## 工程结构（规划 §2）
依赖方向：`App -> Core <- Render`；`Tests -> Core`。**Core 不引用 Render/App**，保证领域逻辑可单测。

```
src/
├── PanoToVideo.sln
├── PanoToVideo.Core/    net8.0 纯类库（领域逻辑，本轮实质开发）
├── PanoToVideo.App/     net8.0-windows WPF（空壳，待 UI）
├── PanoToVideo.Render/  net8.0-windows（GPU 渲染+编码，待探针）
└── PanoToVideo.Tests/   net8.0 xUnit（本轮实质开发）
```

## 关键不变式（规划 §1.3，PRD #1）
渲染、颜色转换与编码纹理共享同一 D3D11 设备和 DXGI Adapter LUID，全程不经 CPU 回读。
任何"下载到 staging 纹理再上传"或跨适配器复制的实现都视为破坏 PRD #1。

## TDD 策略
- Core 层纯逻辑用红-绿-重构驱动（本轮）。
- 投影数学以 py360convert 为对照基准（PSNR ≥ 40dB）。
- GPU 探针与 GPU 结果对照属验证性实验/验收测试，不走单元 TDD。

## 决策记录
- `docs/adr/0001-技术栈与零拷贝路径.md`（待探针）
- `docs/adr/0002-设备选择与回退策略.md`（待设备探测实现）
