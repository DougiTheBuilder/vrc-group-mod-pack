using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.VrcApi;

namespace VrcGroupGuardian.Services.Auth.Cli;

public class Program
{
    private static IServiceProvider? _serviceProvider;

    public static async Task<int> Main(string[] args)
    {
        ConfigureServices();
        
        if (args.Length == 0)
        {
            ShowHelp();
            return 1;
        }

        var command = args[0].ToLower();
        var authService = _serviceProvider!.GetRequiredService<IAuthService>();
        
        try
        {
            return command switch
            {
                "login" => await HandleLogin(authService, args),
                "logout" => await HandleLogout(authService),
                "status" => await HandleStatus(authService),
                "2fa" or "verify" => await Handle2FA(authService, args),
                "help" or "--help" or "-h" => ShowHelp(),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void ConfigureServices()
    {
        var services = new ServiceCollection();
        
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddSingleton<ISecureStorage, SecureStorage>();
        services.AddSingleton<IVrchatHttpClientFactory, VrchatHttpClientFactory>();
        services.AddSingleton<IVrcApiService, VrcApiService>();
        services.AddSingleton<IAuthService, AuthService>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    private static async Task<int> HandleLogin(IAuthService authService, string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: dotnet run login <username> <password>");
            return 1;
        }

        var username = args[1];
        var password = args[2];

        Console.WriteLine($"Logging in as {username}...");
        
        var result = await authService.LoginAsync(username, password);
        
        if (result.Success)
        {
            Console.WriteLine("✓ Login successful!");
            return 0;
        }
        else if (result.RequiresTwoFactor)
        {
            Console.WriteLine("Two-factor authentication required.");
            Console.Write("Enter 2FA code: ");
            var code = Console.ReadLine();
            
            if (!string.IsNullOrEmpty(code))
            {
                var twoFactorResult = await authService.VerifyTwoFactorAsync(code);
                if (twoFactorResult.Success)
                {
                    Console.WriteLine("✓ Login successful with 2FA!");
                    return 0;
                }
                else
                {
                    Console.WriteLine($"✗ 2FA verification failed: {twoFactorResult.ErrorMessage}");
                    return 1;
                }
            }
            else
            {
                Console.WriteLine("✗ 2FA code required");
                return 1;
            }
        }
        else
        {
            Console.WriteLine($"✗ Login failed: {result.ErrorMessage}");
            return 1;
        }
    }

    private static async Task<int> HandleLogout(IAuthService authService)
    {
        Console.WriteLine("Logging out...");
        
        var success = await authService.LogoutAsync();
        
        if (success)
        {
            Console.WriteLine("✓ Logged out successfully");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Logout failed");
            return 1;
        }
    }

    private static async Task<int> HandleStatus(IAuthService authService)
    {
        var isAuthenticated = await authService.IsAuthenticatedAsync();
        var session = await authService.GetCurrentSessionAsync();
        
        if (isAuthenticated && session != null)
        {
            Console.WriteLine("Authentication Status: ✓ Authenticated");
            Console.WriteLine($"Username: {session.Username}");
            Console.WriteLine($"Display Name: {session.DisplayName}");
            Console.WriteLine($"2FA Authenticated: {(session.TwoFactorAuthenticated ? "Yes" : "No")}");
            Console.WriteLine($"Token Expires: {session.TokenExpiry:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"Last Refresh: {session.LastRefresh:yyyy-MM-dd HH:mm:ss} UTC");
            
            if (session.GroupPermissions.Count > 0)
            {
                Console.WriteLine("\nGroup Permissions:");
                foreach (var group in session.GroupPermissions)
                {
                    Console.WriteLine($"  {group.Key}: {string.Join(", ", group.Value)}");
                }
            }
            
            return 0;
        }
        else
        {
            Console.WriteLine("Authentication Status: ✗ Not authenticated");
            return 1;
        }
    }

    private static async Task<int> Handle2FA(IAuthService authService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run 2fa <code>");
            return 1;
        }

        var code = args[1];
        
        Console.WriteLine("Verifying 2FA code...");
        
        var result = await authService.VerifyTwoFactorAsync(code);
        
        if (result.Success)
        {
            Console.WriteLine("✓ 2FA verification successful!");
            return 0;
        }
        else
        {
            Console.WriteLine($"✗ 2FA verification failed: {result.ErrorMessage}");
            return 1;
        }
    }

    private static int ShowHelp()
    {
        Console.WriteLine("VRC Group Guardian - Authentication Service CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run login <username> <password>  - Login to VRChat");
        Console.WriteLine("  dotnet run logout                       - Logout from VRChat");
        Console.WriteLine("  dotnet run status                       - Show authentication status");
        Console.WriteLine("  dotnet run 2fa <code>                   - Verify two-factor code");
        Console.WriteLine("  dotnet run help                         - Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run login myusername mypassword");
        Console.WriteLine("  dotnet run 2fa 123456");
        Console.WriteLine("  dotnet run status");
        Console.WriteLine("  dotnet run logout");
        Console.WriteLine();
        return 0;
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.WriteLine($"Unknown command: {command}");
        Console.WriteLine("Use 'dotnet run help' to see available commands.");
        return 1;
    }
}