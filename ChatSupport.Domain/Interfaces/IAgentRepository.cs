using ChatSupport.Domain.Models;
using ChatSupport.Domain.Enums;

namespace ChatSupport.Domain.Interfaces;

/// <summary>
/// Repository interface for agent data access operations
/// </summary>
public interface IAgentRepository
{
    Task<IEnumerable<Agent>> GetActiveAgentsAsync();
    Task<IEnumerable<Agent>> GetAgentsByTeamAsync(TeamType teamType);
    Task<Agent?> GetByIdAsync(Guid agentId);
    Task<IEnumerable<Agent>> GetAvailableAgentsAsync();
    Task UpdateAsync(Agent agent);
    Task UpdateManyAsync(IEnumerable<Agent> agents);
    Task<Agent?> GetNextAvailableAgentAsync(TeamType teamType);
    Task<int> GetTeamCapacityAsync(TeamType teamType);
}