using Serilog;

namespace VrcGroupGuardian.Infrastructure;

public interface IDryRunMode
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();
    void SetMode(bool enabled);
    Task<T> ExecuteOrSimulateAsync<T>(Func<Task<T>> operation, T mockResult, string operationName);
    Task ExecuteOrSimulateAsync(Func<Task> operation, string operationName);
}

public class DryRunMode : IDryRunMode
{
    private readonly ILogger _logger = Log.ForContext<DryRunMode>();
    private bool _isEnabled = false; // Changed to false for production use

    public bool IsEnabled => _isEnabled;

    public void Enable()
    {
        _isEnabled = true;
        _logger.Information("Dry run mode enabled - API calls will be simulated");
    }

    public void Disable()
    {
        _isEnabled = false;
        _logger.Information("Dry run mode disabled - API calls will be executed normally");
    }

    public void SetMode(bool enabled)
    {
        if (enabled)
            Enable();
        else
            Disable();
    }

    public async Task<T> ExecuteOrSimulateAsync<T>(Func<Task<T>> operation, T mockResult, string operationName)
    {
        if (!_isEnabled)
        {
            _logger.Debug("Executing real operation: {OperationName}", operationName);
            return await operation();
        }

        _logger.Information("Simulating operation: {OperationName} (returning mock result)", operationName);
        
        // Simulate some processing time
        await Task.Delay(Random.Shared.Next(100, 500));
        
        return mockResult;
    }

    public async Task ExecuteOrSimulateAsync(Func<Task> operation, string operationName)
    {
        if (!_isEnabled)
        {
            _logger.Debug("Executing real operation: {OperationName}", operationName);
            await operation();
            return;
        }

        _logger.Information("Simulating operation: {OperationName}", operationName);
        
        // Simulate some processing time
        await Task.Delay(Random.Shared.Next(100, 500));
    }
}