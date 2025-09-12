using System.IO;
using Serilog;
using Serilog.Events;

namespace VrcGroupGuardian.Infrastructure;

public static class LoggingConfiguration
{
    public static void ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "VrcGroupGuardian")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
            )
            .WriteTo.File(
                path: System.IO.Path.Combine(GetLogDirectory(), "vrc-group-guardian-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB
                rollOnFileSizeLimit: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
            )
            .CreateLogger();

        Log.Information("Serilog logging configured successfully");
    }

    private static string GetLogDirectory()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDirectory = System.IO.Path.Combine(baseDirectory, "VrcGroupGuardian", "Logs");
        
        if (!System.IO.Directory.Exists(logDirectory))
        {
            System.IO.Directory.CreateDirectory(logDirectory);
        }
        
        return logDirectory;
    }

    public static void CloseAndFlush()
    {
        Log.CloseAndFlush();
    }
}