# 代码审查综合报告

- 审查日期：2026-07-22
- 审查范围：全代码库（Core / Render / App / Benchmark 跨模块）
- 审查方式：4 路并行 subagent（Core / Render GPU / App UI / 集成并发）+ 工程级自查
- 去重合并后共 **44 项真实问题**（不含风格/格式）

---

## 一、严重度分布

| 严重度 | 数量 | 含义 |
|---|---|---|
| 🔴 高 | 13 | 崩溃/数据损坏/资源泄漏/合规违规，必须修 |
| 🟠 中 | 21 | 潜在错误/性能/边界，应修 |
| 🟡 低 | 10 | 健壮性/死代码/可诊断性，择机修 |

---

## 二、🔴 高严重度（13 项）

### 资源泄漏 / 崩溃

**H1. FFmpeg 子进程取消/异常时未 Kill，进程泄漏+临时文件残留**
`Render/Exporting/FfmpegNvencExecutor.cs:109-112`
取消传播链通，但 `finally` 仅关 stdin，未 `ff.Kill()`/`WaitForExit()`。FFmpeg 持有 tmpPath 文件句柄 finalize 时，`Cleanup` 删文件失败被静默吞，`.tmp.mp4` 残留；若 FFmpeg 卡住则进程永久存活。

**H2. FFmpeg stdin/stderr 同步读写死锁**
`Render/Exporting/FfmpegNvencExecutor.cs:97,106`
循环内同步写 stdin，循环后才 `ReadToEnd()` 读 stderr。stderr 缓冲写满后 FFmpeg 阻塞写 stderr -> 不读 stdin -> 我们写 stdin 阻塞 = 管道死锁。一旦触发导出永久挂死，取消无法打断当前帧 Write。

**H3. ERP 纹理泄漏（UploadErpTexture 漏 Dispose 纹理）**
`Render/D3D11/EquirectPipeline.cs:139-140`
`CreateTexture2D` 返回的纹理未 `using`，SRV 只持内部引用，纹理 C# 包装器等 GC Finalizer 才释放。8192×4096 ERP ≈128MB GPU 内存非确定占用。

**H4. MfH264Encoder 构造异常路径 MFStartup/Shutdown 不配对**
`Render/Encoding/MfH264Encoder.cs:38-75`
构造函数 `MFStartup` 后任何 COM 调用抛异常，构造未完成，`using` 不触发 Dispose，`MFShutdown` 永不执行，MF 引用计数永久 +1。

**H5. SubmitFrame 的 buffer/sample COM 包装器泄漏**
`Render/Encoding/MfH264Encoder.cs:83-91`
`MFCreateDXGISurfaceBuffer`/`MFCreateSample` 返回对象 WriteSample 后未 Dispose，每帧泄漏 2 个 COM 包装器，100 帧视频泄漏 200 个。

**H6. MfDeviceProbe 三处 COM 泄漏**
`Render/DeviceProbe/MfDeviceProbe.cs:23-27,59-69,87-88`
- ProbeDeviceMount 的 `dxgiManager` 未 Dispose
- EnumerateH264HardwareEncoders 的 `IMFActivate*` 数组只 FreeCoTaskMem 内存，每个 Activate COM 引用未 Release
- ProbeSurfaceBuffer 的 `buffer` 未 Dispose
每次探测泄漏多个 COM 对象。

**H7. DeviceProbe.TryProbeEncoder 的 dxgiManager 未 Dispose**
`Render/DeviceProbe/DeviceProbe.cs:74-75`
每探测一个适配器泄漏一个 `IMFDXGIDeviceManager`，N 个适配器泄漏 N 个。

### 像素正确性 / 崩溃

**H8. asteroid shader 中心像素 normalize 零向量产生 NaN**
`Render/D3D11/Shaders/asteroid.hlsl:71`
画面正中 `dirPersp=(0,0,1)` 与 `dirAst=(0,0,-1)` 在 weight=0.5 时 `lerp=(0,0,0)`，`normalize` 产生 NaN，中心区域 NaN 扩散。任何 `0<weight<1` 在中心带数值失稳。**这是当前小行星过渡的潜在崩溃/黑块源。**

**H9. RetrySelected 是 async void 无 catch，异常致崩溃**
`App/MainViewModel.cs:205-221`
`async void` 由同步 RelayCommand 调用，无 catch。`RetryAsync` 抛异常（UNC 路径 DriveInfo ArgumentException、_scheduler null NRE）触发 `DispatcherUnhandledException` 默认崩溃。与有 catch 的 ExportAsync 处理不对称。

**H10. 关窗不取消进行中导出，后台 Task 继续访问已 Dispose 的 _cts**
`App/MainWindow.xaml.cs:17` + `App/MainViewModel.cs:252`
`Dispose()` 仅 `_cts?.Dispose()`。导出中关窗：后台 Task.Run 仍跑、ffmpeg 继续；`_cts` Dispose 后 executor 访问 linked token 抛 `ObjectDisposedException`。

**H11. 进度条永远为 0（数据绑定 BUG）**
`App/MainViewModel.cs:78-79` + `MainWindow.xaml:98`
`ProgressPercent` 只在 `ProgressFraction` setter 刷新，但 ExportAsync/RetrySelected 从不赋值 ProgressFraction，恒 0。实时进度在 QueueItem.Progress，VM 未桥接。**导出全程进度条纹丝不动，用户可见功能缺陷。**

### 合规

**H12. FFmpeg GPL 许可不完整（GPL 合规违规）**
`dist/LICENSE-ffmpeg.txt`
仅 13 行 `ffmpeg -L` 简短声明，非完整 GPL v3 文本。GPL 要求随附完整许可，当前发布违反。

**H13. AtomicMove 非原子：先 Delete 后 Move，中间崩溃丢数据**
`Render/Exporting/FfmpegNvencExecutor.cs:156-157` + `GpuExportExecutor.cs:139-140`
方法名 AtomicMove 但实现 `File.Delete(finalPath); File.Move(tmp,final)`。Delete 后 Move 前崩溃，finalPath 丢失、tmpPath 残留。

---

## 三、🟠 中严重度（21 项）

### 像素正确性（当前被掩盖，修 H 时会暴露）

**M1. NV12 UV 视口宽度 2 倍过大**
`EquirectPipeline.cs:317` - `Viewport(outW, outH/2)`，但 NV12 UV plane 是 `(outW/2, outH/2)`（4:2:0 半宽半高）。光栅化右半像素越界丢弃，浪费 50% UV 着色。当前因 RTV 裁剪未造成可见错误，但修对后 M2 会暴露。

**M2. colorconvert 共用 Wrap sampler 致右边缘色度串色**
`EquirectPipeline.cs:62-70` + `colorconvert.hlsl:60-66` - PSUv 对 BGRA（非 ERP）用 AddressU=Wrap 采样，右边缘 2×2 块从左边缘环绕取色。当前被 M1 视口过宽掩盖，修 M1 后暴露。

**M3. equirect shader asin 未钳制可能 NaN**
`equirect.hlsl:70` - `asin(wy/len)`，浮点误差使 wy/len 微超 ±1（pitch=±90°）返回 NaN，扩散成噪点帧。

### 资源 / 生命周期

**M4. ConvertBgraToYuv 双 RTV 绑定后未解绑**
`EquirectPipeline.cs:178,301,316` - OMSetRenderTargets 后旧 RTV context 持有引用，跨调用残留，异常路径可能引用已释放纹理。

**M5. GpuExportExecutor 纹理池在 Finalize 前 Dispose，可能访问违规**
`GpuExportExecutor.cs:112-113` - 先 Dispose nv12Pool 再 Finalize，若 MF flush 引用已 Dispose 纹理则访问违规。应 Finalize 后再 Dispose。

**M6. MfH264Encoder 构造本地 COM 对象未 Dispose**
`MfH264Encoder.cs:41,45,55,65` - dxgiManager/sinkAttrs/outType/inType 未 `using`。

**M7. MfHevcEncoderProbe 多次 IsAvailable 致 MFStartup/Shutdown 不平衡**
`MfHevcEncoderProbe.cs:18-19,49-53` - 每次 IsAvailable 调 MFStartup 但 Dispose 只 MFShutdown 一次，多次调用 MF 引用计数失衡。

**M8. DeviceProbe 异常路径 MFStartup 无配对 + candidates 清理**
`DeviceProbe.cs:22-23,48-50` - Probe 无 try-catch，异常上抛时 _mfStarted 已 true 但 Dispose 未调用；QueryInterface 收集的 candidates 中途异常无人清理。

**M9. FFmpeg Process 对象未 Dispose，OS 句柄泄漏**
`FfmpegNvencExecutor.cs:74` - 批量 100 项 × 重试累积句柄。

**M10. CachedDeviceProbe Dispose 不释放缓存 COM 适配器**
`CachedDeviceProbe.cs:28-36` - Dispose 形同虚设，Adapter COM 依赖进程退出。

### 并发 / 状态

**M11. _currentTaskCts 并发访问竞态**
`Core/Queue/SerialBatchScheduler.cs:39,95,144-145` - 后台线程 Dispose/置 null，UI 线程 CancelCurrent 读，无锁无 volatile。Dispose 后读抛 ObjectDisposedException。当前 UI 用 _cts.Cancel 未触发，但 CancelCurrent 是 public。

**M12. RetrySelected 复用旧 _scheduler 闭包，参数不一致**
`App/MainViewModel.cs:174-180,215` - 闭包捕获首次 presetResult.Preset 快照，用户改参数后重试用旧参数，UI 显示新参数，预期不一致。且 BrowseFiles 改 SelectedFiles 后 IndexOf 可能 -1 致越界。

### 性能

**M13. 每张图被完整解码两次**
`App/MainViewModel.cs:166-171` + `176-180` - 建项 DecodeImage 取 w/h 后丢弃 rgba，erpLoader 再次解码。8K 图每次 1-3s，100 张浪费 100-300s。

**M14. DecodeImage/设备探测在 UI 线程同步阻塞**
`App/MainViewModel.cs:151-156,165-171` - probe.Probe() + 循环 DecodeImage 在 await Task.Run 之前的 UI 线程，多图界面冻结。

### 错误恢复 / 文档

**M15. RetrySelected 硬编码 ExportPreset.Compatibility**
`App/MainViewModel.cs:215` - 重试写死 H.264，忽略用户 H.265 预设，且 precheck 与 executor 码率口径不一致，违反 PRD#4。

**M16. App.xaml.cs 无全局异常处理**
`App/App.xaml.cs:10-12` - 未注册 DispatcherUnhandledException/UnobservedTaskException，async void 异常直接崩溃。

**M17. 导出中参数仍可编辑**
`MainWindow.xaml:49-67` - 无 IsEnabled 绑定 IsExporting，导出中改参数触发 SaveSettings + 全量刷新，重试参数不一致。

**M18. 临时诊断产物误入库（约 7MB）**
`asteroid_vis/*.png` + `tests/diagnose/*.png|*.log` - 应 gitignore + git rm --cached。

**M19. System.Drawing.Common 10.0.10 版本超前**
全项目 - .NET 8 配套应为 8.x，10.x 疑预览版，跨版本兼容风险。

**M20. 测试覆盖盲区**
FfmpegNvencExecutor / DeviceProbe 真实探测 / MfH264Encoder 三个生产关键路径无单测，仅 SmokeProbe 集成。

**M21. CONTEXT.md 严重滞后**
`CONTEXT.md:10-16` - 仍写"阶段0进行中""硬件编码 MF 零拷贝待探针"，实际已改 FFmpeg h264_nvenc 主路径，文档与实现不一致。

---

## 四、🟡 低严重度（10 项）

- **L1.** `equirect.hlsl:47-48` - g_outW==1 时除零（实际不会出现 1 像素宽，加 max 兜底）
- **L2.** `asteroid.hlsl:55` - asteroidDir 经度未归一化到 [0,2π)，三角函数吸收，轻微
- **L3.** `EquirectPipeline.cs:36` - `_device1` QueryInterface 后从未使用，死代码
- **L4.** `MfH264Encoder.cs:93` - `Finalize` 方法名遮蔽 Object.Finalize，重命名 Finish
- **L5.** `SerialBatchScheduler.cs:33-39` - Pause/Resume/CancelCurrent 是 UI 未调用的死代码
- **L6.** `SerialBatchScheduler.cs:110-113` - 重名检测 Directory.GetFiles 存在 TOCTOU 竞态
- **L7.** `MainViewModel.cs:195-198` - ExportAsync catch 吞异常堆栈，仅存 Message，无日志
- **L8.** `MainViewModel.cs:145,211` - _cts 重复创建未 Dispose 旧的
- **L9.** `MainWindow.xaml:68` - SeamlessHint 死代码绑定（Visibility=Collapsed）
- **L10.** `MainViewModel.cs:100` - SetParam 用 OnPropertyChanged(string.Empty) 全量刷新，输入卡顿

---

## 五、关键数据流确认（链路通但终端有缺陷）

**取消传播**：UI `_cts.Cancel` -> linked `_currentTaskCts` -> executor `ThrowIfCancellationRequested` -> orchestrator catch -> Cleanup。链路通，但终端 FFmpeg 未 Kill（H1）、Cleanup 删占用文件失败被吞。

**错误传播**：executor 异常 -> catch 压缩为 `{Type}: {Message}` -> orchestrator -> QueueItem.SetError。链路通，但丢失堆栈（L7）。

**临时文件**：正常路径 OK；异常/取消路径因 H1 FFmpeg 未退出致 Cleanup 失败、tmpPath 残留。

---

## 六、修复优先级建议

**第一优先（崩溃/数据/合规，必须修）**：
- H1+H2+M9（FFmpeg 进程 Kill + 异步读 stderr 避死锁 + Dispose）
- H8（asteroid normalize 零向量）
- H9+H10（async void 加 catch + 关窗先 Cancel 再 Dispose）
- H11（进度条桥接）
- H12（补完整 GPL v3 许可）
- H13（AtomicMove 用 File.Move(overwrite:true)）

**第二优先（资源泄漏，批量场景累积）**：
- H3+H5+H6+H7（COM 资源 using/Dispose）
- H4+M7+M8（MFStartup/Shutdown try-catch 配对）
- M5（纹理池 Finalize 后释放）

**第三优先（像素正确性，当前被掩盖）**：
- M1+M2（UV 视口半宽 + 独立 Clamp sampler，必须联动修）
- M3（asin 钳制）

**第四优先（性能/状态/文档）**：
- M13+M14（解码缓存 + 后台线程）
- M15+M17（重试预设一致 + 导出中禁编辑）
- M21（更新 CONTEXT.md）
- M18（gitignore 临时产物）

---

## 七、总体评价

- **核心管线正确性已验证**（433FPS、几何 PSNR、NV12 修复后画面正常），但**资源管理是系统性短板**：COM 对象/D3D11 纹理/FFmpeg 进程/MF 引用计数多处非确定性释放，单次导出可工作，**批量+异常+取消场景下会累积泄漏或崩溃**。
- **UI 层是风险集中区**：async void、UI 线程阻塞、进度不刷新、关窗失控、重试状态不一致，5 项高中有 4 项在 App 层。
- **像素正确性有两处被掩盖的隐患**（M1 视口 + M2 sampler），当前因视口过宽巧合未显现，修任一处会暴露另一处，必须联动修。
- **合规风险**：FFmpeg GPL 许可不完整，发布前必须补。
