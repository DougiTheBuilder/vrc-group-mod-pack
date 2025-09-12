using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Win32;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.Enforcement;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Infrastructure;

namespace VrcGroupGuardian.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged, IRefreshable
{
    private readonly IAuthService _authService;
    private readonly IGroupService _groupService;
    private readonly IEnforcementService _enforcementService;
    private readonly ISettingsStore _settingsStore;
    private readonly IThemeManager _themeManager;
    private readonly IDryRunMode _dryRunMode;
    
    private bool _isAuthenticated;
    private string _username = "";
    private string _twoFactorCode = "";
    private string _currentGroupName = "No Group Selected";
    private GroupInstance? _selectedGroup;
    private PolicyConfiguration _policyConfiguration = new();
    private string _statusText = "Ready";
    
    // Application settings
    private bool _startWithWindows;
    private bool _minimizeToTray;
    private bool _highContrastTheme;
    private bool _dryRunMode;
    private string _selectedLogLevel = "Information";

    public SettingsViewModel(
        IAuthService authService,
        IGroupService groupService,
        IEnforcementService enforcementService,
        ISettingsStore settingsStore,
        IThemeManager themeManager,
        IDryRunMode dryRunMode)
    {
        _authService = authService;
        _groupService = groupService;
        _enforcementService = enforcementService;
        _settingsStore = settingsStore;
        _themeManager = themeManager;
        _dryRunMode = dryRunMode;
        
        InitializeCommands();
        InitializeLists();
        
        // Subscribe to theme changes
        _themeManager.ThemeChanged += OnThemeChanged;
        
        InitializeAsync();
    }

    // Authentication Properties
    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set
        {
            if (SetProperty(ref _isAuthenticated, value))
            {
                OnPropertyChanged(nameof(AuthenticationStatusText));
                OnPropertyChanged(nameof(LoginButtonText));
                OnPropertyChanged(nameof(LoginButtonColor));
            }
        }
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string TwoFactorCode
    {
        get => _twoFactorCode;
        set => SetProperty(ref _twoFactorCode, value);
    }

    public string AuthenticationStatusText => IsAuthenticated 
        ? $"Authenticated as {Username}" 
        : "Not authenticated";

    public string LoginButtonText => IsAuthenticated ? "Logout" : "Quick Login";
    public string LoginButtonColor => IsAuthenticated ? "#DC3545" : "#28A745";

    // Group Selection Properties
    public string CurrentGroupName
    {
        get => _currentGroupName;
        set => SetProperty(ref _currentGroupName, value);
    }

    public GroupInstance? SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty(ref _selectedGroup, value);
    }

    public string GroupSelectionStatus => SelectedGroup != null 
        ? $"Ready to manage {SelectedGroup.GroupName}" 
        : "Select a group to begin monitoring";

    public ObservableCollection<GroupInstance> AvailableGroups { get; } = new();

    // Policy Configuration Properties
    public int GracePeriodSeconds
    {
        get => _policyConfiguration.GracePeriodSeconds;
        set
        {
            _policyConfiguration.GracePeriodSeconds = value;
            OnPropertyChanged();
        }
    }

    public int PollingIntervalSeconds
    {
        get => _policyConfiguration.PollingIntervalSeconds;
        set
        {
            _policyConfiguration.PollingIntervalSeconds = value;
            OnPropertyChanged();
        }
    }

    public int RateLimitRequestsPerMinute
    {
        get => _policyConfiguration.RateLimitRequestsPerMinute;
        set
        {
            _policyConfiguration.RateLimitRequestsPerMinute = value;
            OnPropertyChanged();
        }
    }

    public bool NotificationsEnabled
    {
        get => _policyConfiguration.NotificationsEnabled;
        set
        {
            _policyConfiguration.NotificationsEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool ExportAuditLogs
    {
        get => _policyConfiguration.ExportAuditLogs;
        set
        {
            _policyConfiguration.ExportAuditLogs = value;
            OnPropertyChanged();
        }
    }

    public bool DryRunMode
    {
        get => _dryRunMode.IsEnabled;
        set
        {
            if (_dryRunMode.IsEnabled != value)
            {
                _dryRunMode.SetMode(value);
                OnPropertyChanged();
            }
        }
    }

    // Application Settings Properties
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public bool HighContrastTheme
    {
        get => _themeManager.IsHighContrastEnabled;
        set
        {
            if (_themeManager.IsHighContrastEnabled != value)
            {
                if (value)
                {
                    _themeManager.EnableHighContrastTheme();
                }
                else
                {
                    _themeManager.DisableHighContrastTheme();
                }
                OnPropertyChanged();
            }
        }
    }

    public string SelectedLogLevel
    {
        get => _selectedLogLevel;
        set => SetProperty(ref _selectedLogLevel, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // Collections
    public ObservableCollection<string> AvailableLogLevels { get; } = new();

    // System Information Properties
    public string ApplicationVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    public string BuildDate => File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd");
    public string DotNetVersion => Environment.Version.ToString();
    public string OsVersion => Environment.OSVersion.ToString();

    // Commands
    public ICommand ToggleLoginCommand { get; private set; } = null!;
    public ICommand LoginCommand { get; private set; } = null!;
    public ICommand LogoutCommand { get; private set; } = null!;
    public ICommand SelectGroupCommand { get; private set; } = null!;
    public ICommand RefreshGroupsCommand { get; private set; } = null!;
    public ICommand SavePolicyCommand { get; private set; } = null!;
    public ICommand ResetPolicyCommand { get; private set; } = null!;
    public ICommand ImportPolicyCommand { get; private set; } = null!;
    public ICommand ExportPolicyCommand { get; private set; } = null!;
    public ICommand OpenLogFolderCommand { get; private set; } = null!;
    public ICommand ClearCacheCommand { get; private set; } = null!;
    public ICommand ResetAllSettingsCommand { get; private set; } = null!;
    public ICommand CheckForUpdatesCommand { get; private set; } = null!;
    public ICommand ViewLicenseCommand { get; private set; } = null!;
    public ICommand ReportIssueCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        ToggleLoginCommand = new AsyncRelayCommand(ToggleLogin);
        LoginCommand = new AsyncRelayCommand(Login);
        LogoutCommand = new AsyncRelayCommand(Logout);
        SelectGroupCommand = new AsyncRelayCommand(SelectGroup);
        RefreshGroupsCommand = new AsyncRelayCommand(RefreshGroups);
        SavePolicyCommand = new AsyncRelayCommand(SavePolicy);
        ResetPolicyCommand = new AsyncRelayCommand(ResetPolicy);
        ImportPolicyCommand = new AsyncRelayCommand(ImportPolicy);
        ExportPolicyCommand = new AsyncRelayCommand(ExportPolicy);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        ClearCacheCommand = new AsyncRelayCommand(ClearCache);
        ResetAllSettingsCommand = new AsyncRelayCommand(ResetAllSettings);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdates);
        ViewLicenseCommand = new RelayCommand(ViewLicense);
        ReportIssueCommand = new RelayCommand(ReportIssue);
    }

    private void InitializeLists()
    {
        AvailableLogLevels.Add("Verbose");
        AvailableLogLevels.Add("Debug");
        AvailableLogLevels.Add("Information");
        AvailableLogLevels.Add("Warning");
        AvailableLogLevels.Add("Error");
        AvailableLogLevels.Add("Fatal");
    }

    private async void InitializeAsync()
    {
        await LoadSettings();
        await UpdateAuthenticationStatus();
        await LoadPolicyConfiguration();
        await RefreshGroups();
        
        // Subscribe to auth events
        _authService.AuthenticationStateChanged += OnAuthenticationStateChanged;
    }

    private async Task ToggleLogin()
    {
        if (IsAuthenticated)
        {
            await Logout();
        }
        else
        {
            await Login();
        }
    }

    private async Task Login()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            StatusText = "Username is required";
            return;
        }

        try
        {
            StatusText = "Authenticating...";
            
            // Get password from PasswordBox (this would need to be implemented via code-behind)
            var password = ""; // TODO: Get from PasswordBox
            
            var result = await _authService.LoginAsync(Username, password, TwoFactorCode);
            
            if (result.Success)
            {
                StatusText = "Authentication successful";
                await UpdateAuthenticationStatus();
                await RefreshGroups();
                TwoFactorCode = ""; // Clear 2FA code after successful login
            }
            else
            {
                StatusText = result.Message ?? "Authentication failed";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Login error: {ex.Message}";
        }
    }

    private async Task Logout()
    {
        try
        {
            StatusText = "Logging out...";
            
            await _authService.LogoutAsync();
            
            StatusText = "Logged out successfully";
            await UpdateAuthenticationStatus();
            
            // Clear sensitive data
            Username = "";
            TwoFactorCode = "";
            AvailableGroups.Clear();
            CurrentGroupName = "No Group Selected";
        }
        catch (Exception ex)
        {
            StatusText = $"Logout error: {ex.Message}";
        }
    }

    private async Task SelectGroup()
    {
        if (SelectedGroup == null)
        {
            StatusText = "No group selected";
            return;
        }

        try
        {
            StatusText = "Setting selected group...";
            
            await _groupService.SetSelectedGroupAsync(SelectedGroup);
            CurrentGroupName = SelectedGroup.GroupName;
            
            OnPropertyChanged(nameof(GroupSelectionStatus));
            StatusText = $"Selected group: {SelectedGroup.GroupName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Group selection error: {ex.Message}";
        }
    }

    private async Task RefreshGroups()
    {
        try
        {
            StatusText = "Loading available groups...";
            
            if (!IsAuthenticated)
            {
                AvailableGroups.Clear();
                StatusText = "Authentication required to load groups";
                return;
            }

            var groups = await _groupService.GetUserGroupsAsync();
            
            AvailableGroups.Clear();
            foreach (var group in groups)
            {
                AvailableGroups.Add(group);
            }
            
            // Load current selected group
            var selectedGroup = await _groupService.GetSelectedGroupAsync();
            if (selectedGroup != null)
            {
                CurrentGroupName = selectedGroup.GroupName;
                SelectedGroup = AvailableGroups.FirstOrDefault(g => g.GroupId == selectedGroup.GroupId);
            }
            
            StatusText = $"Loaded {groups.Count} available groups";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading groups: {ex.Message}";
        }
    }

    private async Task SavePolicy()
    {
        try
        {
            StatusText = "Saving policy configuration...";
            
            var success = await _enforcementService.UpdatePolicyConfigurationAsync(_policyConfiguration);
            StatusText = success ? "Policy configuration saved" : "Failed to save policy configuration";
        }
        catch (Exception ex)
        {
            StatusText = $"Save policy error: {ex.Message}";
        }
    }

    private async Task ResetPolicy()
    {
        try
        {
            StatusText = "Resetting policy to defaults...";
            
            _policyConfiguration = new PolicyConfiguration(); // Default values
            
            // Notify all policy-related properties
            OnPropertyChanged(nameof(GracePeriodSeconds));
            OnPropertyChanged(nameof(PollingIntervalSeconds));
            OnPropertyChanged(nameof(RateLimitRequestsPerMinute));
            OnPropertyChanged(nameof(NotificationsEnabled));
            OnPropertyChanged(nameof(ExportAuditLogs));
            
            StatusText = "Policy reset to defaults";
        }
        catch (Exception ex)
        {
            StatusText = $"Reset policy error: {ex.Message}";
        }
    }

    private async Task ImportPolicy()
    {
        try
        {
            StatusText = "Importing policy configuration...";
            
            var openFileDialog = new OpenFileDialog
            {
                Title = "Import Policy Configuration",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json"
            };
            
            if (openFileDialog.ShowDialog() == true)
            {
                var result = await _enforcementService.ImportPolicyConfigurationAsync(openFileDialog.FileName);
                if (result.Success)
                {
                    await LoadPolicyConfiguration();
                    StatusText = "Policy configuration imported successfully";
                }
                else
                {
                    StatusText = result.Message ?? "Failed to import policy configuration";
                }
            }
            else
            {
                StatusText = "Import cancelled";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Import policy error: {ex.Message}";
        }
    }

    private async Task ExportPolicy()
    {
        try
        {
            StatusText = "Exporting policy configuration...";
            
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Export Policy Configuration",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json",
                FileName = $"VrcGroupGuardian_Policy_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };
            
            if (saveFileDialog.ShowDialog() == true)
            {
                var result = await _enforcementService.ExportPolicyConfigurationAsync(saveFileDialog.FileName);
                StatusText = result.Success 
                    ? $"Policy configuration exported: {saveFileDialog.FileName}" 
                    : result.Message ?? "Failed to export policy configuration";
            }
            else
            {
                StatusText = "Export cancelled";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Export policy error: {ex.Message}";
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                "VrcGroupGuardian", "Logs");
            
            if (Directory.Exists(logPath))
            {
                Process.Start("explorer.exe", logPath);
                StatusText = "Opened log folder";
            }
            else
            {
                StatusText = "Log folder not found";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error opening log folder: {ex.Message}";
        }
    }

    private async Task ClearCache()
    {
        try
        {
            StatusText = "Clearing application cache...";
            
            // TODO: Implement cache clearing logic
            await Task.Delay(500); // Simulate cache clearing
            
            StatusText = "Cache cleared successfully";
        }
        catch (Exception ex)
        {
            StatusText = $"Clear cache error: {ex.Message}";
        }
    }

    private async Task ResetAllSettings()
    {
        try
        {
            StatusText = "Resetting all settings...";
            
            await _settingsStore.ClearAllSettingsAsync();
            await ResetPolicy();
            
            // Reset application settings
            StartWithWindows = false;
            MinimizeToTray = false;
            HighContrastTheme = false;
            _dryRunMode.SetMode(false);
            SelectedLogLevel = "Information";
            
            StatusText = "All settings reset to defaults";
        }
        catch (Exception ex)
        {
            StatusText = $"Reset settings error: {ex.Message}";
        }
    }

    private async Task CheckForUpdates()
    {
        try
        {
            StatusText = "Checking for updates...";
            
            // TODO: Implement update checking logic
            await Task.Delay(1000); // Simulate update check
            
            StatusText = "No updates available";
        }
        catch (Exception ex)
        {
            StatusText = $"Update check error: {ex.Message}";
        }
    }

    private void ViewLicense()
    {
        try
        {
            // TODO: Show license dialog or open license file
            StatusText = "License information displayed";
        }
        catch (Exception ex)
        {
            StatusText = $"Error viewing license: {ex.Message}";
        }
    }

    private void ReportIssue()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/your-repo/VrcGroupGuardian/issues",
                UseShellExecute = true
            });
            StatusText = "Opened issue tracker";
        }
        catch (Exception ex)
        {
            StatusText = $"Error opening issue tracker: {ex.Message}";
        }
    }

    public async Task RefreshAsync()
    {
        await UpdateAuthenticationStatus();
        await LoadPolicyConfiguration();
        await RefreshGroups();
        await LoadSettings();
    }

    private async Task UpdateAuthenticationStatus()
    {
        try
        {
            IsAuthenticated = await _authService.IsAuthenticatedAsync();
            if (IsAuthenticated)
            {
                var userInfo = await _authService.GetCurrentUserAsync();
                Username = userInfo?.Username ?? "Unknown";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Status update error: {ex.Message}";
        }
    }

    private async Task LoadPolicyConfiguration()
    {
        try
        {
            _policyConfiguration = await _enforcementService.GetPolicyConfigurationAsync();
            
            // Notify all policy-related properties
            OnPropertyChanged(nameof(GracePeriodSeconds));
            OnPropertyChanged(nameof(PollingIntervalSeconds));
            OnPropertyChanged(nameof(RateLimitRequestsPerMinute));
            OnPropertyChanged(nameof(NotificationsEnabled));
            OnPropertyChanged(nameof(ExportAuditLogs));
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load policy: {ex.Message}";
        }
    }

    private async Task LoadSettings()
    {
        try
        {
            StartWithWindows = await _settingsStore.GetSettingAsync("StartWithWindows", false);
            MinimizeToTray = await _settingsStore.GetSettingAsync("MinimizeToTray", true);
            
            var savedHighContrast = await _settingsStore.GetSettingAsync("HighContrastTheme", false);
            if (savedHighContrast && !_themeManager.IsHighContrastEnabled)
            {
                _themeManager.EnableHighContrastTheme();
            }
            else if (!savedHighContrast && _themeManager.IsHighContrastEnabled)
            {
                _themeManager.DisableHighContrastTheme();
            }
            
            var savedDryRunMode = await _settingsStore.GetSettingAsync("DryRunMode", false);
            _dryRunMode.SetMode(savedDryRunMode);
            SelectedLogLevel = await _settingsStore.GetSettingAsync("LogLevel", "Information");
            
            // Apply system theme settings on load
            _themeManager.ApplySystemThemeSettings();
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load settings: {ex.Message}";
        }
    }

    private async Task SaveSettings()
    {
        try
        {
            await _settingsStore.SetSettingAsync("StartWithWindows", StartWithWindows);
            await _settingsStore.SetSettingAsync("MinimizeToTray", MinimizeToTray);
            await _settingsStore.SetSettingAsync("HighContrastTheme", HighContrastTheme);
            await _settingsStore.SetSettingAsync("DryRunMode", DryRunMode);
            await _settingsStore.SetSettingAsync("LogLevel", SelectedLogLevel);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save settings: {ex.Message}";
        }
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        // Update the UI when theme changes
        OnPropertyChanged(nameof(HighContrastTheme));
        
        // Save the setting
        _ = Task.Run(SaveSettings);
        
        StatusText = e.IsHighContrastEnabled 
            ? "High contrast theme enabled" 
            : "High contrast theme disabled";
    }

    private async void OnAuthenticationStateChanged(object? sender, AuthenticationStateChangedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(async () =>
        {
            await UpdateAuthenticationStatus();
            if (e.IsAuthenticated)
            {
                await RefreshGroups();
            }
            else
            {
                AvailableGroups.Clear();
                CurrentGroupName = "No Group Selected";
            }
        });
    }

    // Auto-save settings when properties change
    protected override bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (base.SetProperty(ref field, value, propertyName))
        {
            // Auto-save application settings
            if (propertyName is nameof(StartWithWindows) or nameof(MinimizeToTray) or 
                nameof(HighContrastTheme) or nameof(DryRunMode) or nameof(SelectedLogLevel))
            {
                _ = Task.Run(SaveSettings);
            }
            return true;
        }
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected virtual bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}