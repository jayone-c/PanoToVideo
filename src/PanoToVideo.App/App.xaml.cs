using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PanoToVideo.App;

/// <summary>
/// Interaction logic for App.xaml
/// M16: 全局异常处理，避免 async void 等未捕获异常致静默崩溃
/// </summary>
public partial class App : Application
{
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
}
