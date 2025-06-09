using ChatSupport.Domain.Models;
using ChatSupport.Domain.Interfaces;
using ChatSupport.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ChatSupport.Infrastructure.Repositories;

public class AgentRepository : IAgentRepository
{
    private readonly ConcurrentDictionary<Guid, Agent> _agents;
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<AgentRepository> _logger;
    private readonly object _shiftUpdateLock = new object();

    public AgentRepository(ITimeProvider timeProvider, ILogger<AgentRepository> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _agents = new ConcurrentDictionary<Guid, Agent>();
        InitializeTeams();
    }

    public Task<IEnumerable<Agent>> GetActiveAgentsAsync()
    {
        var activeAgents = _agents.Values.Where(a => a.IsActive).ToList();
        return Task.FromResult<IEnumerable<Agent>>(activeAgents);
    }

    public Task<IEnumerable<Agent>> GetAgentsByTeamAsync(TeamType teamType)
    {
        var teamAgents = _agents.Values.Where(a => a.TeamType == teamType).ToList();
        return Task.FromResult<IEnumerable<Agent>>(teamAgents);
    }

    public Task<Agent?> GetByIdAsync(Guid agentId)
    {
        _agents.TryGetValue(agentId, out var agent);
        return Task.FromResult(agent);
    }

    public Task<IEnumerable<Agent>> GetAvailableAgentsAsync()
    {
        var availableAgents = _agents.Values.Where(a => a.CanAcceptNewChat()).ToList();
        return Task.FromResult<IEnumerable<Agent>>(availableAgents);
    }

    public Task UpdateAsync(Agent agent)
    {
        _agents.AddOrUpdate(agent.Id, agent, (key, existingAgent) => agent);
        return Task.CompletedTask;
    }

    public Task UpdateManyAsync(IEnumerable<Agent> agents)
    {
        foreach (var agent in agents)
        {
            _agents.AddOrUpdate(agent.Id, agent, (key, existingAgent) => agent);
        }
        return Task.CompletedTask;
    }

    public Task<Agent?> GetNextAvailableAgentAsync(TeamType teamType)
    {
        var availableAgents = _agents.Values
            .Where(a => a.TeamType == teamType && a.CanAcceptNewChat())
            .ToList();

        if (!availableAgents.Any())
            return Task.FromResult<Agent?>(null);

        // Return first available agent - round-robin logic is now handled by AgentSelectionService
        return Task.FromResult<Agent?>(availableAgents[0]);
    }

    public Task<int> GetTeamCapacityAsync(TeamType teamType)
    {
        var teamAgents = _agents.Values.Where(a => a.TeamType == teamType && a.IsActive);
        var capacity = teamAgents.Sum(a => a.MaxConcurrentChats);
        return Task.FromResult(capacity);
    }


    private void InitializeTeams()
    {
        var currentTime = _timeProvider.UtcNow;
        
        // Team A: 1x team lead, 2x mid-level, 1x junior (00:00-08:00 UTC)
        var teamALead = new Agent(Guid.NewGuid(), "Alice Thompson", Seniority.TeamLead, TeamType.TeamA, _timeProvider);
        var teamAMid1 = new Agent(Guid.NewGuid(), "Bob Wilson", Seniority.MidLevel, TeamType.TeamA, _timeProvider);
        var teamAMid2 = new Agent(Guid.NewGuid(), "Carol Davis", Seniority.MidLevel, TeamType.TeamA, _timeProvider);
        var teamAJunior = new Agent(Guid.NewGuid(), "David Brown", Seniority.Junior, TeamType.TeamA, _timeProvider);

        // Team B: 1x senior, 1x mid-level, 2x junior (08:00-16:00 UTC)
        var teamBSenior = new Agent(Guid.NewGuid(), "Emma Johnson", Seniority.Senior, TeamType.TeamB, _timeProvider);
        var teamBMid = new Agent(Guid.NewGuid(), "Frank Miller", Seniority.MidLevel, TeamType.TeamB, _timeProvider);
        var teamBJunior1 = new Agent(Guid.NewGuid(), "Grace Lee", Seniority.Junior, TeamType.TeamB, _timeProvider);
        var teamBJunior2 = new Agent(Guid.NewGuid(), "Henry Chen", Seniority.Junior, TeamType.TeamB, _timeProvider);

        // Team C: 2x mid-level (16:00-24:00 UTC)
        var teamCMid1 = new Agent(Guid.NewGuid(), "Isabel Rodriguez", Seniority.MidLevel, TeamType.TeamC, _timeProvider);
        var teamCMid2 = new Agent(Guid.NewGuid(), "Jack Anderson", Seniority.MidLevel, TeamType.TeamC, _timeProvider);

        // Overflow team: 6x junior (office hours only)
        var overflowAgents = new List<Agent>();
        for (int i = 1; i <= 6; i++)
        {
            var agent = new Agent(Guid.NewGuid(), $"Overflow Agent {i}", Seniority.Junior, TeamType.Overflow, _timeProvider);
            overflowAgents.Add(agent);
        }

        // Add all agents to the repository
        var allAgents = new List<Agent>
        {
            teamALead, teamAMid1, teamAMid2, teamAJunior,
            teamBSenior, teamBMid, teamBJunior1, teamBJunior2,
            teamCMid1, teamCMid2
        };
        allAgents.AddRange(overflowAgents);

        foreach (var agent in allAgents)
        {
            _agents.TryAdd(agent.Id, agent);
        }

        // Initialize proper 8-hour rotating shifts for 24/7 coverage
        InitializeShifts(currentTime);

        _logger.LogInformation("Initialized {AgentCount} agents across {TeamCount} teams with 24/7 shift coverage",
            allAgents.Count, Enum.GetValues<TeamType>().Length);

        LogTeamCapacities();
    }

    private void InitializeShifts(DateTime currentTime)
    {
        lock (_shiftUpdateLock)
        {
            // Initialize basic shifts for each team
            InitializeBasicShifts(currentTime);
        }
    }

    private void InitializeBasicShifts(DateTime currentTime)
    {
        var currentDate = currentTime.Date;
        
        // Proper shift initialization with overlap to prevent coverage gaps
        // Team A: 00:00-08:05, Team B: 07:55-16:05, Team C: 15:55-24:05
        SetTeamShiftInternal(TeamType.TeamA, currentDate, currentDate.AddHours(8).AddMinutes(5));
        SetTeamShiftInternal(TeamType.TeamB, currentDate.AddHours(7).AddMinutes(55), currentDate.AddHours(16).AddMinutes(5));
        SetTeamShiftInternal(TeamType.TeamC, currentDate.AddHours(15).AddMinutes(55), currentDate.AddDays(1).AddMinutes(5));
        
        // Overflow team covers peak hours with proper coverage
        SetTeamShiftInternal(TeamType.Overflow, currentDate.AddHours(9), currentDate.AddHours(17));
    }

    private void SetTeamShiftInternal(TeamType team, DateTime shiftStart, DateTime shiftEnd)
    {
        var agents = _agents.Values.Where(a => a.TeamType == team);
        foreach (var agent in agents)
        {
            agent.SetShift(shiftStart, shiftEnd);
        }
    }


    private void LogTeamCapacities()
    {
        foreach (TeamType team in Enum.GetValues<TeamType>())
        {
            var agents = _agents.Values.Where(a => a.TeamType == team).ToList();
            var capacity = agents.Sum(a => a.MaxConcurrentChats);
            var maxQueue = (int)Math.Floor(capacity * 1.5);
            
            _logger.LogInformation(
                "Team {Team}: {AgentCount} agents, Capacity: {Capacity}, Max Queue: {MaxQueue}",
                team, agents.Count, capacity, maxQueue);
        }
    }
}