using Microsoft.Extensions.Logging;
using System.Text.Json;
using VrcGroupGuardian.Models;

namespace VrcGroupGuardian.Infrastructure;

public interface ISettingsStore
{
    Task<PolicyConfiguration> LoadPolicyConfigurationAsync();
    Task<bool> SavePolicyConfigurationAsync(PolicyConfiguration config);
    Task<T?> LoadSettingAsync<T>(string key) where T : class;
    Task<bool> SaveSettingAsync<T>(string key, T value) where T : class;
    Task<bool> DeleteSettingAsync(string key);
    Task<Dictionary<string, object>> LoadAllSettingsAsync();
    Task<bool> BackupSettingsAsync(string backupPath);
    Task<bool> RestoreSettingsAsync(string backupPath);
}

public class SettingsStore : ISettingsStore
{
    private readonly ILogger<SettingsStore> _logger;
    private readonly string _settingsDirectory;
    private readonly string _policyConfigFile;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsStore(ILogger<SettingsStore> logger)
    {
        _logger = logger;
        
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsDirectory = Path.Combine(appDataPath, "VrcGroupGuardian");
        _policyConfigFile = Path.Combine(_settingsDirectory, "policy-config.json");
        
        Directory.CreateDirectory(_settingsDirectory);
    }

    public async Task<PolicyConfiguration> LoadPolicyConfigurationAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            if (!File.Exists(_policyConfigFile))
            {
                _logger.LogInformation("Policy configuration file not found, using defaults");
                return new PolicyConfiguration();
            }

            var json = await File.ReadAllTextAsync(_policyConfigFile);
            var config = JsonSerializer.Deserialize<PolicyConfiguration>(json, JsonOptions);
            
            if (config == null || !config.IsValid())
            {
                _logger.LogWarning("Invalid policy configuration loaded, using defaults");
                return new PolicyConfiguration();
            }

            _logger.LogDebug("Loaded policy configuration: Group={GroupName}, Enforcement={Enabled}", 
                config.GroupName, config.EnforcementEnabled);
            
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load policy configuration, using defaults");
            return new PolicyConfiguration();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> SavePolicyConfigurationAsync(PolicyConfiguration config)
    {
        if (config == null || !config.IsValid())
        {
            _logger.LogWarning("Attempted to save invalid policy configuration");
            return false;
        }

        await _fileLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(_policyConfigFile, json);
            
            _logger.LogInformation("Saved policy configuration: Group={GroupName}, Enforcement={Enabled}", 
                config.GroupName, config.EnforcementEnabled);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save policy configuration");
            return false;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<T?> LoadSettingAsync<T>(string key) where T : class
    {
        if (string.IsNullOrEmpty(key))
            return null;

        var settingFile = GetSettingFilePath(key);
        
        await _fileLock.WaitAsync();
        try
        {
            if (!File.Exists(settingFile))
                return null;

            var json = await File.ReadAllTextAsync(settingFile);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load setting {Key}", key);
            return null;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> SaveSettingAsync<T>(string key, T value) where T : class
    {
        if (string.IsNullOrEmpty(key) || value == null)
            return false;

        var settingFile = GetSettingFilePath(key);
        
        await _fileLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await File.WriteAllTextAsync(settingFile, json);
            
            _logger.LogDebug("Saved setting {Key}", key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save setting {Key}", key);
            return false;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> DeleteSettingAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        var settingFile = GetSettingFilePath(key);
        
        await _fileLock.WaitAsync();
        try
        {
            if (File.Exists(settingFile))
            {
                File.Delete(settingFile);
                _logger.LogDebug("Deleted setting {Key}", key);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete setting {Key}", key);
            return false;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<Dictionary<string, object>> LoadAllSettingsAsync()
    {
        var settings = new Dictionary<string, object>();
        
        await _fileLock.WaitAsync();
        try
        {
            if (!Directory.Exists(_settingsDirectory))
                return settings;

            var settingFiles = Directory.GetFiles(_settingsDirectory, "*.json");
            
            foreach (var file in settingFiles)
            {
                try
                {
                    var key = Path.GetFileNameWithoutExtension(file);
                    var json = await File.ReadAllTextAsync(file);
                    var value = JsonSerializer.Deserialize<object>(json, JsonOptions);
                    
                    if (value != null)
                    {
                        settings[key] = value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load setting from file {File}", file);
                }
            }
            
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load all settings");
            return settings;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> BackupSettingsAsync(string backupPath)
    {
        if (string.IsNullOrEmpty(backupPath))
            return false;

        await _fileLock.WaitAsync();
        try
        {
            var backupDir = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            var allSettings = await LoadAllSettingsAsync();
            var backupData = new
            {
                BackupTime = DateTime.UtcNow,
                Version = "1.0",
                Settings = allSettings
            };

            var json = JsonSerializer.Serialize(backupData, JsonOptions);
            await File.WriteAllTextAsync(backupPath, json);
            
            _logger.LogInformation("Settings backed up to {BackupPath}", backupPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup settings to {BackupPath}", backupPath);
            return false;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> RestoreSettingsAsync(string backupPath)
    {
        if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath))
            return false;

        await _fileLock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(backupPath);
            using var document = JsonDocument.Parse(json);
            
            if (!document.RootElement.TryGetProperty("Settings", out var settingsElement))
            {
                _logger.LogWarning("Invalid backup file format");
                return false;
            }

            foreach (var setting in settingsElement.EnumerateObject())
            {
                var settingFile = GetSettingFilePath(setting.Name);
                var settingJson = JsonSerializer.Serialize(setting.Value, JsonOptions);
                await File.WriteAllTextAsync(settingFile, settingJson);
            }
            
            _logger.LogInformation("Settings restored from {BackupPath}", backupPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore settings from {BackupPath}", backupPath);
            return false;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private string GetSettingFilePath(string key)
    {
        // Sanitize the key for filesystem use
        var sanitizedKey = string.Concat(key.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        return Path.Combine(_settingsDirectory, $"{sanitizedKey}.json");
    }
}