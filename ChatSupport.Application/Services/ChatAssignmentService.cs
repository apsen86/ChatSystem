using ChatSupport.Domain.Models;
using ChatSupport.Domain.Interfaces;
using ChatSupport.Domain.Enums;
using ChatSupport.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace ChatSupport.Application.Services;

public class ChatAssignmentService : IChatAssignmentService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly ISessionCreationService _sessionCreationService;
    private readonly ICapacityCalculationService _capacityCalculationService;
    private readonly IAgentAssignmentService _agentAssignmentService;
    private readonly IAgentSelectionService _agentSelectionService;
    private readonly ISessionTimeoutService _sessionTimeoutService;
    private readonly IBusinessHoursService _businessHoursService;
    private readonly IShiftManagementService _shiftManagementService;
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<ChatAssignmentService> _logger;
    
    public ChatAssignmentService(
        IChatSessionRepository chatSessionRepository,
        IAgentRepository agentRepository,
        ISessionCreationService sessionCreationService,
        ICapacityCalculationService capacityCalculationService,
        IAgentAssignmentService agentAssignmentService,
        IAgentSelectionService agentSelectionService,
        ISessionTimeoutService sessionTimeoutService,
        IBusinessHoursService businessHoursService,
        IShiftManagementService shiftManagementService,
        ITimeProvider timeProvider,
        ILogger<ChatAssignmentService> logger)
    {
        _chatSessionRepository = chatSessionRepository;
        _agentRepository = agentRepository;
        _sessionCreationService = sessionCreationService;
        _capacityCalculationService = capacityCalculationService;
        _agentAssignmentService = agentAssignmentService;
        _agentSelectionService = agentSelectionService;
        _sessionTimeoutService = sessionTimeoutService;
        _businessHoursService = businessHoursService;
        _shiftManagementService = shiftManagementService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ChatSession> CreateChatSessionAsync(string userId)
    {
        return await _sessionCreationService.CreateSessionAsync(userId);
    }

    public async Task<bool> PollSessionAsync(Guid sessionId)
    {
        var session = await _chatSessionRepository.GetByIdAsync(sessionId);
        if (session == null)
        {
            _logger.LogWarning("Poll attempted for non-existent session {SessionId}", sessionId);
            return false;
        }

        session.Poll();
        await _chatSessionRepository.UpdateAsync(session);
        
        _logger.LogDebug("Session {SessionId} polled, count: {PollCount}", sessionId, session.PollCount);
        return true;
    }

    public async Task ProcessQueueAsync()
    {
        _logger.LogDebug("Processing chat queue");

        // Process main queue first
        await ProcessRegularQueueAsync();
        
        if (await _businessHoursService.IsOfficeHoursAsync())
        {
            await ProcessOverflowQueueAsync();
        }
    }

    public async Task MonitorSessionsAsync()
    {
        await _sessionTimeoutService.ProcessSessionTimeoutsAsync();
    }

    public async Task<bool> CanAcceptNewChatAsync()
    {
        return await _capacityCalculationService.CanAcceptNewSessionAsync();
    }

    private async Task ProcessRegularQueueAsync()
    {
        _logger.LogDebug("Processing regular queue");
        
        // Use batch processing instead of one-by-one to avoid N+1 queries
        await _agentAssignmentService.ProcessQueueBatchAsync();
        
        // Handle overflow transition for remaining sessions
        if (await _businessHoursService.IsOfficeHoursAsync())
        {
            await MoveUnassignedSessionsToOverflow();
        }
    }

    private async Task ProcessOverflowQueueAsync()
    {
        _logger.LogDebug("Processing overflow queue");
        
        var overflowSessions = await _chatSessionRepository.GetOverflowSessionsAsync();
        var availableAgents = await _agentRepository.GetAgentsByTeamAsync(TeamType.Overflow);
        var activeAgents = availableAgents.Where(a => a.CanAcceptNewChat()).ToList();

        if (!overflowSessions.Any() || !activeAgents.Any())
        {
            _logger.LogDebug("No overflow sessions to process or no overflow agents available");
            return;
        }

        var sessionsToProcess = overflowSessions.Take(10).ToList();
        var assignments = await _agentSelectionService.CreateOptimalAssignmentsAsync(sessionsToProcess, activeAgents);

        foreach (var (session, agent) in assignments)
        {
            await _agentAssignmentService.TryAssignSessionAsync(session, agent);
        }
    }

    private async Task MoveUnassignedSessionsToOverflow()
    {
        var queuedSessions = await _chatSessionRepository.GetQueuedSessionsAsync();
        var unassignedSessions = queuedSessions
            .Where(s => !s.IsInOverflow && s.Status == ChatSessionStatus.Queued)
            .Take(5) // Limit to avoid overwhelming overflow
            .ToList();

        foreach (var session in unassignedSessions)
        {
            session.MoveToOverflow();
            await _chatSessionRepository.UpdateAsync(session);
            _logger.LogInformation("Session {SessionId} moved to overflow queue", session.Id);
        }
    }

    public async Task<int> GetQueuePositionAsync(Guid sessionId)
    {
        var session = await _chatSessionRepository.GetByIdAsync(sessionId);
        if (session == null || session.Status != ChatSessionStatus.Queued)
        {
            return 0;
        }

        var queuedSessions = await _chatSessionRepository.GetQueuedSessionsAsync();
        var sessionList = queuedSessions
            .Where(s => s.IsInOverflow == session.IsInOverflow)
            .OrderBy(s => s.CreatedAt)
            .ToList();

        var position = sessionList.FindIndex(s => s.Id == sessionId) + 1;
        return Math.Max(position, 0);
    }

    public async Task<TimeSpan?> GetEstimatedWaitTimeAsync(Guid sessionId)
    {
        var session = await _chatSessionRepository.GetByIdAsync(sessionId);
        if (session == null || session.Status != ChatSessionStatus.Queued)
        {
            return null;
        }

        var position = await GetQueuePositionAsync(sessionId);
        if (position <= 0)
        {
            return TimeSpan.Zero;
        }

        // Calculate available agents for this queue type
        var availableAgents = session.IsInOverflow
            ? await _agentRepository.GetAgentsByTeamAsync(TeamType.Overflow)
            : await _agentRepository.GetAvailableAgentsAsync();

        var activeAgentCount = availableAgents.Count(a => a.CanAcceptNewChat());
        if (activeAgentCount == 0)
        {
            return null; // No agents available
        }

        // Estimate 5 minutes per chat assignment on average
        var averageHandlingTime = TimeSpan.FromMinutes(5);
        var estimatedWait = TimeSpan.FromTicks(averageHandlingTime.Ticks * position / activeAgentCount);
        
        return estimatedWait;
    }
}