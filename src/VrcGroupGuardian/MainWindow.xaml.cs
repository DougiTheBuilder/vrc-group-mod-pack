using System.Windows;
using Serilog;

namespace VrcGroupGuardian;

public partial class MainWindow : Window
{
    private readonly ILogger _logger = Log.ForContext<MainWindow>();

    public MainWindow()
    {
        InitializeComponent();
        _logger.Information("MainWindow initialized");
    }
}