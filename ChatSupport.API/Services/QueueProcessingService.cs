using ChatSupport.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatSupport.API.Services;

public class QueueProcessingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueueProcessingService> _logger;
    private const int ProcessingIntervalMs = 2000; // Process queue every 2 seconds

    public QueueProcessingService(IServiceProvider serviceProvider, ILogger<QueueProcessingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queue processing service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var chatAssignmentService = scope.ServiceProvider.GetRequiredService<IChatAssignmentService>();
                
                await chatAssignmentService.ProcessQueueAsync();
                
                await Task.Delay(ProcessingIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Queue processing service is stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing queue");
                await Task.Delay(ProcessingIntervalMs, stoppingToken);
            }
        }

        _logger.LogInformation("Queue processing service stopped");
    }
}