using ChatSupport.Domain.Models;
using ChatSupport.Domain.Enums;

namespace ChatSupport.Domain.Services;

/// <summary>
/// Handles agent selection for chat assignments.
/// </summary>
public interface IAgentSelectionService
{
    /// <summary>
    /// Gets next available agent from team using junior-first logic.
    /// </summary>
    Task<Agent?> SelectNextAvailableAgentAsync(TeamType teamType, bool useOverflow = false);
    
    /// <summary>
    /// Creates batch assignments for sessions and agents.
    /// </summary>
    Task<List<(ChatSession session, Agent agent)>> CreateOptimalAssignmentsAsync(
        IEnumerable<ChatSession> sessions, IEnumerable<Agent> availableAgents);
}