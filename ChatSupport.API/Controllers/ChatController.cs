using Microsoft.AspNetCore.Mvc;
using ChatSupport.Application.Services;
using ChatSupport.API.Models;
using ChatSupport.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChatSupport.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatAssignmentService _chatAssignmentService;
    private readonly IChatSessionRepository _sessionRepository;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IChatAssignmentService chatAssignmentService, IChatSessionRepository sessionRepository, ILogger<ChatController> logger)
    {
        _chatAssignmentService = chatAssignmentService;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    [HttpPost("create")]
    public async Task<ActionResult<CreateChatResponse>> CreateChatSession([FromBody] CreateChatRequest request)
    {
        if (request == null)
        {
            _logger.LogWarning("Create chat request is null");
            return BadRequest(new { Message = "Request body is required" });
        }

        if (request.UserId == Guid.Empty)
        {
            _logger.LogWarning("Create chat request has invalid user ID");
            return BadRequest(new { Message = "Valid user ID is required" });
        }

        try
        {
            _logger.LogInformation("Creating chat session for user {UserId}", request.UserId);

            // prevent duplicate sessions
            var existingSession = await _sessionRepository.GetActiveSessionByUserIdAsync(request.UserId.ToString());
            if (existingSession != null)
            {
                _logger.LogWarning("User {UserId} attempted to create duplicate session. Existing session: {SessionId}",
                    request.UserId, existingSession.Id);
                
                var existingResponse = new CreateChatResponse
                {
                    SessionId = existingSession.Id,
                    Status = existingSession.Status.ToString(),
                    Message = existingSession.Status == Domain.Enums.ChatSessionStatus.Queued
                        ? "You already have an active session in the queue. Please wait for an agent."
                        : "You already have an active session with an agent.",
                    IsAccepted = true
                };
                
                return Ok(existingResponse);
            }

            var session = await _chatAssignmentService.CreateChatSessionAsync(request.UserId.ToString());

            var response = new CreateChatResponse
            {
                SessionId = session.Id,
                Status = session.Status.ToString(),
                Message = session.Status == Domain.Enums.ChatSessionStatus.Refused
                    ? "Chat request refused - all agents are busy"
                    : "Chat session created successfully",
                IsAccepted = session.Status != Domain.Enums.ChatSessionStatus.Refused
            };

            _logger.LogInformation("Chat session {SessionId} created with status {Status} for user {UserId}",
                session.Id, session.Status, request.UserId);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating chat session for user {UserId}", request.UserId);
            return StatusCode(500, new { Message = "Internal server error" });
        }
    }

    [HttpPost("{sessionId}/poll")]
    public async Task<ActionResult<PollResponse>> PollSession(Guid sessionId)
    {
        try
        {
            _logger.LogDebug("Polling session {SessionId}", sessionId);

            var success = await _chatAssignmentService.PollSessionAsync(sessionId);

            var response = new PollResponse
            {
                SessionId = sessionId,
                Success = success,
                Message = success ? "Poll successful" : "Session not found",
                Timestamp = DateTime.UtcNow
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling session {SessionId}", sessionId);
            return StatusCode(500, new { Message = "Internal server error" });
        }
    }

    [HttpGet("health")]
    public async Task<ActionResult<HealthResponse>> GetHealth()
    {
        try
        {
            var canAcceptChats = await _chatAssignmentService.CanAcceptNewChatAsync();
            
            var response = new HealthResponse
            {
                IsHealthy = true,
                CanAcceptNewChats = canAcceptChats,
                Timestamp = DateTime.UtcNow
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking system health");
            return StatusCode(500, new HealthResponse 
            { 
                IsHealthy = false, 
                CanAcceptNewChats = false,
                Timestamp = DateTime.UtcNow,
                Message = "System experiencing issues"
            });
        }
    }

    // Admin endpoints for viewing system state
    [HttpGet("admin/sessions")]
    public async Task<ActionResult> GetAllSessions()
    {
        try
        {
            var activeSessions = await _sessionRepository.GetActiveSessionsAsync();
            var queuedSessions = await _sessionRepository.GetQueuedSessionsAsync();
            var inactiveSessions = await _sessionRepository.GetInactiveSessionsAsync();
            
            var allSessions = activeSessions.Concat(queuedSessions).Concat(inactiveSessions);
            
            return Ok(allSessions.Select(s => new {
                s.Id,
                s.UserId,
                s.Status,
                s.CreatedAt,
                s.AssignedAgentId,
                s.LastPolledAt,
                s.IsInOverflow,
                s.PollCount,
                s.MissedPollCount
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all sessions");
            return StatusCode(500, new { Message = "Error retrieving sessions" });
        }
    }

    [HttpGet("admin/queue-status")]
    public async Task<ActionResult> GetQueueStatus()
    {
        try
        {
            var queueLength = await _sessionRepository.GetQueueLengthAsync();
            var overflowLength = await _sessionRepository.GetOverflowQueueLengthAsync();
            var queuedSessions = await _sessionRepository.GetQueuedSessionsAsync();
            var overflowSessions = await _sessionRepository.GetOverflowSessionsAsync();
            
            return Ok(new {
                MainQueueLength = queueLength,
                OverflowQueueLength = overflowLength,
                TotalQueued = queueLength + overflowLength,
                QueuedSessions = queuedSessions.Select(s => new {
                    s.Id,
                    s.UserId,
                    s.CreatedAt,
                    s.IsInOverflow,
                    WaitTime = DateTime.UtcNow - s.CreatedAt
                }),
                OverflowSessions = overflowSessions.Select(s => new {
                    s.Id,
                    s.UserId,
                    s.CreatedAt,
                    WaitTime = DateTime.UtcNow - s.CreatedAt
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving queue status");
            return StatusCode(500, new { Message = "Error retrieving queue status" });
        }
    }

    [HttpGet("admin/sessions/active")]
    public async Task<ActionResult> GetActiveSessions()
    {
        try
        {
            var activeSessions = await _sessionRepository.GetActiveSessionsAsync();
            return Ok(activeSessions.Select(s => new {
                s.Id,
                s.UserId,
                s.Status,
                s.CreatedAt,
                s.AssignedAgentId,
                s.LastPolledAt,
                Duration = DateTime.UtcNow - s.CreatedAt
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active sessions");
            return StatusCode(500, new { Message = "Error retrieving active sessions" });
        }
    }

    [HttpGet("admin/sessions/inactive")]
    public async Task<ActionResult> GetInactiveSessions()
    {
        try
        {
            var inactiveSessions = await _sessionRepository.GetInactiveSessionsAsync();
            return Ok(inactiveSessions.Select(s => new {
                s.Id,
                s.UserId,
                s.Status,
                s.CreatedAt,
                s.AssignedAgentId,
                s.LastPolledAt,
                TimeSinceLastPoll = DateTime.UtcNow - s.LastPolledAt,
                s.MissedPollCount
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving inactive sessions");
            return StatusCode(500, new { Message = "Error retrieving inactive sessions" });
        }
    }
}