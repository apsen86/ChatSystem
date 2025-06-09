using ChatSupport.Domain.Models;

namespace ChatSupport.Application.Services;

public interface IChatAssignmentService
{
    Task<ChatSession> CreateChatSessionAsync(string userId);
    Task<bool> PollSessionAsync(Guid sessionId);
    Task ProcessQueueAsync();
    Task MonitorSessionsAsync();
    Task<bool> CanAcceptNewChatAsync();
    Task<int> GetQueuePositionAsync(Guid sessionId);
    Task<TimeSpan?> GetEstimatedWaitTimeAsync(Guid sessionId);
}