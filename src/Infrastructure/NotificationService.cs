using System.Windows;
using System.Diagnostics;
using Serilog;

namespace VrcGroupGuardian.Infrastructure;

public interface INotificationService
{
    Task ShowNotificationAsync(string title, string message, NotificationSeverity severity = NotificationSeverity.Information);
    Task ShowDesktopNotificationAsync(string title, string message, NotificationSeverity severity = NotificationSeverity.Information);
    void ShowInAppNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Information);
}

public class NotificationService : INotificationService
{
    private readonly ILogger _logger = Log.ForContext<NotificationService>();

    public async Task ShowNotificationAsync(string title, string message, NotificationSeverity severity = NotificationSeverity.Information)
    {
        try
        {
            // Try desktop notification first, fall back to in-app
            if (IsDesktopNotificationSupported())
            {
                await ShowDesktopNotificationAsync(title, message, severity);
            }
            else
            {
                ShowInAppNotification(title, message, severity);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to show notification: {Title}", title);
            // Fall back to in-app notification
            ShowInAppNotification(title, message, severity);
        }
    }

    public async Task ShowDesktopNotificationAsync(string title, string message, NotificationSeverity severity = NotificationSeverity.Information)
    {
        try
        {
            if (!IsDesktopNotificationSupported())
            {
                _logger.Debug("Desktop notifications not supported, falling back to in-app");
                ShowInAppNotification(title, message, severity);
                return;
            }

            // Use Windows notification system via PowerShell (works on Windows 10/11)
            await ShowWindowsToastNotificationAsync(title, message, severity);
            
            _logger.Information("Desktop notification shown: {Title} - {Severity}", title, severity);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to show desktop notification: {Title}, falling back to in-app", title);
            ShowInAppNotification(title, message, severity);
        }
    }

    public void ShowInAppNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Information)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var messageBoxIcon = GetMessageBoxIcon(severity);
                MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, messageBoxIcon);
            });
            
            _logger.Information("In-app notification shown: {Title} - {Severity}", title, severity);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show in-app notification: {Title}", title);
            throw;
        }
    }

    private bool IsDesktopNotificationSupported()
    {
        // Check if running on Windows 10/11 and if notifications are enabled
        try
        {
            var version = Environment.OSVersion.Version;
            return version.Major >= 10; // Windows 10 or later
        }
        catch
        {
            return false;
        }
    }

    private async Task ShowWindowsToastNotificationAsync(string title, string message, NotificationSeverity severity)
    {
        try
        {
            // Use PowerShell to show Windows Toast notification
            var iconPath = GetNotificationIcon(severity);
            var powershellScript = $@"
                Add-Type -AssemblyName System.Windows.Forms
                $notification = New-Object System.Windows.Forms.NotifyIcon
                $notification.Icon = [System.Drawing.SystemIcons]::{GetSystemIcon(severity)}
                $notification.BalloonTipTitle = '{EscapeForPowerShell(title)}'
                $notification.BalloonTipText = '{EscapeForPowerShell(message)}'
                $notification.BalloonTipIcon = '{GetBalloonTipIcon(severity)}'
                $notification.Visible = $true
                $notification.ShowBalloonTip(5000)
                Start-Sleep -Seconds 1
                $notification.Dispose()
            ";

            using var process = new Process();
            process.StartInfo.FileName = "powershell.exe";
            process.StartInfo.Arguments = $"-Command \"{powershellScript}\"";
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            
            process.Start();
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to show Windows toast notification");
            throw;
        }
    }

    private string EscapeForPowerShell(string input)
    {
        return input.Replace("'", "''").Replace("`", "``");
    }

    private string GetSystemIcon(NotificationSeverity severity)
    {
        return severity switch
        {
            NotificationSeverity.Information => "Information",
            NotificationSeverity.Success => "Information",
            NotificationSeverity.Warning => "Warning",
            NotificationSeverity.Error => "Error",
            _ => "Information"
        };
    }

    private string GetBalloonTipIcon(NotificationSeverity severity)
    {
        return severity switch
        {
            NotificationSeverity.Information => "Info",
            NotificationSeverity.Success => "Info",
            NotificationSeverity.Warning => "Warning",
            NotificationSeverity.Error => "Error",
            _ => "Info"
        };
    }

    private string GetNotificationIcon(NotificationSeverity severity)
    {
        return severity switch
        {
            NotificationSeverity.Information => "ℹ️",
            NotificationSeverity.Success => "✅",
            NotificationSeverity.Warning => "⚠️",
            NotificationSeverity.Error => "❌",
            _ => "ℹ️"
        };
    }

    private MessageBoxImage GetMessageBoxIcon(NotificationSeverity severity)
    {
        return severity switch
        {
            NotificationSeverity.Information => MessageBoxImage.Information,
            NotificationSeverity.Warning => MessageBoxImage.Warning,
            NotificationSeverity.Error => MessageBoxImage.Error,
            NotificationSeverity.Success => MessageBoxImage.Information,
            _ => MessageBoxImage.Information
        };
    }
}

public enum NotificationSeverity
{
    Information,
    Success,
    Warning,
    Error
}