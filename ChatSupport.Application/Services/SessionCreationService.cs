using ChatSupport.Domain.Models;
using ChatSupport.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChatSupport.Application.Services;

public interface ISessionCreationService
{
    Task<ChatSession> CreateSessionAsync(string userId);
}

public class SessionCreationService : ISessionCreationService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly ICapacityCalculationService _capacityCalculationService;
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<SessionCreationService> _logger;

    public SessionCreationService(
        IChatSessionRepository chatSessionRepository,
        ICapacityCalculationService capacityCalculationService,
        ITimeProvider timeProvider,
        ILogger<SessionCreationService> logger)
    {
        _chatSessionRepository = chatSessionRepository;
        _capacityCalculationService = capacityCalculationService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ChatSession> CreateSessionAsync(string userId)
    {
        _logger.LogInformation("Creating chat session for user {UserId}", userId);

        // Check if user already has an active session
        var existingSession = await _chatSessionRepository.GetActiveSessionByUserIdAsync(userId);
        if (existingSession != null)
        {
            _logger.LogWarning("User {UserId} already has an active session {SessionId}", 
                userId, existingSession.Id);
            return existingSession;
        }

        if (!await _capacityCalculationService.CanAcceptNewSessionAsync())
        {
            _logger.LogWarning("Chat request refused - queue is full for user {UserId}", userId);
            var refusedSession = new ChatSession(userId, _timeProvider);
            refusedSession.Refuse();
            return await _chatSessionRepository.CreateAsync(refusedSession);
        }

        var session = new ChatSession(userId, _timeProvider);
        var createdSession = await _chatSessionRepository.CreateAsync(session);
        
        _logger.LogInformation("Chat session {SessionId} created and queued for user {UserId}",
            createdSession.Id, userId);

        return createdSession;
    }
}