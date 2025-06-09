using ChatSupport.Domain.Services;
using ChatSupport.Domain.Enums;
using System.Collections.Concurrent;

namespace ChatSupport.Infrastructure.Services;

public class RoundRobinCoordinator : IRoundRobinCoordinator
{
    private readonly ConcurrentDictionary<string, int> _indices = new();

    public int GetNextIndex(string key, int maxCount)
    {
        if (maxCount <= 0)
            throw new ArgumentException("Max count must be positive", nameof(maxCount));

        return _indices.AddOrUpdate(key, 0, (_, currentIndex) => (currentIndex + 1) % maxCount);
    }

    public void ResetIndex(string key)
    {
        _indices.TryRemove(key, out _);
    }

    public string BuildTeamKey(TeamType teamType)
    {
        return $"team_{teamType}";
    }

    public string BuildSeniorityKey(TeamType teamType, Seniority seniority)
    {
        return $"team_{teamType}_seniority_{seniority}";
    }
}