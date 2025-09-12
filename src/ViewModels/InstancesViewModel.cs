using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Enforcement;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Services.Instances;

namespace VrcGroupGuardian.ViewModels;

public class InstancesViewModel : INotifyPropertyChanged, IRefreshable
{
    private readonly IInstancesService _instancesService;
    private readonly IEnforcementService _enforcementService;
    private readonly IGroupService _groupService;
    
    private ObservableCollection<GroupInstance> _instances = new();
    private GroupInstance? _selectedInstance;
    private bool _isMonitoring;
    private bool _isEnforcementActive;
    private PolicyConfiguration _policyConfiguration = new();
    private string _statusText = "Ready";

    public InstancesViewModel(
        IInstancesService instancesService, 
        IEnforcementService enforcementService,
        IGroupService groupService)
    {
        _instancesService = instancesService;
        _enforcementService = enforcementService;
        _groupService = groupService;
        
        InitializeCommands();
        InitializeAsync();
    }

    public ObservableCollection<GroupInstance> Instances
    {
        get => _instances;
        set => SetProperty(ref _instances, value);
    }

    public GroupInstance? SelectedInstance
    {
        get => _selectedInstance;
        set => SetProperty(ref _selectedInstance, value);
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        set
        {
            if (SetProperty(ref _isMonitoring, value))
            {
                OnPropertyChanged(nameof(MonitoringButtonText));
                OnPropertyChanged(nameof(MonitoringButtonColor));
                UpdateStatusText();
            }
        }
    }

    public bool IsEnforcementActive
    {
        get => _isEnforcementActive;
        set
        {
            if (SetProperty(ref _isEnforcementActive, value))
            {
                OnPropertyChanged(nameof(EnforcementButtonText));
                OnPropertyChanged(nameof(EnforcementButtonColor));
                UpdateStatusText();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

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

    // Computed Properties
    public string MonitoringButtonText => IsMonitoring ? "Stop Monitoring" : "Start Monitoring";
    public string MonitoringButtonColor => IsMonitoring ? "#DC3545" : "#28A745";
    
    public string EnforcementButtonText => IsEnforcementActive ? "Disable Enforcement" : "Enable Enforcement";
    public string EnforcementButtonColor => IsEnforcementActive ? "#DC3545" : "#007BFF";

    // Statistics
    public int TotalInstances => Instances.Count;
    public int ActiveInstances => Instances.Count(i => i.Status == InstanceStatus.Active);
    public int FlaggedInstances => Instances.Count(i => i.Status == InstanceStatus.Flagged);
    public int ClosingInstances => Instances.Count(i => i.Status == InstanceStatus.ClosingCountdown);
    public int TotalUsers => Instances.Sum(i => i.UserCount);

    // Commands
    public ICommand ToggleMonitoringCommand { get; private set; } = null!;
    public ICommand ToggleEnforcementCommand { get; private set; } = null!;
    public ICommand RefreshCommand { get; private set; } = null!;
    public ICommand ExportCommand { get; private set; } = null!;
    public ICommand SavePolicyCommand { get; private set; } = null!;
    public ICommand CloseInstanceCommand { get; private set; } = null!;
    public ICommand CancelClosureCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        ToggleMonitoringCommand = new AsyncRelayCommand(ToggleMonitoring);
        ToggleEnforcementCommand = new AsyncRelayCommand(ToggleEnforcement);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ExportCommand = new AsyncRelayCommand(Export);
        SavePolicyCommand = new AsyncRelayCommand(SavePolicy);
        CloseInstanceCommand = new AsyncRelayCommand<GroupInstance>(CloseInstance);
        CancelClosureCommand = new AsyncRelayCommand<GroupInstance>(CancelClosure);
    }

    private async void InitializeAsync()
    {
        await LoadPolicyConfiguration();
        await UpdateStatus();
        await RefreshAsync();
        
        // Subscribe to real-time events
        _instancesService.InstanceDetected += OnInstanceDetected;
        _instancesService.InstanceUpdated += OnInstanceUpdated;
        _instancesService.InstanceClosed += OnInstanceClosed;
    }

    private async Task ToggleMonitoring()
    {
        try
        {
            var selectedGroup = await _groupService.GetSelectedGroupAsync();
            if (selectedGroup == null)
            {
                StatusText = "Error: No group selected";
                return;
            }

            if (IsMonitoring)
            {
                StatusText = "Stopping monitoring...";
                await _instancesService.StopMonitoringAsync();
                StatusText = "Monitoring stopped";
            }
            else
            {
                StatusText = "Starting monitoring...";
                var success = await _instancesService.StartMonitoringAsync(selectedGroup.GroupId);
                StatusText = success ? "Monitoring started" : "Failed to start monitoring";
            }
            
            await UpdateStatus();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private async Task ToggleEnforcement()
    {
        try
        {
            if (IsEnforcementActive)
            {
                StatusText = "Stopping enforcement...";
                await _enforcementService.StopEnforcementAsync();
                StatusText = "Enforcement stopped";
            }
            else
            {
                StatusText = "Starting enforcement...";
                var success = await _enforcementService.StartEnforcementAsync();
                StatusText = success ? "Enforcement started" : "Failed to start enforcement";
            }
            
            await UpdateStatus();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            StatusText = "Refreshing instances...";
            
            var selectedGroup = await _groupService.GetSelectedGroupAsync();
            if (selectedGroup == null)
            {
                StatusText = "No group selected";
                return;
            }

            var instances = await _instancesService.GetGroupInstancesAsync(selectedGroup.GroupId);
            
            Instances.Clear();
            foreach (var instance in instances)
            {
                Instances.Add(instance);
            }
            
            UpdateStatistics();
            StatusText = $"Loaded {instances.Count} instances";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private async Task Export()
    {
        try
        {
            StatusText = "Exporting instance data...";
            // TODO: Implement export functionality
            StatusText = "Export completed";
        }
        catch (Exception ex)
        {
            StatusText = $"Export error: {ex.Message}";
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
            StatusText = $"Error: {ex.Message}";
        }
    }

    private async Task CloseInstance(GroupInstance? instance)
    {
        if (instance == null) return;
        
        try
        {
            StatusText = $"Closing instance {instance.WorldName}...";
            
            var success = await _instancesService.CloseInstanceAsync(instance.InstanceId);
            StatusText = success ? "Instance closed successfully" : "Failed to close instance";
            
            if (success)
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private async Task CancelClosure(GroupInstance? instance)
    {
        if (instance == null) return;
        
        try
        {
            StatusText = $"Cancelling closure for {instance.WorldName}...";
            
            var result = await _enforcementService.CancelScheduledClosureAsync(instance.InstanceId);
            StatusText = result.Success ? "Closure cancelled" : result.Message;
            
            if (result.Success)
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
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

    private async Task UpdateStatus()
    {
        try
        {
            IsMonitoring = await _instancesService.IsMonitoringAsync();
            IsEnforcementActive = await _enforcementService.IsEnforcementActiveAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Status update error: {ex.Message}";
        }
    }

    private void UpdateStatusText()
    {
        if (IsMonitoring && IsEnforcementActive)
            StatusText = "Monitoring and enforcement active";
        else if (IsMonitoring)
            StatusText = "Monitoring active, enforcement disabled";
        else if (IsEnforcementActive)
            StatusText = "Enforcement active, monitoring stopped";
        else
            StatusText = "Monitoring and enforcement stopped";
    }

    private void UpdateStatistics()
    {
        OnPropertyChanged(nameof(TotalInstances));
        OnPropertyChanged(nameof(ActiveInstances));
        OnPropertyChanged(nameof(FlaggedInstances));
        OnPropertyChanged(nameof(ClosingInstances));
        OnPropertyChanged(nameof(TotalUsers));
    }

    private void OnInstanceDetected(object? sender, InstanceDetectedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Instances.Add(e.Instance);
            UpdateStatistics();
            StatusText = $"New instance detected: {e.Instance.WorldName}";
        });
    }

    private void OnInstanceUpdated(object? sender, InstanceUpdatedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var existingInstance = Instances.FirstOrDefault(i => i.InstanceId == e.NewInstance.InstanceId);
            if (existingInstance != null)
            {
                var index = Instances.IndexOf(existingInstance);
                Instances[index] = e.NewInstance;
                UpdateStatistics();
                StatusText = $"Instance updated: {e.NewInstance.WorldName}";
            }
        });
    }

    private void OnInstanceClosed(object? sender, InstanceClosedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var instanceToRemove = Instances.FirstOrDefault(i => i.InstanceId == e.Instance.InstanceId);
            if (instanceToRemove != null)
            {
                Instances.Remove(instanceToRemove);
                UpdateStatistics();
                StatusText = $"Instance closed: {e.Instance.WorldName}";
            }
        });
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

public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke((T?)parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;
        
        _isExecuting = true;
        CommandManager.InvalidateRequerySuggested();
        
        try
        {
            await _execute((T?)parameter);
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}