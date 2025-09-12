using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Instances;

namespace VrcGroupGuardian.Services.Enforcement;

public interface IEnforcementService
{
    Task<bool> StartEnforcementAsync();
    Task<bool> StopEnforcementAsync();
    Task<bool> IsEnforcementActiveAsync();
    Task<PolicyConfiguration> GetPolicyConfigurationAsync();
    Task<bool> UpdatePolicyConfigurationAsync(PolicyConfiguration config);
    Task<CancellationResult> CancelScheduledClosureAsync(string instanceId);
    Task<List<EnforcementStatus>> GetEnforcementStatusAsync();
    event EventHandler<InstanceFlaggedEventArgs>? InstanceFlagged;
    event EventHandler<ClosureScheduledEventArgs>? ClosureScheduled;
    event EventHandler<ClosureCancelledEventArgs>? ClosureCancelled;
    event EventHandler<AutoCloseExecutedEventArgs>? AutoCloseExecuted;
}

public class EnforcementService : IEnforcementService, IDisposable
{
    private readonly IInstancesService _instancesService;
    private readonly ISettingsStore _settingsStore;
    private readonly INotificationService _notificationService;
    private readonly ILogger<EnforcementService> _logger;
    
    private readonly ConcurrentDictionary<string, EnforcementTimer> _enforcementTimers = new();
    private readonly SemaphoreSlim _enforcementLock = new(1, 1);
    private bool _isActive;
    private PolicyConfiguration? _currentPolicy;

    public event EventHandler<InstanceFlaggedEventArgs>? InstanceFlagged;
    public event EventHandler<ClosureScheduledEventArgs>? ClosureScheduled;
    public event EventHandler<ClosureCancelledEventArgs>? ClosureCancelled;
    public event EventHandler<AutoCloseExecutedEventArgs>? AutoCloseExecuted;

    public EnforcementService(
        IInstancesService instancesService, 
        ISettingsStore settingsStore, 
        INotificationService notificationService,
        ILogger<EnforcementService> logger)
    {
        _instancesService = instancesService;
        _settingsStore = settingsStore;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<bool> StartEnforcementAsync()
    {
        await _enforcementLock.WaitAsync();
        try
        {
            if (_isActive)
            {
                _logger.LogInformation("Enforcement already active");
                return true;
            }

            // Load policy configuration
            _currentPolicy = await _settingsStore.LoadPolicyConfigurationAsync();
            if (!_currentPolicy.EnforcementEnabled)
            {
                _logger.LogInformation("Enforcement disabled in policy configuration");
                return false;
            }

            // Subscribe to instance events
            _instancesService.InstanceDetected += OnInstanceDetected;
            _instancesService.InstanceUpdated += OnInstanceUpdated;
            _instancesService.InstanceClosed += OnInstanceClosed;

            _isActive = true;
            _logger.LogInformation("Started enforcement service with {GracePeriod}s grace period", 
                _currentPolicy.GracePeriodSeconds);
            
            // Send notification about enforcement start
            if (_currentPolicy.NotificationsEnabled)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _notificationService.ShowNotificationAsync(
                            "Policy Enforcement Started",
                            $"VRC Group Guardian is now monitoring and enforcing group instance policies with {_currentPolicy.GracePeriodSeconds}s grace period.",
                            NotificationSeverity.Information);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send notification for enforcement start");
                    }
                });
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start enforcement service");
            return false;
        }
        finally
        {
            _enforcementLock.Release();
        }
    }

    public async Task<bool> StopEnforcementAsync()
    {
        await _enforcementLock.WaitAsync();
        try
        {
            if (!_isActive)
            {
                _logger.LogDebug("Enforcement not active, nothing to stop");
                return true;
            }

            // Unsubscribe from instance events
            _instancesService.InstanceDetected -= OnInstanceDetected;
            _instancesService.InstanceUpdated -= OnInstanceUpdated;
            _instancesService.InstanceClosed -= OnInstanceClosed;

            // Cancel all active timers
            foreach (var timer in _enforcementTimers.Values)
            {
                timer.Dispose();
            }
            _enforcementTimers.Clear();

            _isActive = false;
            
            // Send notification about enforcement stop
            if (_currentPolicy?.NotificationsEnabled == true)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _notificationService.ShowNotificationAsync(
                            "Policy Enforcement Stopped",
                            "VRC Group Guardian has stopped monitoring and enforcing group instance policies.",
                            NotificationSeverity.Information);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send notification for enforcement stop");
                    }
                });
            }
            
            _currentPolicy = null;
            _logger.LogInformation("Stopped enforcement service");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping enforcement service");
            return false;
        }
        finally
        {
            _enforcementLock.Release();
        }
    }

    public async Task<bool> IsEnforcementActiveAsync()
    {
        return _isActive;
    }

    public async Task<PolicyConfiguration> GetPolicyConfigurationAsync()
    {
        return _currentPolicy ?? await _settingsStore.LoadPolicyConfigurationAsync();
    }

    public async Task<bool> UpdatePolicyConfigurationAsync(PolicyConfiguration config)
    {
        if (config == null || !config.IsValid())
        {
            _logger.LogWarning("Attempted to update with invalid policy configuration");
            return false;
        }

        await _enforcementLock.WaitAsync();
        try
        {
            var saved = await _settingsStore.SavePolicyConfigurationAsync(config);
            if (!saved)
            {
                _logger.LogError("Failed to save policy configuration");
                return false;
            }

            _currentPolicy = config;
            
            // If enforcement was disabled, stop it
            if (!config.EnforcementEnabled && _isActive)
            {
                await StopEnforcementAsync();
                _logger.LogInformation("Enforcement disabled by policy update");
            }
            // If enforcement was enabled and we're not active, start it
            else if (config.EnforcementEnabled && !_isActive)
            {
                await StartEnforcementAsync();
                _logger.LogInformation("Enforcement enabled by policy update");
            }

            _logger.LogInformation("Updated policy configuration: Enforcement={Enabled}, GracePeriod={GracePeriod}s", 
                config.EnforcementEnabled, config.GracePeriodSeconds);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update policy configuration");
            return false;
        }
        finally
        {
            _enforcementLock.Release();
        }
    }

    public async Task<CancellationResult> CancelScheduledClosureAsync(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            return new CancellationResult { Success = false, Message = "Invalid instance ID" };
        }

        if (_enforcementTimers.TryRemove(instanceId, out var timer))
        {
            timer.Dispose();
            
            var instance = await _instancesService.GetInstanceAsync(instanceId);
            if (instance != null)
            {
                instance.Status = InstanceStatus.Active;
                instance.CountdownTimer = null;
                instance.LastUpdated = DateTime.UtcNow;
            }

            _logger.LogInformation("Cancelled scheduled closure for instance {InstanceId}", instanceId);
            
            ClosureCancelled?.Invoke(this, new ClosureCancelledEventArgs 
            { 
                Instance = instance,
                CancelledAt = DateTime.UtcNow,
                CancelledBy = "Manual"
            });

            return new CancellationResult { Success = true, Message = "Closure cancelled successfully" };
        }

        return new CancellationResult { Success = false, Message = "No scheduled closure found for this instance" };
    }

    public async Task<List<EnforcementStatus>> GetEnforcementStatusAsync()
    {
        var statuses = new List<EnforcementStatus>();

        foreach (var kvp in _enforcementTimers)
        {
            var instanceId = kvp.Key;
            var timer = kvp.Value;
            var instance = await _instancesService.GetInstanceAsync(instanceId);

            if (instance != null)
            {
                statuses.Add(new EnforcementStatus
                {
                    InstanceId = instanceId,
                    InstanceName = instance.WorldName,
                    Status = instance.Status,
                    TimeRemaining = timer.GetTimeRemaining(),
                    ScheduledAt = timer.ScheduledAt,
                    Reason = "Age-gated world policy violation"
                });
            }
        }

        return statuses;
    }

    private async void OnInstanceDetected(object? sender, InstanceDetectedEventArgs e)
    {
        if (!_isActive || _currentPolicy == null)
            return;

        await EvaluateInstanceForEnforcement(e.Instance);
    }

    private async void OnInstanceUpdated(object? sender, InstanceUpdatedEventArgs e)
    {
        if (!_isActive || _currentPolicy == null)
            return;

        // If instance became compliant, cancel any scheduled closure
        if (e.OldInstance.AgeGated == false && e.NewInstance.AgeGated == true)
        {
            await CancelScheduledClosureAsync(e.NewInstance.InstanceId);
        }
        // If instance became non-compliant, evaluate for enforcement
        else if (e.OldInstance.AgeGated == true && e.NewInstance.AgeGated == false)
        {
            await EvaluateInstanceForEnforcement(e.NewInstance);
        }
    }

    private void OnInstanceClosed(object? sender, InstanceClosedEventArgs e)
    {
        // Remove any enforcement timer for closed instance
        if (_enforcementTimers.TryRemove(e.Instance.InstanceId, out var timer))
        {
            timer.Dispose();
            _logger.LogDebug("Removed enforcement timer for closed instance {InstanceId}", e.Instance.InstanceId);
        }
    }

    private async Task EvaluateInstanceForEnforcement(GroupInstance instance)
    {
        if (_currentPolicy == null || !_currentPolicy.EnforcementEnabled)
            return;

        // Check if instance violates age-gating policy
        if (!instance.AgeGated && instance.Status == InstanceStatus.Active)
        {
            _logger.LogInformation("Instance {InstanceId} flagged for policy violation: Not age-gated", instance.InstanceId);
            
            // Update instance status
            instance.Status = InstanceStatus.Flagged;
            instance.LastUpdated = DateTime.UtcNow;
            
            InstanceFlagged?.Invoke(this, new InstanceFlaggedEventArgs 
            { 
                Instance = instance, 
                Reason = "Age-gated world policy violation",
                FlaggedAt = DateTime.UtcNow 
            });

            // Send desktop notification if enabled
            if (_currentPolicy?.NotificationsEnabled == true)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _notificationService.ShowNotificationAsync(
                            "Instance Flagged",
                            $"Instance '{instance.WorldName}' has been flagged for age-gated content and will be closed soon.",
                            NotificationSeverity.Warning);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send notification for flagged instance {InstanceId}", instance.InstanceId);
                    }
                });
            }

            // Schedule closure after grace period
            await ScheduleClosureAsync(instance);
        }
    }

    private async Task ScheduleClosureAsync(GroupInstance instance)
    {
        if (_currentPolicy == null)
            return;

        var gracePeriod = TimeSpan.FromSeconds(_currentPolicy.GracePeriodSeconds);
        var timer = new EnforcementTimer(instance.InstanceId, gracePeriod, ExecuteAutoCloseAsync);
        
        _enforcementTimers.AddOrUpdate(instance.InstanceId, timer, (key, old) => 
        {
            old.Dispose();
            return timer;
        });

        // Update instance status
        instance.Status = InstanceStatus.ClosingCountdown;
        instance.CountdownTimer = gracePeriod;
        instance.LastUpdated = DateTime.UtcNow;

        _logger.LogInformation("Scheduled closure for instance {InstanceId} in {GracePeriod}s", 
            instance.InstanceId, gracePeriod.TotalSeconds);
        
        ClosureScheduled?.Invoke(this, new ClosureScheduledEventArgs 
        { 
            Instance = instance, 
            ScheduledFor = DateTime.UtcNow.Add(gracePeriod),
            Reason = "Age-gated world policy violation"
        });

        // Send desktop notification if enabled
        if (_currentPolicy?.NotificationsEnabled == true)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var scheduledTime = DateTime.UtcNow.Add(gracePeriod);
                    await _notificationService.ShowNotificationAsync(
                        "Instance Closure Scheduled",
                        $"Instance '{instance.WorldName}' will be automatically closed at {scheduledTime:HH:mm:ss} ({gracePeriod.TotalMinutes:F1} minutes from now).",
                        NotificationSeverity.Information);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send notification for scheduled closure {InstanceId}", instance.InstanceId);
                }
            });
        }
    }

    private async Task ExecuteAutoCloseAsync(string instanceId)
    {
        try
        {
            _logger.LogInformation("Executing auto-close for instance {InstanceId}", instanceId);
            
            var success = await _instancesService.CloseInstanceAsync(instanceId);
            var instance = await _instancesService.GetInstanceAsync(instanceId);
            
            // Remove the timer
            _enforcementTimers.TryRemove(instanceId, out _);
            
            AutoCloseExecuted?.Invoke(this, new AutoCloseExecutedEventArgs 
            { 
                Instance = instance,
                Success = success,
                ExecutedAt = DateTime.UtcNow,
                Reason = "Age-gated world policy violation"
            });

            // Send desktop notification if enabled
            if (_currentPolicy?.NotificationsEnabled == true)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var severity = success ? NotificationSeverity.Success : NotificationSeverity.Error;
                        var title = success ? "Instance Closed Successfully" : "Instance Closure Failed";
                        var message = success 
                            ? $"Instance '{instance?.WorldName ?? instanceId}' has been automatically closed due to policy violation."
                            : $"Failed to automatically close instance '{instance?.WorldName ?? instanceId}'. Manual intervention may be required.";
                        
                        await _notificationService.ShowNotificationAsync(title, message, severity);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send notification for auto-close execution {InstanceId}", instanceId);
                    }
                });
            }

            if (success)
            {
                _logger.LogInformation("Successfully auto-closed instance {InstanceId}", instanceId);
            }
            else
            {
                _logger.LogWarning("Failed to auto-close instance {InstanceId}", instanceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing auto-close for instance {InstanceId}", instanceId);
        }
    }

    public void Dispose()
    {
        foreach (var timer in _enforcementTimers.Values)
        {
            timer.Dispose();
        }
        _enforcementTimers.Clear();
        _enforcementLock?.Dispose();
    }
}

public class EnforcementTimer : IDisposable
{
    private readonly Timer _timer;
    private readonly DateTime _scheduledAt;
    private readonly TimeSpan _duration;

    public DateTime ScheduledAt => _scheduledAt;

    public EnforcementTimer(string instanceId, TimeSpan duration, Func<string, Task> callback)
    {
        _scheduledAt = DateTime.UtcNow;
        _duration = duration;
        _timer = new Timer(async _ => await callback(instanceId), null, duration, Timeout.InfiniteTimeSpan);
    }

    public TimeSpan GetTimeRemaining()
    {
        var elapsed = DateTime.UtcNow - _scheduledAt;
        var remaining = _duration - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}

public class CancellationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class EnforcementStatus
{
    public string InstanceId { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public InstanceStatus Status { get; set; }
    public TimeSpan TimeRemaining { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class InstanceFlaggedEventArgs : EventArgs
{
    public required GroupInstance Instance { get; set; }
    public required string Reason { get; set; }
    public DateTime FlaggedAt { get; set; }
}

public class ClosureScheduledEventArgs : EventArgs
{
    public required GroupInstance Instance { get; set; }
    public DateTime ScheduledFor { get; set; }
    public required string Reason { get; set; }
}

public class ClosureCancelledEventArgs : EventArgs
{
    public GroupInstance? Instance { get; set; }
    public DateTime CancelledAt { get; set; }
    public required string CancelledBy { get; set; }
}

public class AutoCloseExecutedEventArgs : EventArgs
{
    public GroupInstance? Instance { get; set; }
    public bool Success { get; set; }
    public DateTime ExecutedAt { get; set; }
    public required string Reason { get; set; }
}