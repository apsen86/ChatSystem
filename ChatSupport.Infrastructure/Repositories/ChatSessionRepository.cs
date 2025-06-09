using ChatSupport.Domain.Models;
using ChatSupport.Domain.Interfaces;
using ChatSupport.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ChatSupport.Infrastructure.Repositories;

public class ChatSessionRepository : IChatSessionRepository
{
    private readonly ConcurrentDictionary<Guid, ChatSession> _sessions;
    private readonly ConcurrentQueue<ChatSession> _mainQueue;
    private readonly ConcurrentQueue<ChatSession> _overflowQueue;
    private readonly ILogger<ChatSessionRepository> _logger;
    private readonly object _lockObject = new object();

    public ChatSessionRepository(ILogger<ChatSessionRepository> logger)
    {
        _sessions = new ConcurrentDictionary<Guid, ChatSession>();
        _mainQueue = new ConcurrentQueue<ChatSession>();
        _overflowQueue = new ConcurrentQueue<ChatSession>();
        _logger = logger;
    }

    public Task<ChatSession> CreateAsync(ChatSession session)
    {
        _sessions.TryAdd(session.Id, session);
        
        if (session.Status == ChatSessionStatus.Queued)
        {
            if (session.IsInOverflow)
            {
                _overflowQueue.Enqueue(session);
                _logger.LogDebug("Session {SessionId} added to overflow queue", session.Id);
            }
            else
            {
                _mainQueue.Enqueue(session);
                _logger.LogDebug("Session {SessionId} added to main queue", session.Id);
            }
        }

        return Task.FromResult(session);
    }

    public Task<ChatSession?> GetByIdAsync(Guid sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task<IEnumerable<ChatSession>> GetQueuedSessionsAsync()
    {
        var queuedSessions = _sessions.Values
            .Where(s => s.Status == ChatSessionStatus.Queued)
            .OrderBy(s => s.CreatedAt)
            .ToList();

        return Task.FromResult<IEnumerable<ChatSession>>(queuedSessions);
    }

    public Task<IEnumerable<ChatSession>> GetOverflowSessionsAsync()
    {
        var overflowSessions = _sessions.Values
            .Where(s => s.Status == ChatSessionStatus.Queued && s.IsInOverflow)
            .OrderBy(s => s.CreatedAt)
            .ToList();

        return Task.FromResult<IEnumerable<ChatSession>>(overflowSessions);
    }

    public Task<IEnumerable<ChatSession>> GetSessionsByAgentAsync(Guid agentId)
    {
        var agentSessions = _sessions.Values
            .Where(s => s.AssignedAgentId == agentId)
            .ToList();

        return Task.FromResult<IEnumerable<ChatSession>>(agentSessions);
    }

    public Task<IEnumerable<ChatSession>> GetActiveSessionsAsync()
    {
        var activeSessions = _sessions.Values
            .Where(s => s.Status == ChatSessionStatus.Active || s.Status == ChatSessionStatus.Assigned)
            .ToList();

        return Task.FromResult<IEnumerable<ChatSession>>(activeSessions);
    }

    public Task<IEnumerable<ChatSession>> GetInactiveSessionsAsync()
    {
        var inactiveSessions = _sessions.Values
            .Where(s => s.Status == ChatSessionStatus.Inactive)
            .ToList();

        return Task.FromResult<IEnumerable<ChatSession>>(inactiveSessions);
    }

    public Task UpdateAsync(ChatSession session)
    {
        _sessions.TryUpdate(session.Id, session, _sessions[session.Id]);
        
        // Handle status transitions
        if (session.Status == ChatSessionStatus.Queued && session.IsInOverflow)
        {
            _overflowQueue.Enqueue(session);
        }
        
        return Task.CompletedTask;
    }

    public Task UpdateManyAsync(IEnumerable<ChatSession> sessions)
    {
        foreach (var session in sessions)
        {
            _sessions.TryUpdate(session.Id, session, _sessions[session.Id]);
            
            // Handle status transitions
            if (session.Status == ChatSessionStatus.Queued && session.IsInOverflow)
            {
                _overflowQueue.Enqueue(session);
            }
        }
        
        return Task.CompletedTask;
    }

    public Task<int> GetQueueLengthAsync()
    {
        var count = _sessions.Values.Count(s => s.Status == ChatSessionStatus.Queued && !s.IsInOverflow);
        return Task.FromResult(count);
    }

    public Task<int> GetOverflowQueueLengthAsync()
    {
        var count = _sessions.Values.Count(s => s.Status == ChatSessionStatus.Queued && s.IsInOverflow);
        return Task.FromResult(count);
    }

    public Task<ChatSession?> DequeueNextSessionAsync()
    {
        if (_mainQueue.TryDequeue(out var session))
        {
            return Task.FromResult<ChatSession?>(session);
        }
        return Task.FromResult<ChatSession?>(null);
    }

    public Task<ChatSession?> DequeueNextOverflowSessionAsync()
    {
        if (_overflowQueue.TryDequeue(out var session))
        {
            return Task.FromResult<ChatSession?>(session);
        }
        return Task.FromResult<ChatSession?>(null);
    }

    public Task<IEnumerable<ChatSession>> GetTimedOutSessionsAsync()
    {
        var timedOutSessions = _sessions.Values
            .Where(s => (s.Status == ChatSessionStatus.Active || s.Status == ChatSessionStatus.Assigned)
                       && s.IsTimedOut())
            .ToList();

        return Task.FromResult<IEnumerable<ChatSession>>(timedOutSessions);
    }

    public Task<IEnumerable<ChatSession>> GetActiveSessionsForMonitoringAsync()
    {
        var activeSessions = _sessions.Values
            .Where(s => s.Status == ChatSessionStatus.Active ||
                       s.Status == ChatSessionStatus.Assigned ||
                       s.Status == ChatSessionStatus.Queued)
            .ToList();

        return Task.FromResult<IEnumerable<ChatSession>>(activeSessions);
    }

    public Task<ChatSession?> GetActiveSessionByUserIdAsync(string userId)
    {
        var activeSession = _sessions.Values
            .FirstOrDefault(s => s.UserId == userId &&
                                (s.Status == ChatSessionStatus.Active ||
                                 s.Status == ChatSessionStatus.Assigned ||
                                 s.Status == ChatSessionStatus.Queued));

        return Task.FromResult(activeSession);
    }
}