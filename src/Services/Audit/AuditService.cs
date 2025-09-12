using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.VrcApi;

namespace VrcGroupGuardian.Services.Audit;

public interface IAuditService
{
    Task<bool> LogActionAsync(AuditActionType actionType, string targetId, string targetDisplayName, 
        AuditTargetType targetType, string? actorUserId = null, string? actorDisplayName = null, 
        string? details = null, bool success = true, string? errorMessage = null, string? apiResponse = null);
    
    Task<List<AuditRecord>> GetAuditRecordsAsync(DateTime? startDate = null, DateTime? endDate = null, 
        AuditActionType? actionType = null, AuditTargetType? targetType = null, int? limit = null);
    
    Task<bool> ExportToCsvAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null);
    Task<bool> ExportToJsonAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null);
    Task<AuditStats> GetAuditStatsAsync(TimeSpan? period = null);
    Task<bool> PurgeOldRecordsAsync(TimeSpan retentionPeriod);
    Task<List<AuditRecord>> SyncWithVrchatAuditLogsAsync(string groupId);
    Task<bool> StartRealTimeLoggingAsync();
    Task<bool> StopRealTimeLoggingAsync();
}

public class AuditService : IAuditService, IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly IVrcApiService _vrcApiService;
    private readonly ILogger<AuditService> _logger;
    
    private readonly ConcurrentQueue<AuditRecord> _auditQueue = new();
    private readonly SemaphoreSlim _auditLock = new(1, 1);
    private readonly Timer? _flushTimer;
    private readonly string _auditDirectory;
    private bool _isRealTimeLoggingActive;

    public AuditService(ISettingsStore settingsStore, IVrcApiService vrcApiService, ILogger<AuditService> logger)
    {
        _settingsStore = settingsStore;
        _vrcApiService = vrcApiService;
        _logger = logger;
        
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _auditDirectory = Path.Combine(appDataPath, "VrcGroupGuardian", "Audit");
        Directory.CreateDirectory(_auditDirectory);
        
        // Start periodic flush timer (every 30 seconds)
        _flushTimer = new Timer(FlushAuditQueue, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public async Task<bool> LogActionAsync(AuditActionType actionType, string targetId, string targetDisplayName,
        AuditTargetType targetType, string? actorUserId = null, string? actorDisplayName = null,
        string? details = null, bool success = true, string? errorMessage = null, string? apiResponse = null)
    {
        try
        {
            var auditRecord = new AuditRecord
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                ActionType = actionType,
                ActorUserId = actorUserId,
                ActorDisplayName = actorDisplayName,
                TargetType = targetType,
                TargetId = targetId,
                TargetDisplayName = targetDisplayName,
                Details = details ?? "",
                ApiResponse = apiResponse,
                Success = success,
                ErrorMessage = errorMessage
            };

            if (!auditRecord.IsValid())
            {
                _logger.LogWarning("Invalid audit record: {ActionType} for {TargetId}", actionType, targetId);
                return false;
            }

            _auditQueue.Enqueue(auditRecord);
            
            _logger.LogDebug("Queued audit record: {ActionType} for {TargetType} {TargetId} - Success: {Success}", 
                actionType, targetType, targetId, success);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log audit action {ActionType} for {TargetId}", actionType, targetId);
            return false;
        }
    }

    public async Task<List<AuditRecord>> GetAuditRecordsAsync(DateTime? startDate = null, DateTime? endDate = null,
        AuditActionType? actionType = null, AuditTargetType? targetType = null, int? limit = null)
    {
        await _auditLock.WaitAsync();
        try
        {
            // Ensure any queued records are flushed
            await FlushAuditQueueAsync();
            
            var allRecords = await LoadAllAuditRecordsAsync();
            
            // Apply filters
            var filteredRecords = allRecords.AsEnumerable();
            
            if (startDate.HasValue)
                filteredRecords = filteredRecords.Where(r => r.Timestamp >= startDate.Value);
            
            if (endDate.HasValue)
                filteredRecords = filteredRecords.Where(r => r.Timestamp <= endDate.Value);
            
            if (actionType.HasValue)
                filteredRecords = filteredRecords.Where(r => r.ActionType == actionType.Value);
            
            if (targetType.HasValue)
                filteredRecords = filteredRecords.Where(r => r.TargetType == targetType.Value);
            
            // Sort by timestamp descending
            filteredRecords = filteredRecords.OrderByDescending(r => r.Timestamp);
            
            if (limit.HasValue)
                filteredRecords = filteredRecords.Take(limit.Value);
            
            return filteredRecords.ToList();
        }
        finally
        {
            _auditLock.Release();
        }
    }

    public async Task<bool> ExportToCsvAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            var records = await GetAuditRecordsAsync(startDate, endDate);
            
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            
            // Write CSV header
            await writer.WriteLineAsync("Timestamp,ActionType,ActorUserId,ActorDisplayName,TargetType,TargetId,TargetDisplayName,Success,ErrorMessage,Details");
            
            foreach (var record in records)
            {
                var line = string.Join(",", 
                    EscapeCsvField(record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss UTC")),
                    EscapeCsvField(record.ActionType.ToString()),
                    EscapeCsvField(record.ActorUserId ?? ""),
                    EscapeCsvField(record.ActorDisplayName ?? ""),
                    EscapeCsvField(record.TargetType.ToString()),
                    EscapeCsvField(record.TargetId),
                    EscapeCsvField(record.TargetDisplayName),
                    EscapeCsvField(record.Success.ToString()),
                    EscapeCsvField(record.ErrorMessage ?? ""),
                    EscapeCsvField(record.Details));
                
                await writer.WriteLineAsync(line);
            }
            
            _logger.LogInformation("Exported {RecordCount} audit records to CSV: {FilePath}", records.Count, filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export audit records to CSV: {FilePath}", filePath);
            return false;
        }
    }

    public async Task<bool> ExportToJsonAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            var records = await GetAuditRecordsAsync(startDate, endDate);
            
            var exportData = new
            {
                ExportedAt = DateTime.UtcNow,
                RecordCount = records.Count,
                StartDate = startDate,
                EndDate = endDate,
                Records = records
            };
            
            var jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var json = JsonSerializer.Serialize(exportData, jsonOptions);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
            
            _logger.LogInformation("Exported {RecordCount} audit records to JSON: {FilePath}", records.Count, filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export audit records to JSON: {FilePath}", filePath);
            return false;
        }
    }

    public async Task<AuditStats> GetAuditStatsAsync(TimeSpan? period = null)
    {
        var endDate = DateTime.UtcNow;
        var startDate = period.HasValue ? endDate.Subtract(period.Value) : DateTime.MinValue;
        
        var records = await GetAuditRecordsAsync(startDate, endDate);
        
        var stats = new AuditStats
        {
            TotalRecords = records.Count,
            SuccessfulActions = records.Count(r => r.Success),
            FailedActions = records.Count(r => !r.Success),
            ActionTypeCounts = records.GroupBy(r => r.ActionType)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            TargetTypeCounts = records.GroupBy(r => r.TargetType)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            Period = period,
            GeneratedAt = DateTime.UtcNow
        };
        
        return stats;
    }

    public async Task<bool> PurgeOldRecordsAsync(TimeSpan retentionPeriod)
    {
        await _auditLock.WaitAsync();
        try
        {
            var cutoffDate = DateTime.UtcNow.Subtract(retentionPeriod);
            var allRecords = await LoadAllAuditRecordsAsync();
            
            var recordsToKeep = allRecords.Where(r => r.Timestamp >= cutoffDate).ToList();
            var purgedCount = allRecords.Count - recordsToKeep.Count;
            
            if (purgedCount > 0)
            {
                await SaveAuditRecordsAsync(recordsToKeep);
                _logger.LogInformation("Purged {PurgedCount} audit records older than {CutoffDate}", 
                    purgedCount, cutoffDate);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to purge old audit records");
            return false;
        }
        finally
        {
            _auditLock.Release();
        }
    }

    public async Task<List<AuditRecord>> SyncWithVrchatAuditLogsAsync(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return new List<AuditRecord>();

        try
        {
            _logger.LogInformation("Syncing with VRChat audit logs for group {GroupId}", groupId);
            
            var vrchatAuditLogs = await _vrcApiService.GetGroupAuditLogsAsync(groupId, 100);
            var localRecords = await GetAuditRecordsAsync(limit: 1000);
            
            // Find new records from VRChat that we don't have locally
            var newRecords = new List<AuditRecord>();
            var localRecordIds = localRecords.Select(r => r.Id).ToHashSet();
            
            foreach (var vrchatLog in vrchatAuditLogs)
            {
                if (!localRecordIds.Contains(vrchatLog.Id))
                {
                    newRecords.Add(vrchatLog);
                    await LogActionAsync(vrchatLog.ActionType, vrchatLog.TargetId, vrchatLog.TargetDisplayName,
                        vrchatLog.TargetType, vrchatLog.ActorUserId, vrchatLog.ActorDisplayName,
                        vrchatLog.Details, vrchatLog.Success, vrchatLog.ErrorMessage, vrchatLog.ApiResponse);
                }
            }
            
            _logger.LogInformation("Synced {NewRecordCount} new audit records from VRChat for group {GroupId}", 
                newRecords.Count, groupId);
            
            return newRecords;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync with VRChat audit logs for group {GroupId}", groupId);
            return new List<AuditRecord>();
        }
    }

    public async Task<bool> StartRealTimeLoggingAsync()
    {
        _isRealTimeLoggingActive = true;
        _logger.LogInformation("Started real-time audit logging");
        return true;
    }

    public async Task<bool> StopRealTimeLoggingAsync()
    {
        _isRealTimeLoggingActive = false;
        await FlushAuditQueueAsync();
        _logger.LogInformation("Stopped real-time audit logging");
        return true;
    }

    private async void FlushAuditQueue(object? state)
    {
        await FlushAuditQueueAsync();
    }

    private async Task FlushAuditQueueAsync()
    {
        if (_auditQueue.IsEmpty)
            return;

        try
        {
            var recordsToFlush = new List<AuditRecord>();
            while (_auditQueue.TryDequeue(out var record))
            {
                recordsToFlush.Add(record);
            }

            if (recordsToFlush.Count > 0)
            {
                await AppendAuditRecordsAsync(recordsToFlush);
                _logger.LogDebug("Flushed {RecordCount} audit records to storage", recordsToFlush.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush audit queue");
        }
    }

    private async Task<List<AuditRecord>> LoadAllAuditRecordsAsync()
    {
        var allRecords = new List<AuditRecord>();
        
        if (!Directory.Exists(_auditDirectory))
            return allRecords;

        var auditFiles = Directory.GetFiles(_auditDirectory, "audit-*.json")
            .OrderBy(f => f);

        foreach (var file in auditFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var records = JsonSerializer.Deserialize<List<AuditRecord>>(json);
                if (records != null)
                {
                    allRecords.AddRange(records);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load audit file: {FilePath}", file);
            }
        }

        return allRecords;
    }

    private async Task SaveAuditRecordsAsync(List<AuditRecord> records)
    {
        // Group records by month for efficient storage
        var recordsByMonth = records
            .GroupBy(r => r.Timestamp.ToString("yyyy-MM"))
            .ToList();

        foreach (var monthGroup in recordsByMonth)
        {
            var fileName = $"audit-{monthGroup.Key}.json";
            var filePath = Path.Combine(_auditDirectory, fileName);
            
            var jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var json = JsonSerializer.Serialize(monthGroup.OrderBy(r => r.Timestamp).ToList(), jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }
    }

    private async Task AppendAuditRecordsAsync(List<AuditRecord> newRecords)
    {
        var existingRecords = await LoadAllAuditRecordsAsync();
        existingRecords.AddRange(newRecords);
        await SaveAuditRecordsAsync(existingRecords);
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "\"\"";

        if (field.Contains('"') || field.Contains(',') || field.Contains('\n') || field.Contains('\r'))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        return "\"" + field + "\"";
    }

    public void Dispose()
    {
        FlushAuditQueueAsync().Wait(TimeSpan.FromSeconds(5));
        _flushTimer?.Dispose();
        _auditLock?.Dispose();
    }
}

public class AuditStats
{
    public int TotalRecords { get; set; }
    public int SuccessfulActions { get; set; }
    public int FailedActions { get; set; }
    public Dictionary<string, int> ActionTypeCounts { get; set; } = new();
    public Dictionary<string, int> TargetTypeCounts { get; set; } = new();
    public TimeSpan? Period { get; set; }
    public DateTime GeneratedAt { get; set; }
}