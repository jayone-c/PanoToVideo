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
/// 主窗口视图模型（五区UI）：顶部文件输入+输出目录+硬件状态，左参数，中队列，右详情，底部累计。
/// 支持多图批量导出（SerialBatchScheduler）+ 暂停/取消/重试 + 打开输出目录。
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppSettingsStore _settingsStore;
    private AppSettings _settings;
    private string? _outputDir;
    private bool _isExporting;
    private double _progressFraction;
    private string _statusText = "就绪";
    private string _deviceInfo = "探测中...";
    private string _encoderInfo = "";
    private CancellationTokenSource? _cts;
    private DeviceEntry? _preferredDevice;
    private int _selectedQueueIndex;
    private int _completedCount;
    private int _failedCount;
    private double _totalProgressPercent;
    private SerialBatchScheduler? _scheduler;
    private List<QueueItem> _items = new();

    public ObservableCollection<QueueItem> QueueItems { get; } = new();
    public ObservableCollection<string> SelectedFiles { get; } = new();

    public MainViewModel()
    {
        _settingsStore = new AppSettingsStore(GetSettingsPath());
        _settings = _settingsStore.Load();
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !IsExporting && SelectedFiles.Count > 0 && !string.IsNullOrEmpty(OutputDir));
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsExporting);
        RetryCommand = new RelayCommand(RetrySelected, () => !IsExporting && SelectedQueueIndex >= 0 && SelectedQueueIndex < QueueItems.Count && QueueItems[SelectedQueueIndex].Status == TaskStatus.Failed);
        BrowseCommand = new RelayCommand(BrowseFiles);
        BrowseOutputDirCommand = new RelayCommand(BrowseOutputDir);
        OpenOutputDirCommand = new RelayCommand(OpenOutputDir, () => !string.IsNullOrEmpty(OutputDir));
        OutputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PanoToVideo");
    }

    // 参数绑定
    public int DurationSeconds { get => _settings.RenderParameters.DurationSeconds; set => SetParam(p => p with { DurationSeconds = value }); }
    public int RotationDegrees { get => _settings.RenderParameters.RotationDegrees; set => SetParam(p => p with { RotationDegrees = value }); }
    public int Fps { get => _settings.RenderParameters.Fps; set => SetParam(p => p with { Fps = value }); }
    public double HorizontalFov { get => _settings.RenderParameters.HorizontalFov; set => SetParam(p => p with { HorizontalFov = value }); }
    public int Width { get => _settings.RenderParameters.Width; set => SetParam(p => p with { Width = value }); }
    public int Height { get => _settings.RenderParameters.Height; set => SetParam(p => p with { Height = value }); }
    public double Pitch { get => _settings.RenderParameters.Pitch; set => SetParam(p => p with { Pitch = value }); }
    public bool AsteroidIntro { get => _settings.RenderParameters.AsteroidIntro; set => SetParam(p => p with { AsteroidIntro = value }); }
    public int PresetIndex
    {
        get => (int)_settings.Preset;
        set { _settings = _settings with { Preset = (ExportPreset)value }; OnPropertyChanged(); SaveSettings(); }
    }

    public string? OutputDir { get => _outputDir; set { _outputDir = value; OnPropertyChanged(); ExportCommand.RaiseCanExecuteChanged(); OpenOutputDirCommand.RaiseCanExecuteChanged(); } }
    public bool IsExporting { get => _isExporting; set { _isExporting = value; OnPropertyChanged(); ExportCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); } }
    public double ProgressFraction { get => _progressFraction; set { _progressFraction = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); } }
    public double ProgressPercent => ProgressFraction * 100;
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public string DeviceInfo { get => _deviceInfo; set { _deviceInfo = value; OnPropertyChanged(); } }
    public string EncoderInfo { get => _encoderInfo; set { _encoderInfo = value; OnPropertyChanged(); } }
    public string SeamlessHint { get; private set; } = "";
    public string PresetFallbackHint { get; private set; } = "";
    public int SelectedQueueIndex { get => _selectedQueueIndex; set { _selectedQueueIndex = value; OnPropertyChanged(); RetryCommand.RaiseCanExecuteChanged(); } }
    public int CompletedCount { get => _completedCount; set { _completedCount = value; OnPropertyChanged(); } }
    public int FailedCount { get => _failedCount; set { _failedCount = value; OnPropertyChanged(); } }
    public double TotalProgressPercent { get => _totalProgressPercent; set { _totalProgressPercent = value; OnPropertyChanged(); } }

    public AsyncRelayCommand ExportCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand BrowseOutputDirCommand { get; }
    public RelayCommand OpenOutputDirCommand { get; }

    private void SetParam(Func<RenderParameters, RenderParameters> update)
    {
        _settings = _settings with { RenderParameters = update(_settings.RenderParameters) };
        OnPropertyChanged(string.Empty);
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

    private void BrowseFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "全景图|*.jpg;*.jpeg;*.png", Multiselect = true };
        if (dlg.ShowDialog() == true)
        {
            SelectedFiles.Clear();
            foreach (var f in dlg.FileNames) SelectedFiles.Add(f);
            StatusText = $"已选择 {SelectedFiles.Count} 个文件";
        }
    }

    private void BrowseOutputDir()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        if (dlg.ShowDialog() == true) OutputDir = dlg.FolderName;
    }

    private void OpenOutputDir()
    {
        if (string.IsNullOrEmpty(OutputDir)) return;
        var exportsDir = Path.Combine(OutputDir, "exports");
        if (Directory.Exists(exportsDir))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exportsDir) { UseShellExecute = true });
    }

    private async Task ExportAsync()
    {
        if (SelectedFiles.Count == 0 || string.IsNullOrEmpty(OutputDir)) return;
        IsExporting = true;
        CompletedCount = 0; FailedCount = 0; TotalProgressPercent = 0;
        QueueItems.Clear();
        _cts = new CancellationTokenSource();

        try
        {
            // 设备探测
            StatusText = "探测设备...";
            using var probe = new DeviceProbe();
            var probeResult = probe.Probe();
            _preferredDevice = probeResult.Preferred;
            DeviceInfo = _preferredDevice == null ? "无可用 GPU 设备" : _preferredDevice.Candidate.Description;
            EncoderInfo = _preferredDevice?.EncoderName ?? "";
            if (_preferredDevice == null) { StatusText = "无可用 GPU 设备"; return; }

            // 预设退回
            using var hevcProbe = new MfHevcEncoderProbe();
            var presetResult = new PresetResolver(hevcProbe).Resolve(_settings.Preset);
            PresetFallbackHint = presetResult.FallbackReason ?? "";

            // 构建队列项
            _items = new List<QueueItem>();
            foreach (var file in SelectedFiles)
            {
                var (rgba, w, h) = DecodeImage(file);
                var item = new QueueItem(Path.GetFileName(file), w, h);
                _items.Add(item);
                Application.Current.Dispatcher.Invoke(() => QueueItems.Add(item));
            }

            // 调度器
            _scheduler = new SerialBatchScheduler(
                executorFactory: (item, rgba, w, h) => new FfmpegNvencExecutor(rgba, w, h, _settings.RenderParameters, presetResult.Preset),
                erpLoader: item =>
                {
                    int idx = _items.IndexOf(item);
                    return DecodeImage(SelectedFiles[idx]);
                });

            long avail = new DriveInfo(Path.GetPathRoot(OutputDir)!).AvailableFreeSpace;
            int totalItems = _items.Count;

            await Task.Run(() => _scheduler.RunAsync(_items, _settings.RenderParameters, presetResult.Preset, OutputDir, avail, _cts.Token));

            CompletedCount = _items.Count(i => i.Status == TaskStatus.Completed);
            FailedCount = _items.Count(i => i.Status == TaskStatus.Failed);
            TotalProgressPercent = 100;
            StatusText = $"完成: {CompletedCount}/{totalItems} 失败:{FailedCount}";

            // 完成后打开输出目录
            if (FailedCount == 0) OpenOutputDir();
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

    private async void RetrySelected()
    {
        if (SelectedQueueIndex < 0 || SelectedQueueIndex >= QueueItems.Count) return;
        var item = QueueItems[SelectedQueueIndex];
        if (item.Status != TaskStatus.Failed) return;
        IsExporting = true;
        _cts = new CancellationTokenSource();
        try
        {
            long avail = new DriveInfo(Path.GetPathRoot(OutputDir!)!).AvailableFreeSpace;
            await Task.Run(() => _scheduler!.RetryAsync(item, _settings.RenderParameters, ExportPreset.Compatibility, OutputDir!, avail, _cts.Token));
            CompletedCount = _items.Count(i => i.Status == TaskStatus.Completed);
            FailedCount = _items.Count(i => i.Status == TaskStatus.Failed);
            StatusText = $"重试完成: {item.Status}";
        }
        finally { IsExporting = false; }
    }

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
                    rgba[(y * bmp.Width + x) * 4] = row[x * 4 + 2];
                    rgba[(y * bmp.Width + x) * 4 + 1] = row[x * 4 + 1];
                    rgba[(y * bmp.Width + x) * 4 + 2] = row[x * 4];
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
    public void Dispose() => _cts?.Dispose();
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
