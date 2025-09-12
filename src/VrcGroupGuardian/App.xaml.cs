using System.Windows;
using VrcGroupGuardian.Infrastructure;

namespace VrcGroupGuardian;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        LoggingConfiguration.ConfigureLogging();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LoggingConfiguration.CloseAndFlush();
        base.OnExit(e);
    }
}