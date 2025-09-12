using System.Windows.Controls;
using Serilog;

namespace VrcGroupGuardian.Views;

public partial class AuditView : UserControl
{
    private readonly ILogger _logger = Log.ForContext<AuditView>();

    public AuditView()
    {
        InitializeComponent();
        _logger.Debug("AuditView initialized");
    }
}