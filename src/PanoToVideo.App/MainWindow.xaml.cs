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
}
