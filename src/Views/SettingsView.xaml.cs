using System.Windows.Controls;
using Serilog;

namespace VrcGroupGuardian.Views;

public partial class SettingsView : UserControl
{
    private readonly ILogger _logger = Log.ForContext<SettingsView>();

    public SettingsView()
    {
        InitializeComponent();
        _logger.Debug("SettingsView initialized");
    }
}