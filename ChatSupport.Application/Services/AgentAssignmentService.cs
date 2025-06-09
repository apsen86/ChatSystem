using ChatSupport.Domain.Models;
using ChatSupport.Domain.Interfaces;
using ChatSupport.Domain.Enums;
using ChatSupport.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace ChatSupport.Application.Services;

public interface IAgentAssignmentService
{
    Task<Agent?> FindAvailableAgentAsync();
    Task<Agent?> FindAvailableAgentAsync(bool useOverflow);
    Task<bool> TryAssignSessionAsync(ChatSession session, Agent agent);
    Task ProcessQueueBatchAsync();
}

public class AgentAssignmentService : IAgentAssignmentService
{
    private readonly IAgentRepository _agentRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IAgentSelectionService _agentSelectionService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<AgentAssignmentService> _logger;
    
    // Cache and retry settings
    private const string CAPACITY_CACHE_PREFIX = "agent_capacity";
    private const int CACHE_EXPIRATION_SECONDS = 10;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(CACHE_EXPIRATION_SECONDS);
    private const int MAX_ASSIGNMENT_RETRIES = 3;
    private const int BATCH_PROCESSING_SIZE = 10;
    private const int RETRY_DELAY_BASE_MS = 100;

    public AgentAssignmentService(
        IAgentRepository agentRepository,
        IChatSessionRepository chatSessionRepository,
        IAgentSelectionService agentSelectionService,
        IMemoryCache memoryCache,
        ILogger<AgentAssignmentService> logger)
    {
        _agentRepository = agentRepository;
        _chatSessionRepository = chatSessionRepository;
        _agentSelectionService = agentSelectionService;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<Agent?> FindAvailableAgentAsync()
    {
        return await FindAvailableAgentAsync(false);
    }

    public async Task<Agent?> FindAvailableAgentAsync(bool useOverflow)
    {
        var teams = useOverflow 
            ? new[] { TeamType.Overflow }
            : new[] { TeamType.TeamA, TeamType.TeamB, TeamType.TeamC };

        foreach (var team in teams)
        {
            var agent = await _agentSelectionService.SelectNextAvailableAgentAsync(team, useOverflow);
            if (agent != null)
            {
                _logger.LogDebug("Found available agent {AgentId} ({AgentName}) from team {Team}",
                    agent.Id, agent.Name, team);
                return agent;
            }
        }

        _logger.LogDebug("No available agents found in {QueueType} queue", 
            useOverflow ? "overflow" : "regular");
        return null;
    }

    public async Task<bool> TryAssignSessionAsync(ChatSession session, Agent agent)
    {
        var retryCount = 0;
        
        while (retryCount < MAX_ASSIGNMENT_RETRIES)
        {
            try
            {
                // Use proper capacity reservation instead of direct locking
                if (!agent.CanAcceptNewChat())
                {
                    _logger.LogWarning("Agent {AgentId} cannot accept new chat during assignment", agent.Id);
                    return false;
                }

                // Atomically assign session and confirm agent reservation
                session.AssignToAgent(agent.Id);
                
                // If using reservation system, confirm it; otherwise assign directly
                var assignmentSuccessful = agent.ConfirmReservation() || TryDirectAssignment(agent);
                
                if (!assignmentSuccessful)
                {
                    _logger.LogWarning("Failed to confirm agent {AgentId} reservation for session {SessionId}", 
                        agent.Id, session.Id);
                    return false;
                }

                // Persist changes
                await _chatSessionRepository.UpdateAsync(session);
                await _agentRepository.UpdateAsync(agent);

                // Invalidate capacity cache
                InvalidateCapacityCache(agent.TeamType);

                _logger.LogInformation("Session {SessionId} assigned to agent {AgentId} ({AgentName}) - Seniority: {Seniority}", 
                    session.Id, agent.Id, agent.Name, agent.Seniority);
                return true;
            }
            catch (Exception ex) when (retryCount < MAX_ASSIGNMENT_RETRIES - 1)
            {
                retryCount++;
                _logger.LogWarning(ex, "Assignment attempt {Attempt} failed for session {SessionId}, retrying...", 
                    retryCount, session.Id);
                
                // Safe rollback
                agent.ReleaseReservation();
                
                await Task.Delay(TimeSpan.FromMilliseconds(RETRY_DELAY_BASE_MS * retryCount));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error assigning session {SessionId} to agent {AgentId}", 
                    session.Id, agent.Id);
                
                // Safe final rollback
                agent.ReleaseReservation();
                throw;
            }
        }
        
        _logger.LogError("Failed to assign session {SessionId} after {MaxRetries} retries", session.Id, MAX_ASSIGNMENT_RETRIES);
        return false;
    }

    public async Task ProcessQueueBatchAsync()
    {
        // Use the batch processing size constant
        
        // Load all data once to prevent N+1 queries
        var availableAgents = await _agentRepository.GetAvailableAgentsAsync();
        var agentList = availableAgents.ToList();
        
        if (!agentList.Any())
        {
            _logger.LogDebug("No available agents for queue processing");
            return;
        }

        var queuedSessions = await _chatSessionRepository.GetQueuedSessionsAsync();
        var sessionsToProcess = queuedSessions
            .Where(s => !s.IsInOverflow)
            .Take(Math.Min(BATCH_PROCESSING_SIZE, agentList.Count))
            .ToList();

        if (!sessionsToProcess.Any())
        {
            _logger.LogDebug("No sessions in main queue to process");
            return;
        }

        _logger.LogInformation("Processing batch of {SessionCount} sessions with {AgentCount} available agents",
            sessionsToProcess.Count, agentList.Count);

        // Use optimized assignment logic with proper capacity reservation
        var assignments = await _agentSelectionService.CreateOptimalAssignmentsAsync(sessionsToProcess, agentList);

        if (!assignments.Any())
        {
            _logger.LogWarning("No valid assignments could be created from available agents");
            return;
        }

        // Execute assignments with proper error handling
        var assignmentTasks = assignments.Select(async assignment =>
        {
            var (session, agent) = assignment;
            var success = await TryAssignSessionAsync(session, agent);
            
            // If assignment failed, release the reservation
            if (!success)
            {
                agent.ReleaseReservation();
            }
            
            return success;
        });

        var results = await Task.WhenAll(assignmentTasks);
        var successCount = results.Count(r => r);
        
        _logger.LogInformation("Batch processing completed: {SuccessCount}/{TotalCount} assignments successful",
            successCount, assignments.Count);
    }

    private bool TryDirectAssignment(Agent agent)
    {
        try
        {
            agent.AssignChat();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void InvalidateCapacityCache(TeamType teamType)
    {
        var cacheKey = $"{CAPACITY_CACHE_PREFIX}_{teamType}";
        _memoryCache.Remove(cacheKey);
        _memoryCache.Remove("total_capacity");
    }
}