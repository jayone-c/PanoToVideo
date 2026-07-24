using System.Windows;

namespace PanoToVideo.App;

/// <summary>
/// 主窗口（阶段3最小可用UI）。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        app.ToggleTheme();
        ThemeToggleButton.Content = app.IsDarkTheme ? "☀" : "☾";
        ThemeToggleButton.ToolTip = app.IsDarkTheme ? "切换到浅色主题" : "切换到深色主题";
    }
}
