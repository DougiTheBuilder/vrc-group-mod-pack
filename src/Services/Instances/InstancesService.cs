using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Services.VrcApi;

namespace VrcGroupGuardian.Services.Instances;

public interface IInstancesService
{
    Task<List<GroupInstance>> GetGroupInstancesAsync(string groupId);
    Task<GroupInstance?> GetInstanceAsync(string instanceId);
    Task<bool> StartMonitoringAsync(string groupId);
    Task<bool> StopMonitoringAsync();
    Task<bool> CloseInstanceAsync(string instanceId);
    Task<bool> IsMonitoringAsync();
    event EventHandler<InstanceDetectedEventArgs>? InstanceDetected;
    event EventHandler<InstanceUpdatedEventArgs>? InstanceUpdated;
    event EventHandler<InstanceClosedEventArgs>? InstanceClosed;
}

public class InstancesService : IInstancesService, IDisposable
{
    private readonly IVrcApiService _vrcApiService;
    private readonly IAuthService _authService;
    private readonly IGroupService _groupService;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<InstancesService> _logger;
    
    private readonly ConcurrentDictionary<string, GroupInstance> _trackedInstances = new();
    private readonly SemaphoreSlim _monitoringLock = new(1, 1);
    private Timer? _pollingTimer;
    private string? _monitoredGroupId;
    private bool _isMonitoring;

    public event EventHandler<InstanceDetectedEventArgs>? InstanceDetected;
    public event EventHandler<InstanceUpdatedEventArgs>? InstanceUpdated;
    public event EventHandler<InstanceClosedEventArgs>? InstanceClosed;

    public InstancesService(
        IVrcApiService vrcApiService,
        IAuthService authService,
        IGroupService groupService,
        ISettingsStore settingsStore,
        ILogger<InstancesService> logger)
    {
        _vrcApiService = vrcApiService;
        _authService = authService;
        _groupService = groupService;
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public async Task<List<GroupInstance>> GetGroupInstancesAsync(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return new List<GroupInstance>();

        if (!await _authService.IsAuthenticatedAsync())
        {
            _logger.LogWarning("Cannot get group instances: not authenticated");
            return new List<GroupInstance>();
        }

        try
        {
            var instances = await _vrcApiService.GetGroupInstancesAsync(groupId);
            
            // Update tracked instances
            foreach (var instance in instances)
            {
                _trackedInstances.AddOrUpdate(instance.InstanceId, instance, (key, old) => instance);
            }

            _logger.LogDebug("Retrieved {InstanceCount} instances for group {GroupId}", instances.Count, groupId);
            return instances;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get group instances for {GroupId}", groupId);
            return new List<GroupInstance>();
        }
    }

    public async Task<GroupInstance?> GetInstanceAsync(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
            return null;

        // Check tracked instances first
        if (_trackedInstances.TryGetValue(instanceId, out var cachedInstance))
        {
            return cachedInstance;
        }

        // If monitoring, the instance should be in tracked instances
        // If not monitoring, we can't fetch individual instances from VRChat API
        if (_isMonitoring && !string.IsNullOrEmpty(_monitoredGroupId))
        {
            var instances = await GetGroupInstancesAsync(_monitoredGroupId);
            return instances.FirstOrDefault(i => i.InstanceId == instanceId);
        }

        return null;
    }

    public async Task<bool> StartMonitoringAsync(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return false;

        await _monitoringLock.WaitAsync();
        try
        {
            if (_isMonitoring && _monitoredGroupId == groupId)
            {
                _logger.LogInformation("Already monitoring group {GroupId}", groupId);
                return true;
            }

            if (!await _authService.IsAuthenticatedAsync())
            {
                _logger.LogWarning("Cannot start monitoring: not authenticated");
                return false;
            }

            if (!await _groupService.CanManageInstancesAsync(groupId))
            {
                _logger.LogWarning("Cannot start monitoring group {GroupId}: insufficient permissions", groupId);
                return false;
            }

            // Stop existing monitoring
            if (_isMonitoring)
            {
                await StopMonitoringAsync();
            }

            _monitoredGroupId = groupId;
            
            // Load policy configuration for polling interval
            var policy = await _settingsStore.LoadPolicyConfigurationAsync();
            var pollingIntervalMs = policy.GetJitteredPollingInterval() * 1000;

            // Start polling timer
            _pollingTimer = new Timer(PollInstancesCallback, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(pollingIntervalMs));
            _isMonitoring = true;

            _logger.LogInformation("Started monitoring group {GroupId} with {PollingInterval}ms polling interval", 
                groupId, pollingIntervalMs);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start monitoring group {GroupId}", groupId);
            return false;
        }
        finally
        {
            _monitoringLock.Release();
        }
    }

    public async Task<bool> StopMonitoringAsync()
    {
        await _monitoringLock.WaitAsync();
        try
        {
            if (!_isMonitoring)
            {
                _logger.LogDebug("Monitoring not active, nothing to stop");
                return true;
            }

            _pollingTimer?.Dispose();
            _pollingTimer = null;
            _isMonitoring = false;
            
            var groupId = _monitoredGroupId;
            _monitoredGroupId = null;
            
            // Clear tracked instances
            _trackedInstances.Clear();

            _logger.LogInformation("Stopped monitoring group {GroupId}", groupId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping monitoring");
            return false;
        }
        finally
        {
            _monitoringLock.Release();
        }
    }

    public async Task<bool> CloseInstanceAsync(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
            return false;

        if (!await _authService.IsAuthenticatedAsync())
        {
            _logger.LogWarning("Cannot close instance: not authenticated");
            return false;
        }

        try
        {
            var success = await _vrcApiService.CloseInstanceAsync(instanceId);
            
            if (success)
            {
                // Update tracked instance status
                if (_trackedInstances.TryGetValue(instanceId, out var instance))
                {
                    instance.Status = InstanceStatus.Closed;
                    instance.LastUpdated = DateTime.UtcNow;
                    
                    InstanceClosed?.Invoke(this, new InstanceClosedEventArgs 
                    { 
                        Instance = instance, 
                        ClosedBy = "Manual", 
                        Timestamp = DateTime.UtcNow 
                    });
                }

                _logger.LogInformation("Successfully closed instance {InstanceId}", instanceId);
            }
            else
            {
                _logger.LogWarning("Failed to close instance {InstanceId}", instanceId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing instance {InstanceId}", instanceId);
            return false;
        }
    }

    public async Task<bool> IsMonitoringAsync()
    {
        return _isMonitoring;
    }

    private async void PollInstancesCallback(object? state)
    {
        if (!_isMonitoring || string.IsNullOrEmpty(_monitoredGroupId))
            return;

        try
        {
            var instances = await GetGroupInstancesAsync(_monitoredGroupId);
            var currentInstanceIds = instances.Select(i => i.InstanceId).ToHashSet();
            var trackedInstanceIds = _trackedInstances.Keys.ToHashSet();

            // Detect new instances
            var newInstances = instances.Where(i => !trackedInstanceIds.Contains(i.InstanceId)).ToList();
            foreach (var newInstance in newInstances)
            {
                _logger.LogInformation("Detected new instance: {InstanceId} in world {WorldName}", 
                    newInstance.InstanceId, newInstance.WorldName);
                
                InstanceDetected?.Invoke(this, new InstanceDetectedEventArgs 
                { 
                    Instance = newInstance, 
                    Timestamp = DateTime.UtcNow 
                });
            }

            // Detect updated instances
            var updatedInstances = instances.Where(i => trackedInstanceIds.Contains(i.InstanceId)).ToList();
            foreach (var updatedInstance in updatedInstances)
            {
                if (_trackedInstances.TryGetValue(updatedInstance.InstanceId, out var oldInstance))
                {
                    // Check for significant changes
                    if (oldInstance.UserCount != updatedInstance.UserCount ||
                        oldInstance.Status != updatedInstance.Status)
                    {
                        _logger.LogDebug("Instance {InstanceId} updated: Users {OldCount}->{NewCount}, Status {OldStatus}->{NewStatus}",
                            updatedInstance.InstanceId, oldInstance.UserCount, updatedInstance.UserCount,
                            oldInstance.Status, updatedInstance.Status);
                        
                        InstanceUpdated?.Invoke(this, new InstanceUpdatedEventArgs 
                        { 
                            OldInstance = oldInstance, 
                            NewInstance = updatedInstance, 
                            Timestamp = DateTime.UtcNow 
                        });
                    }
                }
            }

            // Detect closed instances (no longer in API response)
            var closedInstanceIds = trackedInstanceIds.Except(currentInstanceIds);
            foreach (var closedInstanceId in closedInstanceIds)
            {
                if (_trackedInstances.TryRemove(closedInstanceId, out var closedInstance))
                {
                    closedInstance.Status = InstanceStatus.Closed;
                    closedInstance.LastUpdated = DateTime.UtcNow;
                    
                    _logger.LogInformation("Instance {InstanceId} is no longer active", closedInstanceId);
                    
                    InstanceClosed?.Invoke(this, new InstanceClosedEventArgs 
                    { 
                        Instance = closedInstance, 
                        ClosedBy = "Automatic", 
                        Timestamp = DateTime.UtcNow 
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during instance polling");
        }
    }

    public void Dispose()
    {
        _pollingTimer?.Dispose();
        _monitoringLock?.Dispose();
    }
}

public class InstanceDetectedEventArgs : EventArgs
{
    public required GroupInstance Instance { get; set; }
    public DateTime Timestamp { get; set; }
}

public class InstanceUpdatedEventArgs : EventArgs
{
    public required GroupInstance OldInstance { get; set; }
    public required GroupInstance NewInstance { get; set; }
    public DateTime Timestamp { get; set; }
}

public class InstanceClosedEventArgs : EventArgs
{
    public required GroupInstance Instance { get; set; }
    public required string ClosedBy { get; set; }
    public DateTime Timestamp { get; set; }
}