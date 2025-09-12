using System.Windows;
using Serilog;
using VrcGroupGuardian.ViewModels;

namespace VrcGroupGuardian;

public partial class MainWindow : Window
{
    private readonly ILogger _logger = Log.ForContext<MainWindow>();
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        
        InitializeComponent();
        DataContext = _viewModel;
        
        _logger.Information("MainWindow initialized");
    }
}