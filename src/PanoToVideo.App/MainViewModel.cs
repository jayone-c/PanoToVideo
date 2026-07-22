using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Projection;
using PanoToVideo.Core.Queue;
using PanoToVideo.Core.Settings;
using PanoToVideo.Core.Validation;
using PanoToVideo.Render.DeviceProbe;
using PanoToVideo.Render.Exporting;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TaskStatus = PanoToVideo.Core.Queue.TaskStatus;

namespace PanoToVideo.App;

/// <summary>
/// 主窗口视图模型（阶段3最小可用UI）。
/// 单图拖入 + 参数面板 + 导出 + 进度 + 设备显示。
/// 配置记忆 + 预设退回 + 无缝提示接入。
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppSettingsStore _settingsStore;
    private AppSettings _settings;
    private string? _selectedFilePath;
    private bool _isExporting;
    private double _progressFraction;
    private string _statusText = "就绪";
    private string _deviceInfo = "探测中...";
    private CancellationTokenSource? _cts;
    private DeviceEntry? _preferredDevice;

    public MainViewModel()
    {
        _settingsStore = new AppSettingsStore(GetSettingsPath());
        _settings = _settingsStore.Load();
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !IsExporting && SelectedFilePath != null);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsExporting);
        BrowseCommand = new RelayCommand(BrowseFile);
    }

    // 参数绑定（直接暴露 RenderParameters 字段，双向）
    public int DurationSeconds { get => _settings.RenderParameters.DurationSeconds; set => SetParam(p => p with { DurationSeconds = value }); }
    public int RotationDegrees { get => _settings.RenderParameters.RotationDegrees; set => SetParam(p => p with { RotationDegrees = value }); }
    public int Fps { get => _settings.RenderParameters.Fps; set => SetParam(p => p with { Fps = value }); }
    public double HorizontalFov { get => _settings.RenderParameters.HorizontalFov; set => SetParam(p => p with { HorizontalFov = value }); }
    public int Width { get => _settings.RenderParameters.Width; set => SetParam(p => p with { Width = value }); }
    public int Height { get => _settings.RenderParameters.Height; set => SetParam(p => p with { Height = value }); }
    public double Pitch { get => _settings.RenderParameters.Pitch; set => SetParam(p => p with { Pitch = value }); }
    public bool AsteroidIntro { get => _settings.RenderParameters.AsteroidIntro; set => SetParam(p => p with { AsteroidIntro = value }); }
    public RotationDirection Direction
    {
        get => _settings.RenderParameters.Direction;
        set => SetParam(p => p with { Direction = value });
    }
    public ExportPreset Preset { get => _settings.Preset; set { _settings = _settings with { Preset = value }; OnPropertyChanged(); SaveSettings(); UpdateSeamlessHint(); } }

    public string? SelectedFilePath { get => _selectedFilePath; set { _selectedFilePath = value; OnPropertyChanged(); ExportCommand.RaiseCanExecuteChanged(); } }
    public bool IsExporting { get => _isExporting; set { _isExporting = value; OnPropertyChanged(); ExportCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); } }
    public double ProgressFraction { get => _progressFraction; set { _progressFraction = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); } }
    public double ProgressPercent => ProgressFraction * 100;
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public string DeviceInfo { get => _deviceInfo; set { _deviceInfo = value; OnPropertyChanged(); } }
    public string SeamlessHint { get; private set; } = "";
    public string PresetFallbackHint { get; private set; } = "";

    public AsyncRelayCommand ExportCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseCommand { get; }

    private void SetParam(Func<RenderParameters, RenderParameters> update)
    {
        _settings = _settings with { RenderParameters = update(_settings.RenderParameters) };
        OnPropertyChanged(string.Empty); // 刷新所有绑定
        SaveSettings();
        UpdateSeamlessHint();
    }

    private void UpdateSeamlessHint()
    {
        var advice = SeamlessLoopAdvisor.Advise(_settings.RenderParameters.RotationDegrees);
        SeamlessHint = advice.IsSeamless ? "" : advice.WarningMessage!;
        OnPropertyChanged(nameof(SeamlessHint));
    }

    private void SaveSettings() => _settingsStore.Save(_settings);

    private void BrowseFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "全景图|*.jpg;*.jpeg;*.png" };
        if (dlg.ShowDialog() == true)
            SelectedFilePath = dlg.FileName;
    }

    private async Task ExportAsync()
    {
        if (SelectedFilePath == null) return;
        IsExporting = true;
        ProgressFraction = 0;
        StatusText = "探测设备...";
        _cts = new CancellationTokenSource();

        try
        {
            // 设备探测
            using var probe = new DeviceProbe();
            var probeResult = probe.Probe();
            _preferredDevice = probeResult.Preferred;
            DeviceInfo = _preferredDevice == null
                ? "无可用 GPU 设备"
                : $"{_preferredDevice.Candidate.Description} | {_preferredDevice.EncoderName}";

            if (_preferredDevice == null)
            {
                StatusText = "无可用 GPU 设备，无法导出";
                return;
            }

            // 预设退回
            using var hevcProbe = new MfHevcEncoderProbe();
            var presetResult = new PresetResolver(hevcProbe).Resolve(_settings.Preset);
            PresetFallbackHint = presetResult.FallbackReason ?? "";

            // 解码 ERP（JPEG/PNG -> RGBA）
            StatusText = "解码图片...";
            var (rgba, w, h) = DecodeImage(SelectedFilePath);

            // ERP 校验
            var imageInfo = new ImageInfo(w, h, false, SelectedFilePath);
            var validation = new EquirectValidator().Validate(imageInfo);
            if (!validation.IsValid)
            {
                StatusText = $"校验失败: {validation.Reason}";
                return;
            }

            // 输出目录 + 预检
            string outDir = Path.GetDirectoryName(SelectedFilePath)!;
            long avail = new DriveInfo(Path.GetPathRoot(outDir)!).AvailableFreeSpace;

            var executor = new GpuExportExecutor(rgba, w, h, _settings.RenderParameters, presetResult.Preset);
            var orchestrator = new SingleImageExportOrchestrator();

            var progress = new ProgressAdapter<ExportProgress>(p =>
            {
                ProgressFraction = p.ProgressFraction;
                StatusText = $"渲染中 {p.FrameIndex + 1}/{p.TotalFrames} 投影{p.ProjectionFps:F0}fps 编码{p.EncodingFps:F0}fps";
            });

            StatusText = "导出中...";
            var result = await Task.Run(() => orchestrator.Export(
                imageInfo, _settings.RenderParameters, presetResult.Preset, outDir, avail,
                Directory.GetFiles(outDir, "*.mp4"), executor, _cts.Token, progress));

            if (result.Success)
            {
                ProgressFraction = 1;
                StatusText = $"完成: {Path.GetFileName(result.OutputPath)} 平均{result.Log!.AverageFps:F0}fps";
                // 完成后打开（单图->打开MP4）
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.OutputPath!) { UseShellExecute = true }); } catch { }
            }
            else
            {
                StatusText = $"失败: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"错误: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    /// <summary>解码 JPEG/PNG 为 RGBA（App 层职责，GDI+）。</summary>
    private static (byte[] rgba, int w, int h) DecodeImage(string path)
    {
        using var bmp = new Bitmap(path);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var rgba = new byte[bmp.Width * bmp.Height * 4];
            for (int y = 0; y < bmp.Height; y++)
            {
                var row = new byte[bmp.Width * 4];
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, bmp.Width * 4);
                for (int x = 0; x < bmp.Width; x++)
                {
                    rgba[(y * bmp.Width + x) * 4] = row[x * 4 + 2];     // B->R
                    rgba[(y * bmp.Width + x) * 4 + 1] = row[x * 4 + 1]; // G
                    rgba[(y * bmp.Width + x) * 4 + 2] = row[x * 4];     // R->B
                    rgba[(y * bmp.Width + x) * 4 + 3] = 255;
                }
            }
            return (rgba, bmp.Width, bmp.Height);
        }
        finally { bmp.UnlockBits(data); }
    }

    private static string GetSettingsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PanoToVideo", "AppSettings.json");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class ProgressAdapter<T> : IProgress<T>
    {
        private readonly Action<T> _cb;
        public ProgressAdapter(Action<T> cb) => _cb = cb;
        public void Report(T value) => Application.Current?.Dispatcher.Invoke(() => _cb(value));
    }

    public void Dispose() => _cts?.Dispose();
}

public sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;
    private readonly Func<bool> _canExecute;
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
    private readonly Func<Task> _execute;
    private readonly Func<bool> _canExecute;
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute ?? (() => true); }
    public bool CanExecute() => _canExecute();
    public async void Execute() { await _execute(); }
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    bool System.Windows.Input.ICommand.CanExecute(object? parameter) => CanExecute();
    void System.Windows.Input.ICommand.Execute(object? parameter) => Execute();
}
