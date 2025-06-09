using Microsoft.Extensions.Logging;
using ChatSupport.Domain.Enums;
using ChatSupport.Domain.Interfaces;
using ChatSupport.Domain.Models;
using ChatSupport.Domain.Services;

namespace ChatSupport.Application.Services;

public class AgentSelectionService : IAgentSelectionService
{
    private readonly IAgentRepository _agentRepository;
    private readonly IRoundRobinCoordinator _roundRobinCoordinator;
    private readonly ILogger<AgentSelectionService> _logger;

    public AgentSelectionService(
        IAgentRepository agentRepository,
        IRoundRobinCoordinator roundRobinCoordinator,
        ILogger<AgentSelectionService> logger)
    {
        _agentRepository = agentRepository;
        _roundRobinCoordinator = roundRobinCoordinator;
        _logger = logger;
    }

    public async Task<Agent?> SelectNextAvailableAgentAsync(TeamType teamType, bool useOverflow = false)
    {
        if (useOverflow)
        {
            return await SelectFromOverflowTeamAsync();
        }

        var teams = new[] { TeamType.TeamA, TeamType.TeamB, TeamType.TeamC };
        var teamKey = _roundRobinCoordinator.BuildTeamKey(TeamType.TeamA);
        var teamIndex = _roundRobinCoordinator.GetNextIndex(teamKey, teams.Length);
        var selectedTeam = teams[teamIndex];

        return await SelectAgentFromTeamAsync(selectedTeam);
    }

    public Task<List<(ChatSession session, Agent agent)>> CreateOptimalAssignmentsAsync(
        IEnumerable<ChatSession> sessions, IEnumerable<Agent> availableAgents)
    {
        var assignments = new List<(ChatSession, Agent)>();
        var sessionList = sessions.ToList();
        var agentList = availableAgents.Where(a => a.CanAcceptNewChat()).ToList();

        if (!sessionList.Any() || !agentList.Any())
        {
            return Task.FromResult(assignments);
        }

        // Group agents by team for proper distribution
        var agentsByTeam = agentList
            .GroupBy(a => a.TeamType)
            .ToDictionary(g => g.Key, g => g.ToList());

        var teams = new[] { TeamType.TeamA, TeamType.TeamB, TeamType.TeamC };
        var teamIndex = 0;

        foreach (var session in sessionList)
        {
            Agent? selectedAgent = null;

            // Round-robin across teams
            for (int i = 0; i < teams.Length && selectedAgent == null; i++)
            {
                var currentTeamIndex = (teamIndex + i) % teams.Length;
                var team = teams[currentTeamIndex];

                if (agentsByTeam.TryGetValue(team, out var teamAgents))
                {
                    selectedAgent = SelectOptimalAgentFromTeam(teamAgents, team);
                    if (selectedAgent != null)
                    {
                        teamIndex = (currentTeamIndex + 1) % teams.Length;
                    }
                }
            }

            if (selectedAgent != null && selectedAgent.TryReserveCapacity())
            {
                assignments.Add((session, selectedAgent));
            }
        }

        return Task.FromResult(assignments);
    }

    private async Task<Agent?> SelectFromOverflowTeamAsync()
    {
        var overflowAgents = await _agentRepository.GetAgentsByTeamAsync(TeamType.Overflow);
        var availableAgents = overflowAgents.Where(a => a.CanAcceptNewChat()).ToList();

        if (!availableAgents.Any())
        {
            return null;
        }

        var key = _roundRobinCoordinator.BuildTeamKey(TeamType.Overflow);
        var index = _roundRobinCoordinator.GetNextIndex(key, availableAgents.Count);
        return availableAgents[index];
    }

    private async Task<Agent?> SelectAgentFromTeamAsync(TeamType teamType)
    {
        var agents = await _agentRepository.GetAgentsByTeamAsync(teamType);
        var availableAgents = agents.Where(a => a.CanAcceptNewChat()).ToList();

        if (!availableAgents.Any())
        {
            return null;
        }

        return SelectOptimalAgentFromTeam(availableAgents, teamType);
    }

    private Agent? SelectOptimalAgentFromTeam(List<Agent> teamAgents, TeamType teamType)
    {
        // Implement proper junior-first with capacity-based distribution
        var seniorityLevels = new[] { Seniority.Junior, Seniority.MidLevel, Seniority.Senior, Seniority.TeamLead };

        foreach (var seniority in seniorityLevels)
        {
            var agentsOfSeniority = teamAgents
                .Where(a => a.Seniority == seniority && a.HasAvailableCapacity())
                .ToList();

            if (agentsOfSeniority.Any())
            {
                return SelectAgentWithinSeniorityGroup(agentsOfSeniority, teamType, seniority);
            }
        }

        return null;
    }

    private Agent SelectAgentWithinSeniorityGroup(List<Agent> agents, TeamType teamType, Seniority seniority)
    {
        if (agents.Count == 1)
        {
            return agents[0];
        }

        // For multiple agents of same seniority, use capacity-weighted selection
        // This ensures proper proportional distribution as per business requirements
        var agentsWithCapacity = agents
            .Select(a => new { Agent = a, AvailableCapacity = a.AvailableCapacity })
            .Where(x => x.AvailableCapacity > 0)
            .OrderByDescending(x => x.AvailableCapacity)
            .ToList();

        if (!agentsWithCapacity.Any())
        {
            return agents[0]; // Fallback to first agent
        }

        // Use round-robin among agents with highest available capacity
        var maxCapacity = agentsWithCapacity.First().AvailableCapacity;
        var topCapacityAgents = agentsWithCapacity
            .Where(x => x.AvailableCapacity == maxCapacity)
            .Select(x => x.Agent)
            .ToList();

        var key = _roundRobinCoordinator.BuildSeniorityKey(teamType, seniority);
        var index = _roundRobinCoordinator.GetNextIndex(key, topCapacityAgents.Count);
        
        var selectedAgent = topCapacityAgents[index];
        
        _logger.LogDebug("Selected agent {AgentId} ({Seniority}) from team {Team} with {AvailableCapacity} available capacity",
            selectedAgent.Id, seniority, teamType, selectedAgent.AvailableCapacity);

        return selectedAgent;
    }
}