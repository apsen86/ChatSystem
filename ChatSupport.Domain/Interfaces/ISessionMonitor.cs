using ChatSupport.Domain.Models;

namespace ChatSupport.Domain.Interfaces;

public interface ISessionMonitor
{
    Task<bool> PollSessionAsync(Guid sessionId);
    Task<IEnumerable<ChatSession>> GetTimedOutSessionsAsync();
    Task CleanupTimedOutSessionsAsync();
    Task<bool> IsSessionActiveAsync(Guid sessionId);
}