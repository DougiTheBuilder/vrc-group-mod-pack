using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace VrcGroupGuardian.Infrastructure;

public interface ISecureStorage
{
    Task<bool> StoreCredentialAsync(string key, string value);
    Task<string?> RetrieveCredentialAsync(string key);
    Task<bool> DeleteCredentialAsync(string key);
    void WipeMemory(string sensitiveData);
}

public class SecureStorage : ISecureStorage
{
    private const string CredentialTarget = "VrcGroupGuardian";

    public async Task<bool> StoreCredentialAsync(string key, string value)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
            return false;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await StoreCredentialWindowsAsync(key, value);
            }
            else
            {
                // Fallback to DPAPI for non-Windows (will throw on Linux/macOS)
                var encryptedData = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(value), 
                    Encoding.UTF8.GetBytes(key), 
                    DataProtectionScope.CurrentUser);
                
                var credentialFile = GetCredentialFilePath(key);
                await File.WriteAllBytesAsync(credentialFile, encryptedData);
                return true;
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            WipeMemory(value);
        }
    }

    public async Task<string?> RetrieveCredentialAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await RetrieveCredentialWindowsAsync(key);
            }
            else
            {
                var credentialFile = GetCredentialFilePath(key);
                if (!File.Exists(credentialFile))
                    return null;

                var encryptedData = await File.ReadAllBytesAsync(credentialFile);
                var decryptedData = ProtectedData.Unprotect(
                    encryptedData, 
                    Encoding.UTF8.GetBytes(key), 
                    DataProtectionScope.CurrentUser);
                
                return Encoding.UTF8.GetString(decryptedData);
            }
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DeleteCredentialAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await DeleteCredentialWindowsAsync(key);
            }
            else
            {
                var credentialFile = GetCredentialFilePath(key);
                if (File.Exists(credentialFile))
                {
                    File.Delete(credentialFile);
                }
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    public void WipeMemory(string sensitiveData)
    {
        // Best effort memory wiping - not cryptographically secure
        // but reduces the window for credential exposure
        if (string.IsNullOrEmpty(sensitiveData))
            return;

        unsafe
        {
            fixed (char* ptr = sensitiveData)
            {
                for (int i = 0; i < sensitiveData.Length; i++)
                {
                    ptr[i] = '\0';
                }
            }
        }
    }

    private async Task<bool> StoreCredentialWindowsAsync(string key, string value)
    {
        // Placeholder for Windows Credential Manager integration
        // Would use CredWrite from advapi32.dll in real implementation
        await Task.Delay(1); // Simulate async operation
        
        // For now, fall back to DPAPI file storage
        var encryptedData = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), 
            Encoding.UTF8.GetBytes(key), 
            DataProtectionScope.CurrentUser);
        
        var credentialFile = GetCredentialFilePath(key);
        await File.WriteAllBytesAsync(credentialFile, encryptedData);
        return true;
    }

    private async Task<string?> RetrieveCredentialWindowsAsync(string key)
    {
        // Placeholder for Windows Credential Manager integration
        // Would use CredRead from advapi32.dll in real implementation
        await Task.Delay(1); // Simulate async operation
        
        // For now, fall back to DPAPI file storage
        var credentialFile = GetCredentialFilePath(key);
        if (!File.Exists(credentialFile))
            return null;

        var encryptedData = await File.ReadAllBytesAsync(credentialFile);
        var decryptedData = ProtectedData.Unprotect(
            encryptedData, 
            Encoding.UTF8.GetBytes(key), 
            DataProtectionScope.CurrentUser);
        
        return Encoding.UTF8.GetString(decryptedData);
    }

    private async Task<bool> DeleteCredentialWindowsAsync(string key)
    {
        // Placeholder for Windows Credential Manager integration
        // Would use CredDelete from advapi32.dll in real implementation
        await Task.Delay(1); // Simulate async operation
        
        // For now, fall back to file deletion
        var credentialFile = GetCredentialFilePath(key);
        if (File.Exists(credentialFile))
        {
            File.Delete(credentialFile);
        }
        return true;
    }

    private static string GetCredentialFilePath(string key)
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataPath, CredentialTarget);
        Directory.CreateDirectory(appFolder);
        
        // Hash the key to avoid filesystem issues
        using var sha256 = SHA256.Create();
        var hashedKey = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(appFolder, $"{hashedKey}.cred");
    }
}