using System.Windows;
using System.Net;
using System.Text.Json;
using Serilog;

namespace VrcGroupGuardian.Infrastructure;

public interface IErrorHandler
{
    Task<ErrorHandlingResult> HandleExceptionAsync(Exception exception, ErrorContext context);
    Task<T> WithErrorHandlingAsync<T>(Func<Task<T>> operation, T fallbackValue, ErrorContext context);
    Task WithErrorHandlingAsync(Func<Task> operation, ErrorContext context);
    void RegisterGlobalErrorHandlers();
    ErrorStatistics GetErrorStatistics();
}

public class ErrorHandler : IErrorHandler
{
    private readonly ILogger _logger = Log.ForContext<ErrorHandler>();
    private readonly INotificationService _notificationService;
    private readonly Dictionary<Type, Func<Exception, ErrorContext, Task<ErrorHandlingResult>>> _specificHandlers = new();
    private readonly ErrorStatistics _statistics = new();

    public ErrorHandler(INotificationService notificationService)
    {
        _notificationService = notificationService;
        RegisterSpecificHandlers();
    }

    public async Task<ErrorHandlingResult> HandleExceptionAsync(Exception exception, ErrorContext context)
    {
        try
        {
            Interlocked.Increment(ref _statistics.TotalErrors);
            _logger.Error(exception, "Error in {Context}: {Message}", context.OperationName, exception.Message);

            // Check for specific exception handlers
            if (_specificHandlers.TryGetValue(exception.GetType(), out var specificHandler))
            {
                return await specificHandler(exception, context);
            }

            // Handle by category
            var result = exception switch
            {
                HttpRequestException httpEx => await HandleNetworkErrorAsync(httpEx, context),
                TaskCanceledException timeoutEx => await HandleTimeoutErrorAsync(timeoutEx, context),
                UnauthorizedAccessException authEx => await HandleAuthenticationErrorAsync(authEx, context),
                ArgumentException argEx => await HandleValidationErrorAsync(argEx, context),
                InvalidOperationException opEx => await HandleOperationErrorAsync(opEx, context),
                JsonException jsonEx => await HandleSerializationErrorAsync(jsonEx, context),
                OutOfMemoryException memEx => await HandleMemoryErrorAsync(memEx, context),
                _ => await HandleGenericErrorAsync(exception, context)
            };

            // Show user notification for critical errors if needed
            if (result.ShowToUser && context.ShowUserNotification)
            {
                await _notificationService.ShowNotificationAsync(
                    result.UserTitle ?? "Application Error",
                    result.UserMessage ?? "An unexpected error occurred.",
                    NotificationSeverity.Error);
            }

            return result;
        }
        catch (Exception handlerException)
        {
            _logger.Fatal(handlerException, "Critical error in error handler");
            return new ErrorHandlingResult
            {
                Success = false,
                Action = ErrorAction.Fail,
                ErrorMessage = "Critical system error",
                UserMessage = "A critical error occurred. Please restart the application."
            };
        }
    }

    public async Task<T> WithErrorHandlingAsync<T>(Func<Task<T>> operation, T fallbackValue, ErrorContext context)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            var result = await HandleExceptionAsync(ex, context);
            
            return result.Action switch
            {
                ErrorAction.Retry => await WithRetryAsync(operation, fallbackValue, context, 3),
                ErrorAction.UseFallback => fallbackValue,
                ErrorAction.Fail => throw new OperationFailedException($"Operation {context.OperationName} failed", ex),
                _ => fallbackValue
            };
        }
    }

    public async Task WithErrorHandlingAsync(Func<Task> operation, ErrorContext context)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            var result = await HandleExceptionAsync(ex, context);
            
            if (result.Action == ErrorAction.Retry)
            {
                await WithRetryAsync(async () => { await operation(); return true; }, true, context, 3);
            }
            else if (result.Action == ErrorAction.Fail)
            {
                throw new OperationFailedException($"Operation {context.OperationName} failed", ex);
            }
        }
    }

    public void RegisterGlobalErrorHandlers()
    {
        // Register global exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        
        if (Application.Current != null)
        {
            Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        _logger.Information("Global error handlers registered");
    }

    public ErrorStatistics GetErrorStatistics()
    {
        return new ErrorStatistics
        {
            TotalErrors = _statistics.TotalErrors,
            NetworkErrors = _statistics.NetworkErrors,
            AuthenticationErrors = _statistics.AuthenticationErrors,
            ValidationErrors = _statistics.ValidationErrors,
            MemoryErrors = _statistics.MemoryErrors,
            TimeoutErrors = _statistics.TimeoutErrors,
            CriticalErrors = _statistics.CriticalErrors,
            RecoveredErrors = _statistics.RecoveredErrors
        };
    }

    private void RegisterSpecificHandlers()
    {
        _specificHandlers[typeof(WebException)] = HandleWebExceptionAsync;
        _specificHandlers[typeof(SocketException)] = HandleSocketExceptionAsync;
    }

    private async Task<ErrorHandlingResult> HandleNetworkErrorAsync(HttpRequestException exception, ErrorContext context)
    {
        Interlocked.Increment(ref _statistics.NetworkErrors);
        _logger.Warning(exception, "Network error in {Context}", context.OperationName);

        return new ErrorHandlingResult
        {
            Success = false,
            Action = context.IsUserInitiated ? ErrorAction.Retry : ErrorAction.UseFallback,
            ErrorMessage = exception.Message,
            UserTitle = "Network Error",
            UserMessage = "Unable to connect to VRChat servers. Please check your internet connection and try again.",
            ShowToUser = true,
            RetryDelay = TimeSpan.FromSeconds(5)
        };
    }

    private async Task<ErrorHandlingResult> HandleTimeoutErrorAsync(TaskCanceledException exception, ErrorContext context)
    {
        Interlocked.Increment(ref _statistics.TimeoutErrors);
        _logger.Warning(exception, "Timeout error in {Context}", context.OperationName);

        return new ErrorHandlingResult
        {
            Success = false,
            Action = ErrorAction.Retry,
            ErrorMessage = exception.Message,
            UserTitle = "Request Timeout",
            UserMessage = "The request took too long to complete. Trying again...",
            ShowToUser = context.IsUserInitiated,
            RetryDelay = TimeSpan.FromSeconds(3)
        };
    }

    private async Task<ErrorHandlingResult> HandleAuthenticationErrorAsync(UnauthorizedAccessException exception, ErrorContext context)
    {
        Interlocked.Increment(ref _statistics.AuthenticationErrors);
        _logger.Warning(exception, "Authentication error in {Context}", context.OperationName);

        return new ErrorHandlingResult
        {
            Success = false,
            Action = ErrorAction.Fail,
            ErrorMessage = exception.Message,
            UserTitle = "Authentication Required",
            UserMessage = "Your session has expired. Please log in again.",
            ShowToUser = true
        };
    }

    private async Task<ErrorHandlingResult> HandleValidationErrorAsync(ArgumentException exception, ErrorContext context)
    {
        Interlocked.Increment(ref _statistics.ValidationErrors);
        _logger.Warning(exception, "Validation error in {Context}", context.OperationName);

        return new ErrorHandlingResult
        {
            Success = false,
            Action = ErrorAction.UseFallback,
            ErrorMessage = exception.Message,
            UserTitle = "Invalid Input",
            UserMessage = "Please check your input and try again.",
            ShowToUser = context.IsUserInitiated
        };
    }

    private async Task<ErrorHandlingResult> HandleOperationErrorAsync(InvalidOperationException exception, ErrorContext context)
    {
        _logger.Warning(exception, "Operation error in {Context}", context.OperationName);

        return new ErrorHandlingResult
        {
            Success = false,
            Action = ErrorAction.UseFallback,
            ErrorMessage = exception.Message,
            UserTitle = "Operation Failed",
            UserMessage = "The requested operation could not be completed.",
            ShowToUser = context.IsUserInitiated
        };
    }

    private async Task<ErrorHandlingResult> HandleSerializationErrorAsync(JsonException exception, ErrorContext context)
    {
        _logger.Warning(exception, "Serialization error in {Context}", context.OperationName);

        return new ErrorHandlingResult
        {
            Success = false,
            Action = ErrorAction.UseFallback,
            ErrorMessage = exception.Message,
            UserTitle = "Data Error",
            UserMessage = "There was a problem processing the server response.",
            ShowToUser = context.IsUserInitiated
        };
    }

    private async Task<ErrorHandlingResult> HandleMemoryErrorAsync(OutOfMemoryException exception, ErrorContext context)
    {
        Interlocked.Increment(ref _statistics.MemoryErrors);
        _logger.Error(exception, "Memory error in {Context}", context.OperationName);

        // Force garbage collection
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);

        return new ErrorHandlingResult
        {
            Success = false,
            Action = ErrorAction.Fail,
            ErrorMessage = exception.Message,
            UserTitle = "Memory Error",
            UserMessage = "The application is running low on memory. Please close other applications and try again.",
            ShowToUser = true
        };
    }

    private async Task<ErrorHandlingResult> HandleGenericErrorAsync(Exception exception, ErrorContext context)
    {
        _logger.Error(exception, "Unhandled error in {Context}", context.OperationName);

        return new ErrorHandlingResult
        {
            Success = false,
            Action = ErrorAction.UseFallback,
            ErrorMessage = exception.Message,
            UserTitle = "Unexpected Error",
            UserMessage = "An unexpected error occurred. The application will try to continue.",
            ShowToUser = context.ShowUserNotification
        };
    }

    private async Task<ErrorHandlingResult> HandleWebExceptionAsync(Exception exception, ErrorContext context)
    {
        var webEx = (WebException)exception;
        Interlocked.Increment(ref _statistics.NetworkErrors);

        var action = webEx.Status switch
        {
            WebExceptionStatus.Timeout => ErrorAction.Retry,
            WebExceptionStatus.NameResolutionFailure => ErrorAction.Fail,
            WebExceptionStatus.ConnectFailure => ErrorAction.Retry,
            WebExceptionStatus.ProtocolError => ErrorAction.UseFallback,
            _ => ErrorAction.UseFallback
        };

        return new ErrorHandlingResult
        {
            Success = false,
            Action = action,
            ErrorMessage = webEx.Message,
            UserTitle = "Network Error",
            UserMessage = GetWebExceptionUserMessage(webEx.Status),
            ShowToUser = true,
            RetryDelay = action == ErrorAction.Retry ? TimeSpan.FromSeconds(5) : null
        };
    }

    private async Task<ErrorHandlingResult> HandleSocketExceptionAsync(Exception exception, ErrorContext context)
    {
        var socketEx = (System.Net.Sockets.SocketException)exception;
        Interlocked.Increment(ref _statistics.NetworkErrors);

        return new ErrorHandlingResult
        {
            Success = false,
            Action = ErrorAction.Retry,
            ErrorMessage = socketEx.Message,
            UserTitle = "Connection Error",
            UserMessage = "Unable to establish a network connection. Retrying...",
            ShowToUser = context.IsUserInitiated,
            RetryDelay = TimeSpan.FromSeconds(3)
        };
    }

    private async Task<T> WithRetryAsync<T>(Func<Task<T>> operation, T fallbackValue, ErrorContext context, int maxRetries)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1))); // Exponential backoff
                    _logger.Information("Retrying operation {Operation}, attempt {Attempt}/{MaxRetries}", 
                        context.OperationName, attempt, maxRetries);
                }

                var result = await operation();
                
                if (attempt > 1)
                {
                    Interlocked.Increment(ref _statistics.RecoveredErrors);
                    _logger.Information("Operation {Operation} succeeded on retry attempt {Attempt}", 
                        context.OperationName, attempt);
                }
                
                return result;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.Warning(ex, "Retry attempt {Attempt} failed for operation {Operation}", 
                    attempt, context.OperationName);
            }
        }

        _logger.Error("All retry attempts failed for operation {Operation}", context.OperationName);
        return fallbackValue;
    }

    private string GetWebExceptionUserMessage(WebExceptionStatus status)
    {
        return status switch
        {
            WebExceptionStatus.Timeout => "The request timed out. Please try again.",
            WebExceptionStatus.NameResolutionFailure => "Unable to resolve server address. Please check your internet connection.",
            WebExceptionStatus.ConnectFailure => "Unable to connect to the server. Please try again later.",
            WebExceptionStatus.ProtocolError => "Server returned an error. Please try again.",
            _ => "A network error occurred. Please check your connection and try again."
        };
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        Interlocked.Increment(ref _statistics.CriticalErrors);
        _logger.Fatal(exception, "Unhandled exception occurred. Terminating: {IsTerminating}", e.IsTerminating);

        if (e.IsTerminating)
        {
            try
            {
                MessageBox.Show(
                    "A critical error occurred and the application must exit. Please check the logs for details.",
                    "Critical Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Ignore errors when showing the message box
            }
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Interlocked.Increment(ref _statistics.CriticalErrors);
        _logger.Error(e.Exception, "Unobserved task exception occurred");
        
        // Mark as observed to prevent application termination
        e.SetObserved();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Interlocked.Increment(ref _statistics.CriticalErrors);
        _logger.Error(e.Exception, "Unhandled dispatcher exception occurred");

        try
        {
            var context = new ErrorContext 
            { 
                OperationName = "UI Operation",
                ShowUserNotification = true,
                IsUserInitiated = true
            };

            var task = HandleExceptionAsync(e.Exception, context);
            task.Wait(TimeSpan.FromSeconds(5)); // Don't wait forever

            // Mark as handled to prevent application crash
            e.Handled = true;
        }
        catch (Exception handlerEx)
        {
            _logger.Fatal(handlerEx, "Critical error in dispatcher exception handler");
        }
    }
}

public class ErrorHandlingResult
{
    public bool Success { get; set; }
    public ErrorAction Action { get; set; }
    public string? ErrorMessage { get; set; }
    public string? UserTitle { get; set; }
    public string? UserMessage { get; set; }
    public bool ShowToUser { get; set; }
    public TimeSpan? RetryDelay { get; set; }
}

public class ErrorContext
{
    public string OperationName { get; set; } = "";
    public bool IsUserInitiated { get; set; }
    public bool ShowUserNotification { get; set; } = true;
    public string? ComponentName { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}

public class ErrorStatistics
{
    public long TotalErrors { get; set; }
    public long NetworkErrors { get; set; }
    public long AuthenticationErrors { get; set; }
    public long ValidationErrors { get; set; }
    public long MemoryErrors { get; set; }
    public long TimeoutErrors { get; set; }
    public long CriticalErrors { get; set; }
    public long RecoveredErrors { get; set; }
}

public enum ErrorAction
{
    UseFallback,
    Retry,
    Fail
}

public class OperationFailedException : Exception
{
    public OperationFailedException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}