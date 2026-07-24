using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;

namespace PanoToVideo.App;

/// <summary>
/// Interaction logic for App.xaml
/// M16: 全局异常处理，避免 async void 等未捕获异常致静默崩溃
/// </summary>
public partial class App : Application
{
    public bool IsDarkTheme { get; private set; } = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 捕获 UI 线程未处理异常
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        // 捕获后台 Task 未观察异常
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        // 捕获非托管/非 UI 线程致命异常
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("Dispatcher", e.Exception);
        MessageBox.Show($"发生错误：{e.Exception.Message}\n\n详情见日志文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // 阻止崩溃
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogException("UnobservedTask", e.Exception);
        e.SetObserved();
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogException("AppDomain", ex);
    }

    private static void LogException(string source, Exception ex)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PanoToVideo");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n\n");
        }
        catch { /* 日志失败不阻塞 */ }
    }

    public void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplyTheme(IsDarkTheme);
    }

    private void ApplyTheme(bool dark)
    {
        var palette = dark
            ? new Dictionary<string, string>
            {
                ["CanvasBrush"] = "#10171E", ["SurfaceBrush"] = "#18232D", ["SurfaceAltBrush"] = "#1E2B36",
                ["StrokeBrush"] = "#344652", ["TextBrush"] = "#F0F4F7", ["MutedTextBrush"] = "#A9B7C2",
                ["AccentBrush"] = "#D78342", ["AccentPressedBrush"] = "#B9642B", ["AccentSoftBrush"] = "#30251D",
                ["DangerBrush"] = "#ED938B", ["DangerSoftBrush"] = "#392225", ["DangerStrokeBrush"] = "#71464B",
                ["HintTextBrush"] = "#F0BE91", ["ProgressTrackBrush"] = "#0D1318"
            }
            : new Dictionary<string, string>
            {
                ["CanvasBrush"] = "#F3F5F7", ["SurfaceBrush"] = "#FFFFFF", ["SurfaceAltBrush"] = "#F8FAFC",
                ["StrokeBrush"] = "#D9E1E8", ["TextBrush"] = "#17212B", ["MutedTextBrush"] = "#667788",
                ["AccentBrush"] = "#C66E2E", ["AccentPressedBrush"] = "#A95720", ["AccentSoftBrush"] = "#FCEDE2",
                ["DangerBrush"] = "#A63D37", ["DangerSoftBrush"] = "#FBEDEC", ["DangerStrokeBrush"] = "#F0C9C6",
                ["HintTextBrush"] = "#895020", ["ProgressTrackBrush"] = "#E5EBF0"
            };

        foreach (var (key, color) in palette)
            Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
    }
}
