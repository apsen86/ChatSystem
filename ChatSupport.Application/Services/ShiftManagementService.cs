using ChatSupport.Domain.Interfaces;
using ChatSupport.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ChatSupport.Application.Services;

public interface IShiftManagementService
{
    Task RefreshAgentShiftStatusAsync();
    Task UpdateShiftsForCurrentTimeAsync(DateTime currentTime);
    Task<bool> IsAgentInActiveShiftAsync(Guid agentId);
    Task<TeamType?> GetActiveShiftTeamAsync();
}

public class ShiftManagementService : IShiftManagementService
{
    private readonly IAgentRepository _agentRepository;
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<ShiftManagementService> _logger;

    public ShiftManagementService(
        IAgentRepository agentRepository,
        ITimeProvider timeProvider,
        ILogger<ShiftManagementService> logger)
    {
        _agentRepository = agentRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // Updates agent availability based on current shifts
    public async Task RefreshAgentShiftStatusAsync()
    {
        var currentTime = _timeProvider.UtcNow;
        var allAgents = await _agentRepository.GetActiveAgentsAsync();
        
        var updatedAgents = new List<Domain.Models.Agent>();
        
        foreach (var agent in allAgents)
        {
            var oldStatus = agent.AcceptingNewChats;
            agent.UpdateShiftStatus();
            
            if (oldStatus != agent.AcceptingNewChats)
            {
                updatedAgents.Add(agent);
                _logger.LogInformation("Agent {AgentId} shift status changed - AcceptingNewChats: {Status}",
                    agent.Id, agent.AcceptingNewChats);
            }
        }

        if (updatedAgents.Any())
        {
            await _agentRepository.UpdateManyAsync(updatedAgents);
            _logger.LogInformation("Updated shift status for {Count} agents", updatedAgents.Count);
        }
    }

    public async Task UpdateShiftsForCurrentTimeAsync(DateTime currentTime)
    {
        // Calculate current shift boundaries for 24/7 coverage
        // Team A: 00:00 - 08:00 UTC
        // Team B: 08:00 - 16:00 UTC  
        // Team C: 16:00 - 24:00 UTC
        
        var currentDate = currentTime.Date;
        
        // Determine shift boundaries based on current time
        var teamAStart = currentTime.Hour < 8 ? currentDate : currentDate.AddDays(1);
        var teamBStart = currentTime.Hour < 16 ? currentDate : currentDate.AddDays(1);
        var teamCStart = currentTime.Hour < 24 ? currentDate : currentDate.AddDays(1);
        
        // Update each team's shift
        await SetTeamShiftAsync(TeamType.TeamA, teamAStart, teamAStart.AddHours(8));
        await SetTeamShiftAsync(TeamType.TeamB, teamBStart.AddHours(8), teamBStart.AddHours(16));
        await SetTeamShiftAsync(TeamType.TeamC, teamCStart.AddHours(16), teamCStart.AddDays(1));
        
        // Overflow during office hours: 09:00 - 17:00 EST
        try
        {
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var estNow = TimeZoneInfo.ConvertTimeFromUtc(currentTime, easternZone);
            var estDate = estNow.Date;
            
            var officeStart = TimeZoneInfo.ConvertTimeToUtc(estDate.AddHours(9), easternZone);
            var officeEnd = TimeZoneInfo.ConvertTimeToUtc(estDate.AddHours(17), easternZone);
            
            await SetTeamShiftAsync(TeamType.Overflow, officeStart, officeEnd);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback to UTC approximation
            await SetTeamShiftAsync(TeamType.Overflow, currentDate.AddHours(14), currentDate.AddHours(22));
        }
        
        _logger.LogDebug("Updated shifts for current time: {CurrentTime}", currentTime);
    }

    private async Task SetTeamShiftAsync(TeamType team, DateTime shiftStart, DateTime shiftEnd)
    {
        var agents = await _agentRepository.GetAgentsByTeamAsync(team);
        var updatedAgents = new List<Domain.Models.Agent>();
        
        foreach (var agent in agents)
        {
            agent.SetShift(shiftStart, shiftEnd);
            updatedAgents.Add(agent);
        }
        
        if (updatedAgents.Any())
        {
            await _agentRepository.UpdateManyAsync(updatedAgents);
        }
    }

    public async Task<bool> IsAgentInActiveShiftAsync(Guid agentId)
    {
        var agent = await _agentRepository.GetByIdAsync(agentId);
        return agent?.IsActive ?? false;
    }

    public Task<TeamType?> GetActiveShiftTeamAsync()
    {
        var currentTime = _timeProvider.UtcNow;
        var currentHour = currentTime.Hour;
        
        TeamType? result = currentHour switch
        {
            >= 0 and < 8 => TeamType.TeamA,
            >= 8 and < 16 => TeamType.TeamB,
            >= 16 and < 24 => TeamType.TeamC,
            _ => (TeamType?)null
        };
        
        return Task.FromResult(result);
    }
}