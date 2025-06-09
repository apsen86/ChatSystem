using ChatSupport.Domain.Models;
using ChatSupport.Domain.Enums;

namespace ChatSupport.Domain.Interfaces;

public interface IChatSessionRepository
{
    Task<ChatSession> CreateAsync(ChatSession session);
    Task<ChatSession?> GetByIdAsync(Guid sessionId);
    Task<IEnumerable<ChatSession>> GetQueuedSessionsAsync();
    Task<IEnumerable<ChatSession>> GetOverflowSessionsAsync();
    Task<IEnumerable<ChatSession>> GetSessionsByAgentAsync(Guid agentId);
    Task<IEnumerable<ChatSession>> GetActiveSessionsAsync();
    Task<IEnumerable<ChatSession>> GetInactiveSessionsAsync();
    Task UpdateAsync(ChatSession session);
    Task UpdateManyAsync(IEnumerable<ChatSession> sessions);
    Task<int> GetQueueLengthAsync();
    Task<int> GetOverflowQueueLengthAsync();
    Task<ChatSession?> DequeueNextSessionAsync();
    Task<ChatSession?> DequeueNextOverflowSessionAsync();
    Task<IEnumerable<ChatSession>> GetTimedOutSessionsAsync();
    Task<IEnumerable<ChatSession>> GetActiveSessionsForMonitoringAsync();
    Task<ChatSession?> GetActiveSessionByUserIdAsync(string userId);
}