using ChatSupport.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatSupport.API.Services;

public class SessionMonitoringService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionMonitoringService> _logger;
    
    // Monitor every 5 seconds
    private const int MONITORING_INTERVAL_SECONDS = 5;
    private const int MonitoringIntervalMs = MONITORING_INTERVAL_SECONDS * 1000;

    public SessionMonitoringService(IServiceProvider serviceProvider, ILogger<SessionMonitoringService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Session monitoring service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var chatAssignmentService = scope.ServiceProvider.GetRequiredService<IChatAssignmentService>();
                
                await chatAssignmentService.MonitorSessionsAsync();
                
                await Task.Delay(MonitoringIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Session monitoring service is stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while monitoring sessions");
                await Task.Delay(MonitoringIntervalMs, stoppingToken);
            }
        }

        _logger.LogInformation("Session monitoring service stopped");
    }
}