using ChatSupport.Domain.Models;

namespace ChatSupport.Domain.Interfaces;

public interface IAgentAssignmentStrategy
{
    Task<Agent?> GetBestAvailableAgentAsync();
    Task<bool> AssignChatToAgentAsync(ChatSession session, Agent agent);
    Task ProcessQueueAsync();
    Task CompleteChatAsync(Guid sessionId);
}