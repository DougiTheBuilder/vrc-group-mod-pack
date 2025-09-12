using Microsoft.Extensions.Logging;
using System.Net.Http;
using Polly;
using Polly.Extensions.Http;
using System.Net;

namespace VrcGroupGuardian.Infrastructure;

public interface IVrchatHttpClientFactory
{
    HttpClient CreateClient(string? authToken = null);
    HttpClient CreateRateLimitedClient(string? authToken = null, int requestsPerMinute = 20);
}

public class VrchatHttpClientFactory : IVrchatHttpClientFactory
{
    private readonly ILogger<VrchatHttpClientFactory> _logger;
    private readonly IRateLimitService _rateLimitService;

    public VrchatHttpClientFactory(ILogger<VrchatHttpClientFactory> logger, IRateLimitService rateLimitService)
    {
        _logger = logger;
        _rateLimitService = rateLimitService;
    }

    public HttpClient CreateClient(string? authToken = null)
    {
        var client = new HttpClient(CreateMessageHandler());
        ConfigureClient(client, authToken);
        return client;
    }

    public HttpClient CreateRateLimitedClient(string? authToken = null, int requestsPerMinute = 20)
    {
        var rateLimitedHandler = new RateLimitedHttpMessageHandler(_rateLimitService, requestsPerMinute);
        rateLimitedHandler.InnerHandler = CreateMessageHandler();
        
        var client = new HttpClient(rateLimitedHandler);
        ConfigureClient(client, authToken);
        return client;
    }

    private HttpClientHandler CreateMessageHandler()
    {
        return new HttpClientHandler()
        {
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
    }

    private void ConfigureClient(HttpClient client, string? authToken)
    {
        client.BaseAddress = new Uri("https://api.vrchat.cloud/");
        client.DefaultRequestHeaders.Add("User-Agent", "VrcGroupGuardian/1.0");
        
        if (!string.IsNullOrEmpty(authToken))
        {
            client.DefaultRequestHeaders.Add("Cookie", $"auth={authToken}");
        }

        // Increase timeout for VRChat API calls, especially 2FA which can be slow
        client.Timeout = TimeSpan.FromSeconds(90);
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var logger = context.GetLogger();
                    if (outcome.Exception != null)
                    {
                        logger?.LogWarning("HTTP retry {RetryCount} in {Delay}ms due to {Exception}", retryCount, timespan.TotalMilliseconds, outcome.Exception.Message);
                    }
                    else
                    {
                        logger?.LogWarning("HTTP retry {RetryCount} in {Delay}ms due to {StatusCode}", retryCount, timespan.TotalMilliseconds, outcome.Result?.StatusCode);
                    }
                });
    }
}

public class RateLimitedHttpMessageHandler : DelegatingHandler
{
    private readonly IRateLimitService _rateLimitService;
    private readonly int _requestsPerMinute;

    public RateLimitedHttpMessageHandler(IRateLimitService rateLimitService, int requestsPerMinute)
    {
        _rateLimitService = rateLimitService;
        _requestsPerMinute = requestsPerMinute;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _rateLimitService.WaitForAvailabilityAsync(_requestsPerMinute, cancellationToken);
        
        var response = await base.SendAsync(request, cancellationToken);
        
        // Handle rate limiting responses
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1);
            await Task.Delay(retryAfter, cancellationToken);
            
            // Retry the request once after rate limit
            await _rateLimitService.WaitForAvailabilityAsync(_requestsPerMinute, cancellationToken);
            response = await base.SendAsync(request, cancellationToken);
        }
        
        return response;
    }
}

public static class PollyContextExtensions
{
    public static ILogger? GetLogger(this Context context)
    {
        if (context.TryGetValue("logger", out var logger) && logger is ILogger loggerInstance)
        {
            return loggerInstance;
        }
        return null;
    }
}