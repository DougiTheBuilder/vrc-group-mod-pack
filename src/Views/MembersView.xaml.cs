using System.Windows.Controls;
using Serilog;

namespace VrcGroupGuardian.Views;

public partial class MembersView : UserControl
{
    private readonly ILogger _logger = Log.ForContext<MembersView>();

    public MembersView()
    {
        InitializeComponent();
        _logger.Debug("MembersView initialized");
    }
}