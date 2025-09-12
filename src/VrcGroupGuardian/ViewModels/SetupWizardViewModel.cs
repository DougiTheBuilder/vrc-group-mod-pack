using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.Enforcement;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Infrastructure;

namespace VrcGroupGuardian.ViewModels;

public class SetupWizardViewModel : INotifyPropertyChanged
{
    private readonly IAuthService _authService;
    private readonly IGroupService _groupService;
    private readonly IEnforcementService _enforcementService;
    private readonly ISettingsStore _settingsStore;
    
    private int _currentStep = 1;
    private string _username = "";
    private string _password = "";
    private string _twoFactorCode = "";
    private string _authenticationStatus = "";
    private bool _isAuthenticated;
    private GroupInfo? _selectedGroup;
    private string _statusMessage = "Ready to begin setup";
    
    // Policy configuration
    private int _gracePeriodSeconds = 300;
    private int _pollingIntervalSeconds = 60;
    private int _rateLimitRequestsPerMinute = 20;
    private bool _notificationsEnabled = true;
    private bool _exportAuditLogs = false;

    public SetupWizardViewModel(
        IAuthService authService,
        IGroupService groupService,
        IEnforcementService enforcementService,
        ISettingsStore settingsStore)
    {
        _authService = authService;
        _groupService = groupService;
        _enforcementService = enforcementService;
        _settingsStore = settingsStore;
        
        InitializeCommands();
    }

    public bool IsSetupComplete { get; private set; }

    // Current step and navigation
    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            if (SetProperty(ref _currentStep, value))
            {
                UpdateStepVisibility();
                UpdateStepIndicators();
                UpdateNavigationButtons();
            }
        }
    }

    // Step visibility
    public Visibility Step1Visibility => CurrentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility Step2Visibility => CurrentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility Step3Visibility => CurrentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

    // Step indicators
    public Brush Step1Background => CurrentStep >= 1 ? Brushes.DodgerBlue : Brushes.Transparent;
    public Brush Step1Foreground => CurrentStep >= 1 ? Brushes.White : Brushes.Gray;
    public Brush Step2Background => CurrentStep >= 2 ? Brushes.DodgerBlue : Brushes.Transparent;
    public Brush Step2Foreground => CurrentStep >= 2 ? Brushes.White : Brushes.Gray;
    public Brush Step3Background => CurrentStep >= 3 ? Brushes.DodgerBlue : Brushes.Transparent;
    public Brush Step3Foreground => CurrentStep >= 3 ? Brushes.White : Brushes.Gray;

    public string CurrentStepDescription => CurrentStep switch
    {
        1 => "Step 1: Authenticate with VRChat",
        2 => "Step 2: Select your group to manage",
        3 => "Step 3: Configure monitoring policies",
        _ => ""
    };

    // Authentication properties
    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string TwoFactorCode
    {
        get => _twoFactorCode;
        set => SetProperty(ref _twoFactorCode, value);
    }

    public string AuthenticationStatus
    {
        get => _authenticationStatus;
        set => SetProperty(ref _authenticationStatus, value);
    }

    public Brush AuthenticationStatusColor => _isAuthenticated ? Brushes.Green : Brushes.Red;

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set
        {
            if (SetProperty(ref _isAuthenticated, value))
            {
                OnPropertyChanged(nameof(AuthenticationStatusColor));
                UpdateNavigationButtons();
            }
        }
    }

    // Group selection properties
    public ObservableCollection<GroupInfo> AvailableGroups { get; } = new();

    public GroupInfo? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                OnPropertyChanged(nameof(HasSelectedGroup));
                OnPropertyChanged(nameof(HasInsufficientPermissions));
                UpdateNavigationButtons();
            }
        }
    }

    public bool HasSelectedGroup => SelectedGroup != null;
    public bool HasInsufficientPermissions => SelectedGroup?.UserRole is "Member" or "Visitor";

    // Policy configuration properties
    public int GracePeriodSeconds
    {
        get => _gracePeriodSeconds;
        set => SetProperty(ref _gracePeriodSeconds, value);
    }

    public int PollingIntervalSeconds
    {
        get => _pollingIntervalSeconds;
        set => SetProperty(ref _pollingIntervalSeconds, value);
    }

    public int RateLimitRequestsPerMinute
    {
        get => _rateLimitRequestsPerMinute;
        set => SetProperty(ref _rateLimitRequestsPerMinute, value);
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => SetProperty(ref _notificationsEnabled, value);
    }

    public bool ExportAuditLogs
    {
        get => _exportAuditLogs;
        set => SetProperty(ref _exportAuditLogs, value);
    }

    // Navigation properties
    public bool CanGoBack => CurrentStep > 1;
    public bool CanGoNext => CurrentStep switch
    {
        1 => IsAuthenticated,
        2 => HasSelectedGroup && !HasInsufficientPermissions,
        3 => true,
        _ => false
    };

    public string NextButtonText => CurrentStep == 3 ? "Finish Setup" : "Next";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // Commands
    public ICommand TestConnectionCommand { get; private set; } = null!;
    public ICommand RefreshGroupsCommand { get; private set; } = null!;
    public ICommand UseRecommendedSettingsCommand { get; private set; } = null!;
    public ICommand PreviousStepCommand { get; private set; } = null!;
    public ICommand NextStepCommand { get; private set; } = null!;
    public ICommand CancelCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        TestConnectionCommand = new AsyncRelayCommand(TestConnection);
        RefreshGroupsCommand = new AsyncRelayCommand(RefreshGroups);
        UseRecommendedSettingsCommand = new RelayCommand(UseRecommendedSettings);
        PreviousStepCommand = new RelayCommand(PreviousStep, () => CanGoBack);
        NextStepCommand = new AsyncRelayCommand(NextStep, () => CanGoNext);
        CancelCommand = new RelayCommand(Cancel);
    }

    public async Task InitializeAsync()
    {
        StatusMessage = "Setup wizard initialized";
        
        // Check if already authenticated
        if (await _authService.IsAuthenticatedAsync())
        {
            IsAuthenticated = true;
            AuthenticationStatus = "Already authenticated";
            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser != null)
            {
                Username = currentUser.Username;
            }
        }
    }

    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            AuthenticationStatus = "Username and password are required";
            StatusMessage = "Please enter your credentials";
            return;
        }

        try
        {
            StatusMessage = "Testing VRChat connection...";
            AuthenticationStatus = "Authenticating...";
            
            var result = await _authService.LoginAsync(Username, Password, TwoFactorCode);
            
            if (result.Success)
            {
                IsAuthenticated = true;
                AuthenticationStatus = "✅ Authentication successful!";
                StatusMessage = "Connected to VRChat successfully";
                
                // Clear password for security
                Password = "";
                TwoFactorCode = "";
            }
            else
            {
                IsAuthenticated = false;
                AuthenticationStatus = $"❌ Authentication failed: {result.Message}";
                StatusMessage = "Authentication failed - please check your credentials";
            }
        }
        catch (Exception ex)
        {
            IsAuthenticated = false;
            AuthenticationStatus = $"❌ Connection error: {ex.Message}";
            StatusMessage = "Connection test failed";
        }
    }

    private async Task RefreshGroups()
    {
        if (!IsAuthenticated)
        {
            StatusMessage = "Please authenticate first";
            return;
        }

        try
        {
            StatusMessage = "Loading your VRChat groups...";
            
            var groups = await _groupService.GetUserGroupsAsync();
            
            AvailableGroups.Clear();
            foreach (var group in groups)
            {
                AvailableGroups.Add(group);
            }
            
            StatusMessage = $"Loaded {groups.Count} available groups";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load groups: {ex.Message}";
        }
    }

    private void UseRecommendedSettings()
    {
        GracePeriodSeconds = 300;
        PollingIntervalSeconds = 60;
        RateLimitRequestsPerMinute = 20;
        NotificationsEnabled = true;
        ExportAuditLogs = false;
        
        StatusMessage = "Applied recommended settings";
    }

    private void PreviousStep()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            StatusMessage = $"Moved to step {CurrentStep}";
        }
    }

    private async Task NextStep()
    {
        if (CurrentStep < 3)
        {
            // Perform step-specific actions before advancing
            switch (CurrentStep)
            {
                case 1:
                    if (!IsAuthenticated)
                    {
                        StatusMessage = "Please authenticate before continuing";
                        return;
                    }
                    await RefreshGroups();
                    break;
                
                case 2:
                    if (SelectedGroup == null)
                    {
                        StatusMessage = "Please select a group before continuing";
                        return;
                    }
                    if (HasInsufficientPermissions)
                    {
                        StatusMessage = "Insufficient permissions for selected group";
                        return;
                    }
                    break;
            }
            
            CurrentStep++;
            StatusMessage = $"Moved to step {CurrentStep}";
        }
        else
        {
            // Finish setup
            await FinishSetup();
        }
    }

    private async Task FinishSetup()
    {
        try
        {
            StatusMessage = "Completing setup...";
            
            // Save selected group
            if (SelectedGroup != null)
            {
                await _groupService.SetSelectedGroupAsync(SelectedGroup);
            }
            
            // Save policy configuration
            var policyConfig = new PolicyConfiguration
            {
                GracePeriodSeconds = GracePeriodSeconds,
                PollingIntervalSeconds = PollingIntervalSeconds,
                RateLimitRequestsPerMinute = RateLimitRequestsPerMinute,
                NotificationsEnabled = NotificationsEnabled,
                ExportAuditLogs = ExportAuditLogs
            };
            
            await _enforcementService.UpdatePolicyConfigurationAsync(policyConfig);
            
            // Mark setup as complete
            await _settingsStore.SetSettingAsync("SetupCompleted", true);
            await _settingsStore.SetSettingAsync("SetupCompletedDate", DateTime.Now);
            
            IsSetupComplete = true;
            StatusMessage = "Setup completed successfully!";
            
            // Close the wizard and show success message
            if (Application.Current.MainWindow is Window setupWindow)
            {
                setupWindow.DialogResult = true;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Setup failed: {ex.Message}";
        }
    }

    private void Cancel()
    {
        if (Application.Current.MainWindow is Window setupWindow)
        {
            setupWindow.DialogResult = false;
        }
    }

    private void UpdateStepVisibility()
    {
        OnPropertyChanged(nameof(Step1Visibility));
        OnPropertyChanged(nameof(Step2Visibility));
        OnPropertyChanged(nameof(Step3Visibility));
        OnPropertyChanged(nameof(CurrentStepDescription));
    }

    private void UpdateStepIndicators()
    {
        OnPropertyChanged(nameof(Step1Background));
        OnPropertyChanged(nameof(Step1Foreground));
        OnPropertyChanged(nameof(Step2Background));
        OnPropertyChanged(nameof(Step2Foreground));
        OnPropertyChanged(nameof(Step3Background));
        OnPropertyChanged(nameof(Step3Foreground));
    }

    private void UpdateNavigationButtons()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(NextButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}