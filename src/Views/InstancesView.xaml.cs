using System.Windows.Controls;
using Serilog;

namespace VrcGroupGuardian.Views;

public partial class InstancesView : UserControl
{
    private readonly ILogger _logger = Log.ForContext<InstancesView>();

    public InstancesView()
    {
        InitializeComponent();
        _logger.Debug("InstancesView initialized");
    }
}