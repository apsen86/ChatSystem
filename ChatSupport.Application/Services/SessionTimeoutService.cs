using ChatSupport.Domain.Models;
using ChatSupport.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChatSupport.Application.Services;

public interface ISessionTimeoutService
{
    Task ProcessSessionTimeoutsAsync();
    Task IncrementMissedPollsForStaleSessionsAsync();
}

public class SessionTimeoutService : ISessionTimeoutService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<SessionTimeoutService> _logger;

    public SessionTimeoutService(
        IChatSessionRepository chatSessionRepository,
        IAgentRepository agentRepository,
        ITimeProvider timeProvider,
        ILogger<SessionTimeoutService> logger)
    {
        _chatSessionRepository = chatSessionRepository;
        _agentRepository = agentRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ProcessSessionTimeoutsAsync()
    {
        _logger.LogDebug("Processing session timeouts");

        // First, increment missed polls for stale sessions
        await IncrementMissedPollsForStaleSessionsAsync();

        // Then process sessions that have timed out
        var timedOutSessions = await _chatSessionRepository.GetTimedOutSessionsAsync();
        
        if (!timedOutSessions.Any())
        {
            _logger.LogDebug("No timed out sessions to process");
            return;
        }

        await ProcessTimedOutSessionsBatch(timedOutSessions.ToList());
    }

    public async Task IncrementMissedPollsForStaleSessionsAsync()
    {
        var activeSessions = await _chatSessionRepository.GetActiveSessionsForMonitoringAsync();
        var staleSessions = new List<ChatSession>();
        
        // Use atomic operation to prevent race conditions
        foreach (var session in activeSessions)
        {
            // Use thread-safe atomic check and increment
            var wasIncremented = session.IncrementMissedPollIfStale(_timeProvider, 1.0);
            if (wasIncremented)
            {
                staleSessions.Add(session);
            }
        }

        if (staleSessions.Any())
        {
            await _chatSessionRepository.UpdateManyAsync(staleSessions);
            _logger.LogDebug("Incremented missed polls for {Count} stale sessions", staleSessions.Count);
        }
    }

    private async Task ProcessTimedOutSessionsBatch(List<ChatSession> timedOutSessions)
    {
        _logger.LogInformation("Processing {Count} timed out sessions", timedOutSessions.Count);
        
        var agentsToUpdate = new List<Agent>();
        
        foreach (var session in timedOutSessions)
        {
            _logger.LogWarning("Session {SessionId} marked inactive due to {MissedPolls} missed polls", 
                session.Id, session.MissedPollCount);
            
            session.MarkInactive();

            // Release agent if assigned
            if (session.AssignedAgentId.HasValue)
            {
                var agent = await _agentRepository.GetByIdAsync(session.AssignedAgentId.Value);
                if (agent != null)
                {
                    agent.CompleteChat();
                    agentsToUpdate.Add(agent);
                }
            }
        }

        // Bulk update sessions and agents
        try
        {
            await _chatSessionRepository.UpdateManyAsync(timedOutSessions);
            
            if (agentsToUpdate.Any())
            {
                await _agentRepository.UpdateManyAsync(agentsToUpdate);
            }
            
            _logger.LogInformation("Successfully processed {SessionCount} timed out sessions", 
                timedOutSessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process timed out sessions batch");
            throw;
        }
    }
}