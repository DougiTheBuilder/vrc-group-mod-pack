using System.Windows;
using System.Windows.Media;
using Serilog;

namespace VrcGroupGuardian.Infrastructure;

public interface IThemeManager
{
    bool IsHighContrastEnabled { get; }
    void EnableHighContrastTheme();
    void DisableHighContrastTheme();
    void ToggleHighContrastTheme();
    void ApplySystemThemeSettings();
    event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
}

public class ThemeManager : IThemeManager
{
    private readonly ILogger _logger = Log.ForContext<ThemeManager>();
    private bool _isHighContrastEnabled;

    public bool IsHighContrastEnabled
    {
        get => _isHighContrastEnabled;
        private set
        {
            if (_isHighContrastEnabled != value)
            {
                _isHighContrastEnabled = value;
                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(_isHighContrastEnabled));
            }
        }
    }

    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public void EnableHighContrastTheme()
    {
        try
        {
            if (IsHighContrastEnabled)
            {
                _logger.Debug("High contrast theme already enabled");
                return;
            }

            _logger.Information("Enabling high contrast theme");
            
            // Load high contrast resource dictionary
            var highContrastDict = new ResourceDictionary();
            highContrastDict.Source = new Uri("/Themes/HighContrastTheme.xaml", UriKind.Relative);
            
            // Remove any existing theme dictionaries
            RemoveExistingThemes();
            
            // Add high contrast dictionary to application resources
            Application.Current.Resources.MergedDictionaries.Add(highContrastDict);
            
            IsHighContrastEnabled = true;
            
            // Apply theme to all open windows
            ApplyThemeToAllWindows();
            
            _logger.Information("High contrast theme enabled successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to enable high contrast theme");
            throw;
        }
    }

    public void DisableHighContrastTheme()
    {
        try
        {
            if (!IsHighContrastEnabled)
            {
                _logger.Debug("High contrast theme already disabled");
                return;
            }

            _logger.Information("Disabling high contrast theme");
            
            // Remove high contrast theme
            RemoveExistingThemes();
            
            IsHighContrastEnabled = false;
            
            // Apply default theme to all open windows
            ApplyThemeToAllWindows();
            
            _logger.Information("High contrast theme disabled successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to disable high contrast theme");
            throw;
        }
    }

    public void ToggleHighContrastTheme()
    {
        if (IsHighContrastEnabled)
        {
            DisableHighContrastTheme();
        }
        else
        {
            EnableHighContrastTheme();
        }
    }

    public void ApplySystemThemeSettings()
    {
        try
        {
            _logger.Debug("Applying system theme settings");
            
            // Check if Windows High Contrast is enabled
            var isSystemHighContrast = IsSystemHighContrastEnabled();
            
            if (isSystemHighContrast && !IsHighContrastEnabled)
            {
                _logger.Information("System high contrast detected, enabling application high contrast");
                EnableHighContrastTheme();
            }
            else if (!isSystemHighContrast && IsHighContrastEnabled)
            {
                _logger.Information("System high contrast disabled, disabling application high contrast");
                DisableHighContrastTheme();
            }
            
            // Apply additional accessibility settings
            ApplyAccessibilitySettings();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to apply system theme settings");
        }
    }

    private void RemoveExistingThemes()
    {
        try
        {
            // Remove any existing theme resource dictionaries
            var toRemove = Application.Current.Resources.MergedDictionaries
                .Where(dict => dict.Source?.OriginalString?.Contains("Theme.xaml") == true)
                .ToList();

            foreach (var dict in toRemove)
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                _logger.Debug("Removed theme dictionary: {Source}", dict.Source);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error removing existing themes");
        }
    }

    private void ApplyThemeToAllWindows()
    {
        try
        {
            foreach (Window window in Application.Current.Windows)
            {
                ApplyThemeToWindow(window);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error applying theme to windows");
        }
    }

    private void ApplyThemeToWindow(Window window)
    {
        try
        {
            if (IsHighContrastEnabled)
            {
                // Apply high contrast styles
                if (Application.Current.Resources.Contains("HighContrastWindowStyle"))
                {
                    window.Style = (Style)Application.Current.Resources["HighContrastWindowStyle"];
                }
                
                // Update window properties for accessibility
                window.FontSize = 16;
                window.FontWeight = FontWeights.SemiBold;
                
                ApplyHighContrastToWindowContent(window);
            }
            else
            {
                // Reset to default styles
                window.ClearValue(Window.StyleProperty);
                window.ClearValue(Window.FontSizeProperty);
                window.ClearValue(Window.FontWeightProperty);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error applying theme to window {WindowType}", window.GetType().Name);
        }
    }

    private void ApplyHighContrastToWindowContent(Window window)
    {
        try
        {
            // Apply high contrast styles to all child controls
            ApplyHighContrastToElement(window);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error applying high contrast to window content");
        }
    }

    private void ApplyHighContrastToElement(DependencyObject element)
    {
        try
        {
            // Apply styles based on element type
            switch (element)
            {
                case System.Windows.Controls.Button button:
                    if (Application.Current.Resources.Contains("HighContrastButtonStyle"))
                    {
                        button.Style = (Style)Application.Current.Resources["HighContrastButtonStyle"];
                    }
                    break;
                
                case System.Windows.Controls.TextBox textBox:
                    if (Application.Current.Resources.Contains("HighContrastTextBoxStyle"))
                    {
                        textBox.Style = (Style)Application.Current.Resources["HighContrastTextBoxStyle"];
                    }
                    break;
                
                case System.Windows.Controls.Label label:
                    if (Application.Current.Resources.Contains("HighContrastLabelStyle"))
                    {
                        label.Style = (Style)Application.Current.Resources["HighContrastLabelStyle"];
                    }
                    break;
                
                case System.Windows.Controls.TextBlock textBlock:
                    if (Application.Current.Resources.Contains("HighContrastTextBlockStyle"))
                    {
                        textBlock.Style = (Style)Application.Current.Resources["HighContrastTextBlockStyle"];
                    }
                    break;
                
                case System.Windows.Controls.DataGrid dataGrid:
                    if (Application.Current.Resources.Contains("HighContrastDataGridStyle"))
                    {
                        dataGrid.Style = (Style)Application.Current.Resources["HighContrastDataGridStyle"];
                    }
                    break;
                
                case System.Windows.Controls.ComboBox comboBox:
                    if (Application.Current.Resources.Contains("HighContrastComboBoxStyle"))
                    {
                        comboBox.Style = (Style)Application.Current.Resources["HighContrastComboBoxStyle"];
                    }
                    break;
                
                case System.Windows.Controls.CheckBox checkBox:
                    if (Application.Current.Resources.Contains("HighContrastCheckBoxStyle"))
                    {
                        checkBox.Style = (Style)Application.Current.Resources["HighContrastCheckBoxStyle"];
                    }
                    break;
                
                case System.Windows.Controls.Border border:
                    if (Application.Current.Resources.Contains("HighContrastBorderStyle"))
                    {
                        border.Style = (Style)Application.Current.Resources["HighContrastBorderStyle"];
                    }
                    break;
            }

            // Recursively apply to child elements
            var childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                ApplyHighContrastToElement(child);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error applying high contrast to element {ElementType}", element.GetType().Name);
        }
    }

    private bool IsSystemHighContrastEnabled()
    {
        try
        {
            // Check Windows system settings for high contrast
            return SystemParameters.HighContrast;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not determine system high contrast setting");
            return false;
        }
    }

    private void ApplyAccessibilitySettings()
    {
        try
        {
            // Apply additional accessibility features based on system settings
            
            // Keyboard navigation improvements
            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.KeyboardNavigation.TabNavigation = System.Windows.Input.KeyboardNavigationMode.Cycle;
                Application.Current.MainWindow.KeyboardNavigation.DirectionalNavigation = System.Windows.Input.KeyboardNavigationMode.Cycle;
            }
            
            // Focus visualization improvements are handled by the high contrast theme
            
            _logger.Debug("Applied accessibility settings");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error applying accessibility settings");
        }
    }
}

public class ThemeChangedEventArgs : EventArgs
{
    public bool IsHighContrastEnabled { get; }

    public ThemeChangedEventArgs(bool isHighContrastEnabled)
    {
        IsHighContrastEnabled = isHighContrastEnabled;
    }
}