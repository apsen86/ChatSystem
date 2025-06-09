using ChatSupport.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChatSupport.Application.Services;

public interface IBusinessHoursService
{
    Task<bool> IsOfficeHoursAsync();
    Task<bool> IsWithinBusinessDayAsync();
}

public class BusinessHoursService : IBusinessHoursService
{
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<BusinessHoursService> _logger;

    public BusinessHoursService(ITimeProvider timeProvider, ILogger<BusinessHoursService> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // Check if current time is office hours (M-F 9-5 EST)
    public Task<bool> IsOfficeHoursAsync()
    {
        var now = _timeProvider.UtcNow;
        
        // Office hours: Monday-Friday, 9 AM to 5 PM in Eastern Standard Time
        if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            return Task.FromResult(false);
        
        try
        {
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var businessTime = TimeZoneInfo.ConvertTimeFromUtc(now, easternZone);
            
            var isOfficeTime = businessTime.Hour >= 9 && businessTime.Hour < 17;
            return Task.FromResult(isOfficeTime);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback to UTC if timezone not found
            _logger.LogWarning("Eastern Standard Time zone not found, falling back to UTC");
            var isOfficeTime = now.Hour >= 14 && now.Hour < 22; // 9 AM - 5 PM EST in UTC (approximate)
            return Task.FromResult(isOfficeTime);
        }
    }

    public Task<bool> IsWithinBusinessDayAsync()
    {
        var now = _timeProvider.UtcNow;
        var isBusinessDay = now.DayOfWeek != DayOfWeek.Saturday && now.DayOfWeek != DayOfWeek.Sunday;
        return Task.FromResult(isBusinessDay);
    }
}