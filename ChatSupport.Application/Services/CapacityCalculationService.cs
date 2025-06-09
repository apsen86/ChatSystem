using ChatSupport.Domain.Interfaces;
using ChatSupport.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace ChatSupport.Application.Services;

public interface ICapacityCalculationService
{
    Task<int> CalculateTeamCapacityAsync(TeamType teamType);
    Task<int> CalculateTotalCapacityAsync();
    Task<int> CalculateOverflowCapacityAsync();
    Task<bool> CanAcceptNewSessionAsync();
    Task<double> GetCurrentUtilizationRatioAsync();
}

public class CapacityCalculationService : ICapacityCalculationService
{
    private readonly IAgentRepository _agentRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IBusinessHoursService _businessHoursService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CapacityCalculationService> _logger;
    
    // Cache settings
    private const string CAPACITY_CACHE_KEY = "team_capacity";
    private const string UTILIZATION_CACHE_KEY = "utilization_ratio";
    private const int CACHE_EXPIRATION_SECONDS = 5;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(CACHE_EXPIRATION_SECONDS);
    
    // Queue can be 1.5x agent capacity
    private const double QUEUE_CAPACITY_MULTIPLIER = 1.5;

    public CapacityCalculationService(
        IAgentRepository agentRepository,
        IChatSessionRepository chatSessionRepository,
        IBusinessHoursService businessHoursService,
        IMemoryCache memoryCache,
        ILogger<CapacityCalculationService> logger)
    {
        _agentRepository = agentRepository;
        _chatSessionRepository = chatSessionRepository;
        _businessHoursService = businessHoursService;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<int> CalculateTeamCapacityAsync(TeamType teamType)
    {
        var cacheKey = $"{CAPACITY_CACHE_KEY}_{teamType}";
        
        if (_memoryCache.TryGetValue(cacheKey, out int cachedCapacity))
        {
            return cachedCapacity;
        }

        var capacity = await _agentRepository.GetTeamCapacityAsync(teamType);
        
        _memoryCache.Set(cacheKey, capacity, _cacheExpiration);
        return capacity;
    }

    public async Task<int> CalculateTotalCapacityAsync()
    {
        const string totalCacheKey = "total_capacity";
        
        if (_memoryCache.TryGetValue(totalCacheKey, out int cachedTotal))
        {
            return cachedTotal;
        }

        var teams = new[] { TeamType.TeamA, TeamType.TeamB, TeamType.TeamC };
        var totalCapacity = 0;

        foreach (var team in teams)
        {
            totalCapacity += await CalculateTeamCapacityAsync(team);
        }

        _memoryCache.Set(totalCacheKey, totalCapacity, _cacheExpiration);
        return totalCapacity;
    }

    public async Task<int> CalculateOverflowCapacityAsync()
    {
        return await CalculateTeamCapacityAsync(TeamType.Overflow);
    }

    public async Task<bool> CanAcceptNewSessionAsync()
    {
        var totalCapacity = await CalculateTotalCapacityAsync();
        var maxQueueLength = (int)Math.Floor(totalCapacity * QUEUE_CAPACITY_MULTIPLIER);
        var currentQueueLength = await _chatSessionRepository.GetQueueLengthAsync();

        if (currentQueueLength < maxQueueLength)
        {
            _logger.LogDebug("Main queue has capacity: {Current}/{Max}", currentQueueLength, maxQueueLength);
            return true;
        }

        if (await _businessHoursService.IsOfficeHoursAsync())
        {
            var overflowCapacity = await CalculateOverflowCapacityAsync();
            var maxOverflowLength = (int)Math.Floor(overflowCapacity * QUEUE_CAPACITY_MULTIPLIER);
            var currentOverflowLength = await _chatSessionRepository.GetOverflowQueueLengthAsync();
            
            var canAcceptOverflow = currentOverflowLength < maxOverflowLength;
            
            _logger.LogDebug("Overflow queue check during office hours: {Current}/{Max}, Can Accept: {CanAccept}", 
                currentOverflowLength, maxOverflowLength, canAcceptOverflow);
            
            return canAcceptOverflow;
        }

        _logger.LogDebug("Cannot accept new session - main queue full and not office hours");
        return false;
    }

    public async Task<double> GetCurrentUtilizationRatioAsync()
    {
        if (_memoryCache.TryGetValue(UTILIZATION_CACHE_KEY, out double cachedRatio))
        {
            return cachedRatio;
        }

        var totalCapacity = await CalculateTotalCapacityAsync();
        if (totalCapacity == 0) return 0.0;

        var activeSessions = await _chatSessionRepository.GetActiveSessionsAsync();
        var activeCount = activeSessions.Count();

        var ratio = (double)activeCount / totalCapacity;
        
        _memoryCache.Set(UTILIZATION_CACHE_KEY, ratio, TimeSpan.FromSeconds(2));
        return ratio;
    }

    public void InvalidateCapacityCache(TeamType? teamType = null)
    {
        if (teamType.HasValue)
        {
            _memoryCache.Remove($"{CAPACITY_CACHE_KEY}_{teamType}");
        }
        else
        {
            // Invalidate all team caches
            foreach (TeamType team in Enum.GetValues<TeamType>())
            {
                _memoryCache.Remove($"{CAPACITY_CACHE_KEY}_{team}");
            }
        }
        
        _memoryCache.Remove("total_capacity");
        _memoryCache.Remove(UTILIZATION_CACHE_KEY);
        
        _logger.LogDebug("Invalidated capacity cache for {Team}",
            teamType?.ToString() ?? "all teams");
    }
}