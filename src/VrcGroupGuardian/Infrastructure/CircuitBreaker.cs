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
            _statistics.IncrementCircuitOpenCount();
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
            _statistics.IncrementCircuitOpenCount();
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
        return new CircuitBreakerStatistics(
            State,
            _statistics.TotalCalls,
            _statistics.SuccessfulCalls,
            _statistics.FailedCalls,
            _statistics.CircuitOpenCount,
            _statistics.TimeoutCount,
            _failureCount,
            _lastFailureTime);
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
        _statistics.IncrementTotalCalls();

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
                _statistics.IncrementTimeoutCount();
                throw new TimeoutException($"Operation timed out after {_options.Timeout.TotalSeconds} seconds");
            }
        }
        catch (OperationCanceledException) when (cancellationTokenSource.Token.IsCancellationRequested)
        {
            _statistics.IncrementTimeoutCount();
            throw new TimeoutException($"Operation timed out after {_options.Timeout.TotalSeconds} seconds");
        }
    }

    private void OnSuccess()
    {
        lock (_lock)
        {
            _statistics.IncrementSuccessfulCalls();
            
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
            _statistics.IncrementFailedCalls();
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
    private long _totalCalls;
    private long _successfulCalls;
    private long _failedCalls;
    private long _circuitOpenCount;
    private long _timeoutCount;
    private int _currentFailureCount;
    
    public CircuitBreakerState CurrentState { get; set; }
    public long TotalCalls => _totalCalls;
    public long SuccessfulCalls => _successfulCalls;
    public long FailedCalls => _failedCalls;
    public long CircuitOpenCount => _circuitOpenCount;
    public long TimeoutCount => _timeoutCount;
    public int CurrentFailureCount => _currentFailureCount;
    public DateTime LastFailureTime { get; set; }
    
    public CircuitBreakerStatistics() { }
    
    public CircuitBreakerStatistics(CircuitBreakerState currentState, long totalCalls, long successfulCalls, 
        long failedCalls, long circuitOpenCount, long timeoutCount, int currentFailureCount, DateTime lastFailureTime)
    {
        CurrentState = currentState;
        _totalCalls = totalCalls;
        _successfulCalls = successfulCalls;
        _failedCalls = failedCalls;
        _circuitOpenCount = circuitOpenCount;
        _timeoutCount = timeoutCount;
        _currentFailureCount = currentFailureCount;
        LastFailureTime = lastFailureTime;
    }
    
    public double SuccessRate => TotalCalls > 0 ? (double)SuccessfulCalls / TotalCalls : 0;
    public double FailureRate => TotalCalls > 0 ? (double)FailedCalls / TotalCalls : 0;
    
    public void IncrementTotalCalls() => Interlocked.Increment(ref _totalCalls);
    public void IncrementSuccessfulCalls() => Interlocked.Increment(ref _successfulCalls);
    public void IncrementFailedCalls() => Interlocked.Increment(ref _failedCalls);
    public void IncrementCircuitOpenCount() => Interlocked.Increment(ref _circuitOpenCount);
    public void IncrementTimeoutCount() => Interlocked.Increment(ref _timeoutCount);
    public void SetCurrentFailureCount(int count) => _currentFailureCount = count;
}