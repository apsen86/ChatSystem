using ChatSupport.Domain.Models;

namespace ChatSupport.Domain.Interfaces;

public interface IQueueManager
{
    Task<int> GetQueuePositionAsync(Guid sessionId);
    Task<TimeSpan?> GetEstimatedWaitTimeAsync(Guid sessionId);
    Task<bool> CanAcceptNewChatAsync();
    Task<ChatSession?> GetNextSessionFromQueueAsync();
    Task<int> GetQueueLengthAsync();
    Task<int> GetOverflowQueueLengthAsync();
}