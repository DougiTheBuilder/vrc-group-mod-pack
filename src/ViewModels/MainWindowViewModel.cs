using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.Enforcement;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Services.Instances;
using VrcGroupGuardian.Views;

namespace VrcGroupGuardian.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IAuthService _authService;
    private readonly IGroupService _groupService;
    private readonly IInstancesService _instancesService;
    private readonly IEnforcementService _enforcementService;
    
    private object? _currentView;
    private string _currentViewTitle = "Dashboard";
    private string _currentViewSubtitle = "";
    private string _currentGroupName = "No Group Selected";
    private bool _isAuthenticated;
    private bool _isMonitoring;
    private bool _isEnforcementActive;
    private readonly Timer _statusUpdateTimer;

    public MainWindowViewModel(
        IAuthService authService,
        IGroupService groupService,
        IInstancesService instancesService,
        IEnforcementService enforcementService)
    {
        _authService = authService;
        _groupService = groupService;
        _instancesService = instancesService;
        _enforcementService = enforcementService;
        
        InitializeCommands();
        
        // Start status update timer
        _statusUpdateTimer = new Timer(UpdateStatus, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        
        // Initialize with settings view
        NavigateToSettings();
    }

    public object? CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public string CurrentViewTitle
    {
        get => _currentViewTitle;
        set => SetProperty(ref _currentViewTitle, value);
    }

    public string CurrentViewSubtitle
    {
        get => _currentViewSubtitle;
        set => SetProperty(ref _currentViewSubtitle, value);
    }

    public string CurrentGroupName
    {
        get => _currentGroupName;
        set => SetProperty(ref _currentGroupName, value);
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set => SetProperty(ref _isAuthenticated, value);
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        set => SetProperty(ref _isMonitoring, value);
    }

    public bool IsEnforcementActive
    {
        get => _isEnforcementActive;
        set => SetProperty(ref _isEnforcementActive, value);
    }

    public string AuthenticationStatus => IsAuthenticated ? "Authenticated" : "Not Authenticated";
    public string MonitoringStatus => IsMonitoring ? "Monitoring Active" : "Monitoring Stopped";
    public string EnforcementStatus => IsEnforcementActive ? "Enforcement Active" : "Enforcement Disabled";

    public ICommand NavigateToInstancesCommand { get; private set; } = null!;
    public ICommand NavigateToMembersCommand { get; private set; } = null!;
    public ICommand NavigateToAuditCommand { get; private set; } = null!;
    public ICommand NavigateToSettingsCommand { get; private set; } = null!;
    public ICommand RefreshCommand { get; private set; } = null!;
    public ICommand EmergencyStopCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        NavigateToInstancesCommand = new RelayCommand(NavigateToInstances);
        NavigateToMembersCommand = new RelayCommand(NavigateToMembers);
        NavigateToAuditCommand = new RelayCommand(NavigateToAudit);
        NavigateToSettingsCommand = new RelayCommand(NavigateToSettings);
        RefreshCommand = new RelayCommand(async () => await RefreshCurrentView());
        EmergencyStopCommand = new RelayCommand(async () => await EmergencyStop());
    }

    private void NavigateToInstances()
    {
        try
        {
            var instancesViewModel = App.GetRequiredService<InstancesViewModel>();
            var instancesView = new InstancesView { DataContext = instancesViewModel };
            CurrentView = instancesView;
            CurrentViewTitle = "Instances";
            CurrentViewSubtitle = "Monitor and manage group instances";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to navigate to instances: {ex.Message}");
        }
    }

    private void NavigateToMembers()
    {
        try
        {
            var membersViewModel = App.GetRequiredService<MembersViewModel>();
            var membersView = new MembersView { DataContext = membersViewModel };
            CurrentView = membersView;
            CurrentViewTitle = "Members";
            CurrentViewSubtitle = "Manage group members";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to navigate to members: {ex.Message}");
        }
    }

    private void NavigateToAudit()
    {
        try
        {
            var auditViewModel = App.GetRequiredService<AuditViewModel>();
            var auditView = new AuditView { DataContext = auditViewModel };
            CurrentView = auditView;
            CurrentViewTitle = "Audit Trail";
            CurrentViewSubtitle = "View and export audit records";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to navigate to audit: {ex.Message}");
        }
    }

    private void NavigateToSettings()
    {
        try
        {
            var settingsViewModel = App.GetRequiredService<SettingsViewModel>();
            var settingsView = new SettingsView { DataContext = settingsViewModel };
            CurrentView = settingsView;
            CurrentViewTitle = "Settings";
            CurrentViewSubtitle = "Authentication and policy configuration";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to navigate to settings: {ex.Message}");
        }
    }

    private async Task RefreshCurrentView()
    {
        // Refresh the current view's data
        if (CurrentView is IRefreshable refreshableView)
        {
            await refreshableView.RefreshAsync();
        }
        
        // Update status immediately
        await UpdateStatusAsync();
    }

    private async Task EmergencyStop()
    {
        try
        {
            // Stop all active services
            await _enforcementService.StopEnforcementAsync();
            await _instancesService.StopMonitoringAsync();
            
            await UpdateStatusAsync();
        }
        catch (Exception ex)
        {
            // Log error or show message to user
            System.Diagnostics.Debug.WriteLine($"Emergency stop failed: {ex.Message}");
        }
    }

    private async void UpdateStatus(object? state)
    {
        await UpdateStatusAsync();
    }

    private async Task UpdateStatusAsync()
    {
        try
        {
            IsAuthenticated = await _authService.IsAuthenticatedAsync();
            IsMonitoring = await _instancesService.IsMonitoringAsync();
            IsEnforcementActive = await _enforcementService.IsEnforcementActiveAsync();
            
            var selectedGroup = await _groupService.GetSelectedGroupAsync();
            CurrentGroupName = selectedGroup?.GroupName ?? "No Group Selected";
            
            // Notify property changes for computed properties
            OnPropertyChanged(nameof(AuthenticationStatus));
            OnPropertyChanged(nameof(MonitoringStatus));
            OnPropertyChanged(nameof(EnforcementStatus));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Status update failed: {ex.Message}");
        }
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

    public void Dispose()
    {
        _statusUpdateTimer?.Dispose();
    }
}

// Interface for views that support refresh
public interface IRefreshable
{
    Task RefreshAsync();
}

// Simple RelayCommand implementation
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}

public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;
        
        _isExecuting = true;
        CommandManager.InvalidateRequerySuggested();
        
        try
        {
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}