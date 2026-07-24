using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Naming;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Projection;
using PanoToVideo.Core.Queue;
using PanoToVideo.Core.Settings;
using PanoToVideo.Core.Validation;
using PanoToVideo.Render.Exporting;
using TaskStatus = PanoToVideo.Core.Queue.TaskStatus;

namespace PanoToVideo.App;

/// <summary>
/// 主窗口视图模型（五区UI）：顶部文件输入+输出目录+硬件状态，左参数，中队列，右详情，底部累计。
/// 支持多图批量导出（SerialBatchScheduler）+ 暂停/取消/重试 + 打开输出目录。
/// P0-1：CPU 回退决策（FallbackDecider）；P0-4：入队即校验（QueueIntakeService）；
/// P0-5：按需解码（OnDemandErpLoader）+ 内存预检（MemoryPrecheck）。
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppSettingsStore _settingsStore;
    private AppSettings _settings;
    private string? _outputDir;
    private bool _outputDirChosenByUser;
    private bool _isExporting;
    private double _progressFraction;
    private string _statusText = "就绪";
    private string _deviceInfo = "探测中...";
    private string _encoderInfo = "";
    private string _fallbackHint = "";
    private CancellationTokenSource? _cts;
    private int _selectedQueueIndex;
    private int _completedCount;
    private int _failedCount;
    private double _totalProgressPercent;
    private SerialBatchScheduler? _scheduler;
    private TaskCompletionSource? _resumeGate;
    private bool _isPaused;
    private Task<GpuAvailability>? _gpuAvailabilityTask;
    private ImageSource? _previewImage;
    private string _previewStatus = "添加图片后显示镜头预览";
    private int _previewVersion;
    private readonly int? _autoDetectedCpuCores = TryDetectCpuCores();
    private bool _isCustomResolution;
    private PresetResolveResult? _lastPreset; // H9/M15: 缓存首次预设，重试复用保持一致
    private List<QueueItem> _items = new();
    // P0-5：按需解码路径映射（替代旧 _rgbaCache 预解码字典，100 张不再常驻 RGBA）
    private readonly Dictionary<QueueItem, string> _itemPaths = new();
    // P0-4：入队校验服务 + 图像头读取器
    private readonly QueueIntakeService _intakeService = new();
    private readonly WicImageHeaderReader _headerReader = new();

    public ObservableCollection<QueueItem> QueueItems { get; } = new();
    public ObservableCollection<string> SelectedFiles { get; } = new();

    public MainViewModel()
    {
        _settingsStore = new AppSettingsStore(GetSettingsPath());
        _settings = _settingsStore.Load();
        // 自动检测成功时始终使用本机逻辑核心数，避免旧配置跨设备沿用不合适的线程数。
        if (_autoDetectedCpuCores is > 0)
            _settings = _settings with { RenderParameters = _settings.RenderParameters with { CpuCores = _autoDetectedCpuCores.Value } };
        _isCustomResolution = GetMatchingResolutionPresetIndex(_settings.RenderParameters.Width, _settings.RenderParameters.Height) < 0;
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !IsExporting && QueueItems.Count > 0 && !string.IsNullOrEmpty(OutputDir));
        CancelCommand = new RelayCommand(CancelAll, () => IsExporting);
        RetryCommand = new RelayCommand(RetrySelected, () => !IsExporting && SelectedQueueIndex >= 0 && SelectedQueueIndex < QueueItems.Count && QueueItems[SelectedQueueIndex].Status == TaskStatus.Failed);
        BrowseCommand = new RelayCommand(BrowseFiles);
        BrowseOutputDirCommand = new RelayCommand(BrowseOutputDir);
        OpenOutputDirCommand = new RelayCommand(OpenOutputDir, () => !string.IsNullOrEmpty(OutputDir));
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => !IsExporting && SelectedQueueIndex >= 0 && SelectedQueueIndex < QueueItems.Count);
        ClearQueueCommand = new RelayCommand(ClearQueue, () => !IsExporting && QueueItems.Count > 0);
        // P1：暂停/恢复/取消当前/重试全部失败
        PauseCommand = new RelayCommand(PauseQueue, () => IsExporting && !IsPaused);
        ResumeCommand = new AsyncRelayCommand(ResumeAsync, () => IsExporting && IsPaused);
        CancelCurrentCommand = new RelayCommand(() => _scheduler?.CancelCurrent(), () => IsExporting);
        RetryFailedCommand = new AsyncRelayCommand(RetryAllFailedAsync, () => !IsExporting && QueueItems.Any(i => i.Status == TaskStatus.Failed));
        OutputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PanoToVideo");
        UpdateSeamlessHint();
        _ = RefreshDeviceInfoAsync();
    }

    // 参数绑定
    public int DurationSeconds { get => _settings.RenderParameters.DurationSeconds; set => SetParam(p => p with { DurationSeconds = value }); }
    public int RotationDegrees { get => _settings.RenderParameters.RotationDegrees; set => SetParam(p => p with { RotationDegrees = value }); }
    public int Fps { get => _settings.RenderParameters.Fps; set => SetParam(p => p with { Fps = value }); }
    public double HorizontalFov
    {
        get => _settings.RenderParameters.HorizontalFov;
        set { SetParam(p => p with { HorizontalFov = value }); OnPropertyChanged(nameof(HorizontalFovDegrees)); }
    }
    public int HorizontalFovDegrees { get => (int)Math.Round(HorizontalFov); set => HorizontalFov = value; }
    public int Width
    {
        get => _settings.RenderParameters.Width;
        set => SetCustomDimension(p => p with { Width = value });
    }
    public int Height
    {
        get => _settings.RenderParameters.Height;
        set => SetCustomDimension(p => p with { Height = value });
    }
    public double Pitch
    {
        get => _settings.RenderParameters.Pitch;
        set { SetParam(p => p with { Pitch = value }); OnPropertyChanged(nameof(PitchDegrees)); }
    }
    public int PitchDegrees { get => (int)Math.Round(Pitch); set => Pitch = value; }
    public double StartYaw { get => _settings.RenderParameters.StartYaw; set => SetParam(p => p with { StartYaw = value }); }
    public int CpuCores { get => _settings.RenderParameters.CpuCores; set => SetParam(p => p with { CpuCores = value }); }
    /// <summary>当前设备自动探测到的逻辑 CPU 核心数；CPU 回退默认使用该值。</summary>
    public int? AutoDetectedCpuCores => _autoDetectedCpuCores;
    public bool IsCpuCoresAutoDetected => _autoDetectedCpuCores is > 0;
    public bool IsCpuCoreInputReadOnly => IsCpuCoresAutoDetected;
    public int CpuCoreInputMaximum => _autoDetectedCpuCores is > 0 ? _autoDetectedCpuCores.Value : 128;
    public string CpuCoresHint => IsCpuCoresAutoDetected
        ? $"已自动检测到 {_autoDetectedCpuCores} 个逻辑核心；CPU 回退将自动使用此值。"
        : "无法自动检测 CPU 核心数；请手动指定 1–128 个逻辑核心。";
    public bool AsteroidIntro { get => _settings.RenderParameters.AsteroidIntro; set => SetParam(p => p with { AsteroidIntro = value }); }
    /// <summary>P1：旋转方向 ComboBox 索引（0=顺时针，1=逆时针）。</summary>
    public int DirectionIndex
    {
        get => _settings.RenderParameters.Direction == RotationDirection.Clockwise ? 0 : 1;
        set
        {
            SetParam(p => p with { Direction = value == 0 ? RotationDirection.Clockwise : RotationDirection.Counterclockwise });
            OnPropertyChanged(nameof(PreviewMotionHint));
        }
    }
    public int PresetIndex
    {
        get => (int)_settings.Preset;
        set
        {
            _settings = _settings with { Preset = (ExportPreset)value };
            OnPropertyChanged();
            SaveSettings();
            _ = RefreshDeviceInfoAsync(showProbingState: false);
            OnPropertyChanged(nameof(ExportSummary));
        }
    }

    /// <summary>竖屏输出分辨率：1080P、720P、2K 或自定义。</summary>
    public int ResolutionPresetIndex
    {
        get => _isCustomResolution ? 3 : Math.Max(0, GetMatchingResolutionPresetIndex(Width, Height));
        set
        {
            switch (value)
            {
                case 0:
                    _isCustomResolution = false;
                    SetDimensionsForPreset(0, IsLandscapeOutput);
                    break;
                case 1:
                    _isCustomResolution = false;
                    SetDimensionsForPreset(1, IsLandscapeOutput);
                    break;
                case 2:
                    _isCustomResolution = false;
                    SetDimensionsForPreset(2, IsLandscapeOutput);
                    break;
                default:
                    _isCustomResolution = true;
                    OnPropertyChanged(nameof(ResolutionPresetIndex));
                    OnPropertyChanged(nameof(IsCustomResolution));
                    break;
            }
        }
    }

    public bool IsCustomResolution => _isCustomResolution;

    /// <summary>输出方向：0=横屏，1=竖屏。方向先于分辨率选择，预设会自动套用同规格横/竖尺寸。</summary>
    public int OutputOrientationIndex
    {
        get => IsLandscapeOutput ? 0 : 1;
        set
        {
            var landscape = value == 0;
            if (landscape == IsLandscapeOutput) return;

            if (_isCustomResolution)
            {
                SetDimensions(Height, Width);
                return;
            }

            SetDimensionsForPreset(ResolutionPresetIndex, landscape);
        }
    }

    public bool IsLandscapeOutput => Width > Height;
    /// <summary>P1：导出后打开输出（单图开 MP4，批量开目录）。</summary>
    public bool OpenAfterExport
    {
        get => _settings.OpenAfterExport;
        set { _settings = _settings with { OpenAfterExport = value }; OnPropertyChanged(); SaveSettings(); }
    }
    /// <summary>P1：是否记忆配置（关闭时每次修改不写文件）。</summary>
    public bool RememberSettings
    {
        get => _settings.RememberSettings;
        set { _settings = _settings with { RememberSettings = value }; OnPropertyChanged(); SaveSettings(); }
    }

    /// <summary>P1 修复：顶部文件输入显示（首个文件名或计数）。</summary>
    public string? SelectedFilePath =>
        QueueItems.Count == 0 ? null :
        QueueItems.Count == 1 ? QueueItems[0].SourceFileName :
        $"已选 {QueueItems.Count} 个文件";

    public string? OutputDir { get => _outputDir; set { _outputDir = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExportSummary)); ExportCommand.RaiseCanExecuteChanged(); OpenOutputDirCommand.RaiseCanExecuteChanged(); } }
    public bool IsExporting { get => _isExporting; set { _isExporting = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotExporting)); ExportCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); PauseCommand.RaiseCanExecuteChanged(); ResumeCommand.RaiseCanExecuteChanged(); CancelCurrentCommand.RaiseCanExecuteChanged(); RetryFailedCommand.RaiseCanExecuteChanged(); RemoveSelectedCommand.RaiseCanExecuteChanged(); ClearQueueCommand.RaiseCanExecuteChanged(); } }
    public bool IsNotExporting => !IsExporting; // M17: 供 UI 绑定禁用参数面板
    public bool IsPaused { get => _isPaused; private set { _isPaused = value; OnPropertyChanged(); PauseCommand.RaiseCanExecuteChanged(); ResumeCommand.RaiseCanExecuteChanged(); } }
    public double ProgressFraction { get => _progressFraction; set { _progressFraction = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); } }
    public double ProgressPercent => ProgressFraction * 100;
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public string DeviceInfo { get => _deviceInfo; set { _deviceInfo = value; OnPropertyChanged(); } }
    public string EncoderInfo { get => _encoderInfo; set { _encoderInfo = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExportSummary)); } }
    /// <summary>P0-1：CPU 回退提示（非空时 UI 显示回退原因）。</summary>
    public string FallbackHint { get => _fallbackHint; set { _fallbackHint = value; OnPropertyChanged(); } }
    public string SeamlessHint { get; private set; } = "";
    public string PresetFallbackHint { get; private set; } = "";
    public int SelectedQueueIndex { get => _selectedQueueIndex; set { _selectedQueueIndex = value; OnPropertyChanged(); RetryCommand.RaiseCanExecuteChanged(); RemoveSelectedCommand.RaiseCanExecuteChanged(); RefreshPreview(); } }
    public int CompletedCount { get => _completedCount; set { _completedCount = value; OnPropertyChanged(); } }
    public int FailedCount { get => _failedCount; set { _failedCount = value; OnPropertyChanged(); } }
    public double TotalProgressPercent { get => _totalProgressPercent; set { _totalProgressPercent = value; OnPropertyChanged(); } }
    public ImageSource? PreviewImage { get => _previewImage; private set { _previewImage = value; OnPropertyChanged(); } }
    public string PreviewStatus { get => _previewStatus; private set { _previewStatus = value; OnPropertyChanged(); } }
    public string PreviewMotionHint => $"从 {StartYaw:F0}° 起始，以{(DirectionIndex == 0 ? "顺时针" : "逆时针")}旋转 {RotationDegrees}°";
    public string ExportSummary
    {
        get
        {
            if (QueueItems.Count == 0) return "添加图片后将显示文件名、预计体积和保存位置。";
            var first = QueueItems[0];
            var name = OutputNaming.BuildFileName(Path.GetFileNameWithoutExtension(first.SourceFileName), Width, Height, DurationSeconds, RotationDegrees);
            var bytes = ExportPrecheck.EstimateBytes(_settings.Preset, Width, Height, Fps, DurationSeconds) * QueueItems.Count;
            var size = bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024 / 1024:F1} GB" : $"{bytes / 1024d / 1024:F0} MB";
            var fileText = QueueItems.Count == 1 ? name : $"首个：{name}（共 {QueueItems.Count} 个）";
            return $"{fileText}\n保存到：{OutputDir}\n预计总大小：{size} · {EncoderInfo}";
        }
    }

    public AsyncRelayCommand ExportCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand BrowseOutputDirCommand { get; }
    public RelayCommand OpenOutputDirCommand { get; }
    public RelayCommand RemoveSelectedCommand { get; }
    public RelayCommand ClearQueueCommand { get; }
    // P1：新命令
    public RelayCommand PauseCommand { get; }
    public AsyncRelayCommand ResumeCommand { get; }
    public RelayCommand CancelCurrentCommand { get; }
    public AsyncRelayCommand RetryFailedCommand { get; }

    // L10: 精确属性通知，避免 OnPropertyChanged(string.Empty) 全量刷新
    private void SetParam(Func<RenderParameters, RenderParameters> update, [CallerMemberName] string? name = null)
    {
        _settings = _settings with { RenderParameters = update(_settings.RenderParameters) };
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(ExportSummary));
        SaveSettings();
        UpdateSeamlessHint();
        if (name is nameof(HorizontalFov) or nameof(Pitch) or nameof(StartYaw)) RefreshPreview();
        if (name is nameof(RotationDegrees) or nameof(StartYaw)) OnPropertyChanged(nameof(PreviewMotionHint));
    }

    private void SetCustomDimension(Func<RenderParameters, RenderParameters> update, [CallerMemberName] string? name = null)
    {
        _isCustomResolution = true;
        SetParam(update, name);
        OnPropertyChanged(nameof(ResolutionPresetIndex));
        OnPropertyChanged(nameof(IsCustomResolution));
        OnPropertyChanged(nameof(OutputOrientationIndex));
        OnPropertyChanged(nameof(IsLandscapeOutput));
    }

    private void SetDimensions(int width, int height)
    {
        _settings = _settings with { RenderParameters = _settings.RenderParameters with { Width = width, Height = height } };
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(ResolutionPresetIndex));
        OnPropertyChanged(nameof(IsCustomResolution));
        OnPropertyChanged(nameof(OutputOrientationIndex));
        OnPropertyChanged(nameof(IsLandscapeOutput));
        OnPropertyChanged(nameof(ExportSummary));
        SaveSettings();
    }

    private void SetDimensionsForPreset(int presetIndex, bool landscape)
    {
        var (shortEdge, longEdge) = presetIndex switch
        {
            1 => (720, 1280),
            2 => (1440, 2560),
            _ => (1080, 1920),
        };
        SetDimensions(landscape ? longEdge : shortEdge, landscape ? shortEdge : longEdge);
    }

    private static int GetMatchingResolutionPresetIndex(int width, int height) => (width, height) switch
    {
        (1080, 1920) or (1920, 1080) => 0,
        (720, 1280) or (1280, 720) => 1,
        (1440, 2560) or (2560, 1440) => 2,
        _ => -1,
    };

    private static int? TryDetectCpuCores()
    {
        try
        {
            var count = Environment.ProcessorCount;
            return count > 0 ? count : null;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateSeamlessHint()
    {
        var advice = SeamlessLoopAdvisor.Advise(_settings.RenderParameters.RotationDegrees);
        SeamlessHint = advice.IsSeamless ? "" : advice.WarningMessage!;
        OnPropertyChanged(nameof(SeamlessHint));
    }

    private void SaveSettings() => _settingsStore.Save(_settings);

    /// <summary>P0-4：入队即校验。选择文件后立即校验 2:1 比例与可解码性，无效图不进队列。</summary>
    private void BrowseFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "全景图|*.jpg;*.jpeg;*.png", Multiselect = true };
        if (dlg.ShowDialog() != true) return;

        // 默认将成品与首张原图放在同一目录；用户手动选过目录后不再覆盖其选择。
        if (QueueItems.Count == 0 && !_outputDirChosenByUser)
            OutputDir = Path.GetDirectoryName(dlg.FileNames[0]);

        // 追加队列，不因再次选择文件丢失已有任务。
        foreach (var f in dlg.FileNames) SelectedFiles.Add(f);

        // P0-4：入队校验（读头 + ERP 校验）
        var results = _intakeService.IntakeMany(dlg.FileNames, _headerReader);
        int accepted = 0, rejected = 0;
        var rejectMsgs = new List<string>();
        foreach (var r in results)
        {
            if (r.Accepted && r.Item != null)
            {
                var item = r.Item;
                item.TransitionTo(TaskStatus.Pending);
                item.PropertyChanged += QueueItem_PropertyChanged; // H11: 桥接进度
                _items.Add(item);
                _itemPaths[item] = r.SourcePath;
                QueueItems.Add(item);
                accepted++;
            }
            else
            {
                rejected++;
                if (r.RejectionReason != null) rejectMsgs.Add(r.RejectionReason);
            }
        }

        OnPropertyChanged(nameof(SelectedFilePath));
        OnPropertyChanged(nameof(ExportSummary));
        ExportCommand.RaiseCanExecuteChanged();
        RetryFailedCommand.RaiseCanExecuteChanged();
        RemoveSelectedCommand.RaiseCanExecuteChanged();
        ClearQueueCommand.RaiseCanExecuteChanged();
        if (accepted > 0) SelectedQueueIndex = QueueItems.Count - 1;

        if (rejected == 0)
            StatusText = $"已选择 {accepted} 个文件，全部通过校验";
        else
        {
            StatusText = $"已入队 {accepted} 个，拒绝 {rejected} 个：{string.Join("; ", rejectMsgs)}";
            var details = string.Join(Environment.NewLine, rejectMsgs.Take(5));
            if (rejectMsgs.Count > 5) details += $"{Environment.NewLine}另有 {rejectMsgs.Count - 5} 个文件未通过校验。";
            MessageBox.Show($"以下图片未加入导出队列：{Environment.NewLine}{details}", "图片校验未通过", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BrowseOutputDir()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        if (dlg.ShowDialog() == true)
        {
            _outputDirChosenByUser = true;
            OutputDir = dlg.FolderName;
        }
    }

    private void RemoveSelected()
    {
        if (SelectedQueueIndex < 0 || SelectedQueueIndex >= QueueItems.Count) return;
        var item = QueueItems[SelectedQueueIndex];
        item.PropertyChanged -= QueueItem_PropertyChanged;
        _items.Remove(item);
        _itemPaths.Remove(item);
        QueueItems.RemoveAt(SelectedQueueIndex);
        SelectedQueueIndex = QueueItems.Count == 0 ? -1 : Math.Min(SelectedQueueIndex, QueueItems.Count - 1);
        OnPropertyChanged(nameof(SelectedFilePath));
        OnPropertyChanged(nameof(ExportSummary));
        ExportCommand.RaiseCanExecuteChanged();
        RetryFailedCommand.RaiseCanExecuteChanged();
        ClearQueueCommand.RaiseCanExecuteChanged();
        StatusText = "已从队列移除所选图片";
    }

    private void ClearQueue()
    {
        if (QueueItems.Count == 0) return;
        if (MessageBox.Show("确定清空全部待导出图片吗？", "清空导出队列", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var item in _items) item.PropertyChanged -= QueueItem_PropertyChanged;
        _items.Clear();
        _itemPaths.Clear();
        QueueItems.Clear();
        SelectedFiles.Clear();
        SelectedQueueIndex = -1;
        PreviewImage = null;
        PreviewStatus = "添加图片后显示镜头预览";
        OnPropertyChanged(nameof(SelectedFilePath));
        OnPropertyChanged(nameof(ExportSummary));
        ExportCommand.RaiseCanExecuteChanged();
        RetryFailedCommand.RaiseCanExecuteChanged();
        RemoveSelectedCommand.RaiseCanExecuteChanged();
        ClearQueueCommand.RaiseCanExecuteChanged();
        StatusText = "已清空导出队列";
    }

    private void OpenOutputDir()
    {
        if (string.IsNullOrEmpty(OutputDir)) return;
        if (Directory.Exists(OutputDir))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(OutputDir) { UseShellExecute = true });
    }

    /// <summary>
    /// 在窗口启动时完成一次硬件探测。导出前复用相同的探测快照，避免标题区域永久停在“探测中”。
    /// </summary>
    private async Task RefreshDeviceInfoAsync(bool showProbingState = true)
    {
        try
        {
            if (showProbingState) DeviceInfo = "正在探测设备…";
            var (_, presetResult, decision) = await ProbeExportDecisionAsync();
            _lastPreset = presetResult;
            PresetFallbackHint = presetResult.FallbackReason ?? "";
            ApplyDeviceDecision(decision, updateStatus: false);
        }
        catch (Exception ex)
        {
            DeviceInfo = "CPU";
            EncoderInfo = "libx264";
            FallbackHint = $"设备探测失败，已使用 CPU 回退：{ex.GetType().Name}";
        }
    }

    private Task<GpuAvailability> GetGpuAvailabilityAsync()
    {
        return _gpuAvailabilityTask ??= Task.Run(() => new RenderFallbackDecisionProbe().Probe());
    }

    private async Task<(GpuAvailability Gpu, PresetResolveResult PresetResult, FallbackDecision Decision)> ProbeExportDecisionAsync()
    {
        var gpu = await GetGpuAvailabilityAsync();
        var presetResult = new PresetResolver(new MfHevcEncoderProbeAvailable(gpu.HasHevcEncoder)).Resolve(_settings.Preset);
        var decision = FallbackDecider.Decide(gpu, presetResult.Preset);
        return (gpu, presetResult, decision);
    }

    private void ApplyDeviceDecision(FallbackDecision decision, bool updateStatus)
    {
        DeviceInfo = decision.ProjectionDeviceLabel;
        EncoderInfo = decision.EncoderLabel;
        if (decision.UsedCpuFallback && decision.Reason != null)
        {
            FallbackHint = $"CPU 回退：{decision.Reason}";
            if (updateStatus) StatusText = $"CPU 回退模式：{decision.Reason}";
        }
        else if (!string.IsNullOrEmpty(PresetFallbackHint))
        {
            FallbackHint = PresetFallbackHint;
            if (updateStatus) StatusText = PresetFallbackHint;
        }
        else
        {
            FallbackHint = "";
            if (updateStatus) StatusText = $"使用 {decision.EncoderLabel} @ {decision.ProjectionDeviceLabel}";
        }
    }

    private async void RefreshPreview()
    {
        var version = ++_previewVersion;
        if (SelectedQueueIndex < 0 || SelectedQueueIndex >= QueueItems.Count || !_itemPaths.TryGetValue(QueueItems[SelectedQueueIndex], out var path))
        {
            PreviewImage = null;
            PreviewStatus = "添加图片后显示镜头预览";
            return;
        }

        PreviewStatus = "正在生成起始画面预览…";
        try
        {
            var image = await Task.Run(() => RenderPreview(path, HorizontalFov, Pitch, StartYaw));
            if (version != _previewVersion) return;
            PreviewImage = image;
            PreviewStatus = $"起始画面 · FOV {HorizontalFov:F0}° · 俯仰 {Pitch:F0}°";
        }
        catch
        {
            if (version != _previewVersion) return;
            PreviewImage = null;
            PreviewStatus = "无法生成预览，但不影响导出";
        }
    }

    private static BitmapSource RenderPreview(string path, double fov, double pitch, double startYaw = 0.0)
    {
        var original = new BitmapImage();
        original.BeginInit();
        original.UriSource = new Uri(path, UriKind.Absolute);
        original.DecodePixelWidth = 512;
        original.CacheOption = BitmapCacheOption.OnLoad;
        original.EndInit();
        original.Freeze();

        var converted = new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var erpWidth = converted.PixelWidth;
        var erpHeight = converted.PixelHeight;
        var sourcePixels = new byte[erpWidth * erpHeight * 4];
        converted.CopyPixels(sourcePixels, erpWidth * 4, 0);
        var erp = new Rgb[erpWidth * erpHeight];
        for (var y = 0; y < erpHeight; y++)
        for (var x = 0; x < erpWidth; x++)
        {
            var offset = (y * erpWidth + x) * 4;
            erp[y * erpWidth + x] = new Rgb(sourcePixels[offset + 2], sourcePixels[offset + 1], sourcePixels[offset]);
        }

        const int previewWidth = 180;
        const int previewHeight = 320;
        var frame = EquirectRenderer.RenderFrame(erp, erpWidth, erpHeight, previewWidth, previewHeight, fov, startYaw, pitch);
        var pixels = new byte[previewWidth * previewHeight * 4];
        for (var i = 0; i < frame.Length; i++)
        {
            pixels[i * 4] = frame[i].B;
            pixels[i * 4 + 1] = frame[i].G;
            pixels[i * 4 + 2] = frame[i].R;
            pixels[i * 4 + 3] = 255;
        }

        var source = BitmapSource.Create(previewWidth, previewHeight, 96, 96, PixelFormats.Bgra32, null, pixels, previewWidth * 4);
        source.Freeze();
        return source;
    }

    private async Task ExportAsync()
    {
        if (QueueItems.Count == 0 || string.IsNullOrEmpty(OutputDir)) return;
        IsExporting = true;
        IsPaused = false;
        CompletedCount = 0; FailedCount = 0; TotalProgressPercent = 0;
        // L8: 创建新 CTS 前释放旧实例
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        FallbackHint = "";

        try
        {
            // P0-1：GPU 可用性探测 + 回退决策（后台线程，避免 UI 冻结）
            StatusText = "探测设备与编码器...";
            var (gpu, presetResult, decision) = await ProbeExportDecisionAsync();

            _lastPreset = presetResult; // H9/M15: 缓存供重试复用
            PresetFallbackHint = presetResult.FallbackReason ?? "";
            ApplyDeviceDecision(decision, updateStatus: true);

            // P0-5：内存预检（按需解码模式下，峰值≈单张 RGBA × 安全系数）
            var largestW = _items.Max(i => i.Width);
            var largestH = _items.Max(i => i.Height);
            var largestRgba = MemoryPrecheck.EstimateRgbaBytes(largestW, largestH);
            var availableMem = SystemMemoryProbe.GetAvailablePhysicalBytes();
            var memCheck = MemoryPrecheck.Check(largestRgba, availableMem);
            if (!memCheck.CanProceed)
            {
                StatusText = $"内存预检失败：{memCheck.Reason}";
                return;
            }

            if (decision.Backend == ExportBackend.GpuNvenc)
            {
                var vramCheck = VramPrecheck.Check(largestW, largestH, _settings.RenderParameters.Width,
                    _settings.RenderParameters.Height, gpu.DedicatedVideoMemoryBytes);
                if (!vramCheck.CanProceed)
                {
                    StatusText = $"显存预检失败：{vramCheck.Reason}";
                    return;
                }
            }

            // P0-5：按需解码 erpLoader（替代旧 _rgbaCache 预解码）
            var erpLoader = new OnDemandErpLoader(_itemPaths);
            // P0-1：executorFactory 按回退决策选择执行器
            var hevcAvailable = decision.Backend == ExportBackend.GpuNvenc && gpu.HasHevcEncoder;
            _scheduler = new SerialBatchScheduler(
                executorFactory: (item, rgba, w, h) => decision.Backend == ExportBackend.CpuFallback
                    ? new FfmpegCpuFallbackExecutor(rgba, w, h, _settings.RenderParameters, presetResult.Preset)
                    : new FfmpegNvencExecutor(rgba, w, h, _settings.RenderParameters, presetResult.Preset, hevcAvailable: hevcAvailable),
                erpLoader: erpLoader);

            long avail = GetAvailableBytes(OutputDir);
            int totalItems = _items.Count;

            StatusText = $"正在读取第 1/{totalItems} 张全景图…";
            await RunQueueWithPauseAsync(_scheduler, _settings.RenderParameters, presetResult.Preset, OutputDir, avail);

            CompletedCount = _items.Count(i => i.Status == TaskStatus.Completed);
            FailedCount = _items.Count(i => i.Status == TaskStatus.Failed);
            TotalProgressPercent = 100;
            var firstFailure = _items.FirstOrDefault(i => i.Status == TaskStatus.Failed);
            StatusText = _cts.IsCancellationRequested
                ? $"已取消：完成 {CompletedCount}/{totalItems}，失败 {FailedCount}"
                : firstFailure is null
                    ? $"完成：{CompletedCount}/{totalItems}"
                    : $"完成：{CompletedCount}/{totalItems}，失败 {FailedCount}。{firstFailure.SourceFileName}：{firstFailure.ErrorMessage}";

            if (!_cts.IsCancellationRequested && FailedCount == 0 && CompletedCount > 0 && _settings.OpenAfterExport)
                OpenCompletedOutput();
        }
        catch (Exception ex)
        {
            // L7: 保留完整异常信息（含堆栈），便于诊断
            StatusText = $"错误: {ex}";
        }
        finally
        {
            IsPaused = false;
            _resumeGate = null;
            IsExporting = false;
        }
    }

    private async void RetrySelected()
    {
        if (SelectedQueueIndex < 0 || SelectedQueueIndex >= QueueItems.Count) return;
        var item = QueueItems[SelectedQueueIndex];
        if (item.Status != TaskStatus.Failed) return;
        if (string.IsNullOrEmpty(OutputDir) || _scheduler == null) return;
        IsExporting = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            // H9/M15: 用缓存的首次预设，与导出保持一致（不硬编码 Compatibility）
            long avail = GetAvailableBytes(OutputDir);
            var preset = _lastPreset ?? new PresetResolveResult(_settings.Preset, null);
            await Task.Run(() => _scheduler!.RetryAsync(item, _settings.RenderParameters, preset.Preset, OutputDir!, avail, _cts.Token));
            CompletedCount = _items.Count(i => i.Status == TaskStatus.Completed);
            FailedCount = _items.Count(i => i.Status == TaskStatus.Failed);
            StatusText = $"重试完成: {item.Status}";
        }
        catch (Exception ex)
        {
            // H9: async void 无 catch 致崩溃，补 catch
            StatusText = $"重试错误: {ex.GetType().Name}: {ex.Message}";
        }
        finally { IsExporting = false; }
    }

    private void PauseQueue()
    {
        _scheduler?.Pause();
        IsPaused = true;
        StatusText = "暂停将在当前任务完成后生效";
    }

    private void CancelAll()
    {
        _cts?.Cancel();
        _resumeGate?.TrySetResult();
    }

    /// <summary>恢复暂停队列。由仍在等待的 ExportAsync 接手继续后续未处理项，避免并发启动两个调度循环。</summary>
    private async Task ResumeAsync()
    {
        if (_scheduler == null) return;
        _scheduler.Resume();
        IsPaused = false;
        _resumeGate?.TrySetResult();
        await Task.CompletedTask;
    }

    private async Task RunQueueWithPauseAsync(
        SerialBatchScheduler scheduler, RenderParameters parameters, ExportPreset preset,
        string outputDir, long availableBytes)
    {
        var pending = _items.Where(i => i.Status is TaskStatus.PendingValidation or TaskStatus.Pending).ToList();
        while (pending.Count > 0 && !_cts!.IsCancellationRequested)
        {
            await Task.Run(() => scheduler.RunAsync(pending, parameters, preset, outputDir, availableBytes, _cts.Token));
            pending = _items.Where(i => i.Status is TaskStatus.PendingValidation or TaskStatus.Pending).ToList();
            if (pending.Count == 0 || _cts.IsCancellationRequested) break;

            if (IsPaused)
            {
                StatusText = $"队列已暂停，剩余 {pending.Count} 项";
                _resumeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await _resumeGate.Task;
                _resumeGate = null;
            }
        }
    }

    private void OpenCompletedOutput()
    {
        var completed = _items.Where(i => i.Status == TaskStatus.Completed && !string.IsNullOrWhiteSpace(i.OutputPath)).ToList();
        if (completed.Count == 1)
        {
            var path = completed[0].OutputPath!;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }
        OpenOutputDir();
    }

    /// <summary>P1：串行重试全部失败项。</summary>
    private async Task RetryAllFailedAsync()
    {
        var failed = _items.Where(i => i.Status == TaskStatus.Failed).ToList();
        if (failed.Count == 0 || _scheduler == null || string.IsNullOrEmpty(OutputDir)) return;
        IsExporting = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        long avail = GetAvailableBytes(OutputDir);
        var preset = _lastPreset ?? new PresetResolveResult(_settings.Preset, null);
        try
        {
            foreach (var item in failed)
            {
                await Task.Run(() => _scheduler!.RetryAsync(item, _settings.RenderParameters, preset.Preset, OutputDir!, avail, _cts.Token));
            }
            CompletedCount = _items.Count(i => i.Status == TaskStatus.Completed);
            FailedCount = _items.Count(i => i.Status == TaskStatus.Failed);
            StatusText = $"重试全部完成: 成功 {CompletedCount} 失败 {FailedCount}";
        }
        catch (Exception ex) { StatusText = $"重试错误: {ex.GetType().Name}: {ex.Message}"; }
        finally { IsExporting = false; }
    }

    /// <summary>H9: 安全获取可用磁盘空间，UNC/异常路径返回 long.MaxValue 跳过预检</summary>
    private static long GetAvailableBytes(string dir)
    {
        try
        {
            var root = Path.GetPathRoot(dir);
            if (string.IsNullOrEmpty(root)) return long.MaxValue;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return long.MaxValue; }
    }

    /// <summary>H11: 当前队列项进度变化时桥接到进度条与状态文本。P1：区分投影/编码 FPS。</summary>
    private void QueueItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not QueueItem item) return;
        if (e.PropertyName == nameof(QueueItem.Status) && item.Status == TaskStatus.Processing)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ProgressFraction = 0;
                StatusText = $"正在读取全景图：{item.SourceFileName}";
            });
            return;
        }
        if (e.PropertyName == nameof(QueueItem.ErrorMessage) && !string.IsNullOrWhiteSpace(item.ErrorMessage))
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
                StatusText = $"导出失败：{item.SourceFileName}：{item.ErrorMessage}");
            return;
        }
        // 只更新当前选中项或处理中项
        if (SelectedQueueIndex >= 0 && SelectedQueueIndex < QueueItems.Count && QueueItems[SelectedQueueIndex] != item
            && item.Status != TaskStatus.Processing) return;
        if (item.Status == TaskStatus.Processing)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ProgressFraction = item.Progress.ProgressFraction;
                // P1：状态文本区分投影/编码 FPS（编码 FPS>0 时显示编码中，否则渲染中）
                var fpsText = item.Progress.EncodingFps > 0
                    ? $"编码中 {item.Progress.EncodingFps:F0}fps"
                    : $"渲染中 {item.Progress.ProjectionFps:F0}fps";
                StatusText = $"{fpsText} {item.Progress.FramesDone}/{item.Progress.TotalFrames}";
                // 总进度：已完成项 + 当前项分数
                if (_items.Count > 0)
                {
                    var done = _items.Count(i => i.Status == TaskStatus.Completed);
                    TotalProgressPercent = BatchProgressCalculator.CalculatePercent(
                        _items.Count, done, item.Status == TaskStatus.Processing, item.Progress.ProgressFraction);
                }
            });
        }
    }

    private static string GetSettingsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PanoToVideo", "AppSettings.json");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose()
    {
        // H10: 关窗先取消进行中的导出，避免后台 Task 访问已 Dispose 的 _cts
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
    }
}

/// <summary>
/// 包装已知 HEVC 可用性为 IHevcEncoderProbe（P0-1：复用 RenderFallbackDecisionProbe 的探测结果，
/// 避免重复枚举 HEVC 编码器）。
/// </summary>
internal sealed class MfHevcEncoderProbeAvailable : IHevcEncoderProbe
{
    private readonly bool _available;
    public MfHevcEncoderProbeAvailable(bool available) => _available = available;
    public bool IsAvailable() => _available;
}

public sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute; private readonly Func<bool> _canExecute;
    public RelayCommand(Action execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute ?? (() => true); }
    public bool CanExecute() => _canExecute();
    public void Execute() => _execute();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    bool System.Windows.Input.ICommand.CanExecute(object? parameter) => CanExecute();
    void System.Windows.Input.ICommand.Execute(object? parameter) => Execute();
}

public sealed class AsyncRelayCommand : System.Windows.Input.ICommand
{
    private readonly Func<Task> _execute; private readonly Func<bool> _canExecute;
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute ?? (() => true); }
    public bool CanExecute() => _canExecute();
    public async void Execute() { await _execute(); }
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    bool System.Windows.Input.ICommand.CanExecute(object? parameter) => CanExecute();
    void System.Windows.Input.ICommand.Execute(object? parameter) => Execute();
}
