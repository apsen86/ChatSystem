using ChatSupport.Domain.Enums;

namespace ChatSupport.Domain.Services;

/// <summary>
/// Thread-safe round-robin coordination for chat assignment distribution.
/// </summary>
public interface IRoundRobinCoordinator
{
    /// <summary>
    /// Gets next index in round-robin sequence.
    /// </summary>
    int GetNextIndex(string key, int maxCount);
    
    /// <summary>
    /// Resets counter for the given key.
    /// </summary>
    void ResetIndex(string key);
    
    /// <summary>
    /// Builds team key for round-robin tracking.
    /// </summary>
    string BuildTeamKey(TeamType teamType);
    
    /// <summary>
    /// Builds team + seniority key for round-robin tracking.
    /// </summary>
    string BuildSeniorityKey(TeamType teamType, Seniority seniority);
}