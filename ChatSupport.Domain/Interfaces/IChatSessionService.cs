using ChatSupport.Domain.Models;

namespace ChatSupport.Domain.Interfaces;

public interface IChatSessionService
{
    Task<ChatSession> CreateChatSessionAsync(string userId);
    Task<ChatSession?> GetSessionAsync(Guid sessionId);
    Task<bool> EndSessionAsync(Guid sessionId);
    Task<IEnumerable<ChatSession>> GetUserSessionsAsync(string userId);
}