using Serilog;

namespace VrcGroupGuardian.Infrastructure;

public interface ICircuitBreaker
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, T fallbackValue);
    Task ExecuteAsync(Func<Task> operation);
    CircuitBreakerState State { get; }
    void Reset();
    CircuitBreakerStatistics GetStatistics();
}

public class CircuitBreaker : ICircuitBreaker
{
    private readonly ILogger _logger = Log.ForContext<CircuitBreaker>();
    private readonly CircuitBreakerOptions _options;
    private readonly object _lock = new object();
    
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private DateTime _nextAttemptTime = DateTime.MinValue;
    private readonly CircuitBreakerStatistics _statistics = new();

    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public CircuitBreaker(CircuitBreakerOptions? options = null)
    {
        _options = options ?? new CircuitBreakerOptions();
        _logger.Information("Circuit breaker initialized with {FailureThreshold} failure threshold and {TimeoutMs}ms timeout", 
            _options.FailureThreshold, _options.Timeout.TotalMilliseconds);
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, T fallbackValue)
    {
        if (!CanExecute())
        {
            _logger.Warning("Circuit breaker is open, returning fallback value");
            Interlocked.Increment(ref _statistics.CircuitOpenCount);
            return fallbackValue;
        }

        try
        {
            var result = await ExecuteWithTimeout(operation);
            OnSuccess();
            return result;
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            
            if (_state == CircuitBreakerState.Open)
            {
                _logger.Warning("Circuit breaker opened due to failure, returning fallback value");
                return fallbackValue;
            }
            
            throw;
        }
    }

    public async Task ExecuteAsync(Func<Task> operation)
    {
        if (!CanExecute())
        {
            _logger.Warning("Circuit breaker is open, skipping operation");
            Interlocked.Increment(ref _statistics.CircuitOpenCount);
            return;
        }

        try
        {
            await ExecuteWithTimeout(async () =>
            {
                await operation();
                return true;
            });
            OnSuccess();
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            
            if (_state != CircuitBreakerState.Open)
            {
                throw;
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Closed;
            _failureCount = 0;
            _lastFailureTime = DateTime.MinValue;
            _nextAttemptTime = DateTime.MinValue;
            
            _logger.Information("Circuit breaker reset to closed state");
        }
    }

    public CircuitBreakerStatistics GetStatistics()
    {
        return new CircuitBreakerStatistics
        {
            CurrentState = State,
            TotalCalls = _statistics.TotalCalls,
            SuccessfulCalls = _statistics.SuccessfulCalls,
            FailedCalls = _statistics.FailedCalls,
            CircuitOpenCount = _statistics.CircuitOpenCount,
            TimeoutCount = _statistics.TimeoutCount,
            CurrentFailureCount = _failureCount,
            LastFailureTime = _lastFailureTime
        };
    }

    private bool CanExecute()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case CircuitBreakerState.Closed:
                    return true;

                case CircuitBreakerState.Open:
                    if (DateTime.UtcNow >= _nextAttemptTime)
                    {
                        _state = CircuitBreakerState.HalfOpen;
                        _logger.Information("Circuit breaker moved to half-open state");
                        return true;
                    }
                    return false;

                case CircuitBreakerState.HalfOpen:
                    return true;

                default:
                    return false;
            }
        }
    }

    private async Task<T> ExecuteWithTimeout<T>(Func<Task<T>> operation)
    {
        Interlocked.Increment(ref _statistics.TotalCalls);

        using var cancellationTokenSource = new CancellationTokenSource(_options.Timeout);
        
        try
        {
            var task = operation();
            var completedTask = await Task.WhenAny(task, Task.Delay(_options.Timeout, cancellationTokenSource.Token));
            
            if (completedTask == task)
            {
                return await task;
            }
            else
            {
                Interlocked.Increment(ref _statistics.TimeoutCount);
                throw new TimeoutException($"Operation timed out after {_options.Timeout.TotalSeconds} seconds");
            }
        }
        catch (OperationCanceledException) when (cancellationTokenSource.Token.IsCancellationRequested)
        {
            Interlocked.Increment(ref _statistics.TimeoutCount);
            throw new TimeoutException($"Operation timed out after {_options.Timeout.TotalSeconds} seconds");
        }
    }

    private void OnSuccess()
    {
        lock (_lock)
        {
            Interlocked.Increment(ref _statistics.SuccessfulCalls);
            
            if (_state == CircuitBreakerState.HalfOpen)
            {
                _state = CircuitBreakerState.Closed;
                _failureCount = 0;
                _logger.Information("Circuit breaker moved to closed state after successful operation");
            }
            else if (_state == CircuitBreakerState.Closed && _failureCount > 0)
            {
                _failureCount = 0;
                _logger.Debug("Circuit breaker failure count reset after successful operation");
            }
        }
    }

    private void OnFailure(Exception exception)
    {
        lock (_lock)
        {
            Interlocked.Increment(ref _statistics.FailedCalls);
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;
            
            _logger.Warning(exception, "Circuit breaker registered failure {FailureCount}/{Threshold}", 
                _failureCount, _options.FailureThreshold);

            if (_state == CircuitBreakerState.HalfOpen)
            {
                // Half-open state failed, go back to open
                _state = CircuitBreakerState.Open;
                _nextAttemptTime = DateTime.UtcNow.Add(_options.RetryDelay);
                _logger.Warning("Circuit breaker moved to open state after half-open failure");
            }
            else if (_state == CircuitBreakerState.Closed && _failureCount >= _options.FailureThreshold)
            {
                // Too many failures, open the circuit
                _state = CircuitBreakerState.Open;
                _nextAttemptTime = DateTime.UtcNow.Add(_options.RetryDelay);
                _logger.Error("Circuit breaker opened due to {FailureCount} consecutive failures", _failureCount);
            }
        }
    }
}

public class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(1);
}

public enum CircuitBreakerState
{
    Closed,    // Normal operation
    Open,      // Circuit is open, calls are blocked
    HalfOpen   // Testing if the circuit can be closed
}

public class CircuitBreakerStatistics
{
    public CircuitBreakerState CurrentState { get; set; }
    public long TotalCalls { get; set; }
    public long SuccessfulCalls { get; set; }
    public long FailedCalls { get; set; }
    public long CircuitOpenCount { get; set; }
    public long TimeoutCount { get; set; }
    public int CurrentFailureCount { get; set; }
    public DateTime LastFailureTime { get; set; }
    
    public double SuccessRate => TotalCalls > 0 ? (double)SuccessfulCalls / TotalCalls : 0;
    public double FailureRate => TotalCalls > 0 ? (double)FailedCalls / TotalCalls : 0;
}