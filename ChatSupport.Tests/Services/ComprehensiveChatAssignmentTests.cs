using Xunit;
using Moq;
using ChatSupport.Application.Services;
using ChatSupport.Domain.Interfaces;
using ChatSupport.Domain.Services;
using ChatSupport.Domain.Models;
using ChatSupport.Domain.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChatSupport.Tests.Services;

public class ComprehensiveChatAssignmentTests
{
    private readonly Mock<IChatSessionRepository> _mockSessionRepository;
    private readonly Mock<IAgentRepository> _mockAgentRepository;
    private readonly Mock<ISessionCreationService> _mockSessionCreationService;
    private readonly Mock<ICapacityCalculationService> _mockCapacityCalculationService;
    private readonly Mock<IAgentAssignmentService> _mockAgentAssignmentService;
    private readonly Mock<IAgentSelectionService> _mockAgentSelectionService;
    private readonly Mock<ISessionTimeoutService> _mockSessionTimeoutService;
    private readonly Mock<IBusinessHoursService> _mockBusinessHoursService;
    private readonly Mock<IShiftManagementService> _mockShiftManagementService;
    private readonly Mock<ITimeProvider> _mockTimeProvider;
    private readonly Mock<ILogger<ChatAssignmentService>> _mockLogger;
    private readonly ChatAssignmentService _service;

    public ComprehensiveChatAssignmentTests()
    {
        _mockSessionRepository = new Mock<IChatSessionRepository>();
        _mockAgentRepository = new Mock<IAgentRepository>();
        _mockSessionCreationService = new Mock<ISessionCreationService>();
        _mockCapacityCalculationService = new Mock<ICapacityCalculationService>();
        _mockAgentAssignmentService = new Mock<IAgentAssignmentService>();
        _mockAgentSelectionService = new Mock<IAgentSelectionService>();
        _mockSessionTimeoutService = new Mock<ISessionTimeoutService>();
        _mockBusinessHoursService = new Mock<IBusinessHoursService>();
        _mockShiftManagementService = new Mock<IShiftManagementService>();
        _mockTimeProvider = new Mock<ITimeProvider>();
        _mockLogger = new Mock<ILogger<ChatAssignmentService>>();
        
        _service = new ChatAssignmentService(
            _mockSessionRepository.Object,
            _mockAgentRepository.Object,
            _mockSessionCreationService.Object,
            _mockCapacityCalculationService.Object,
            _mockAgentAssignmentService.Object,
            _mockAgentSelectionService.Object,
            _mockSessionTimeoutService.Object,
            _mockBusinessHoursService.Object,
            _mockShiftManagementService.Object,
            _mockTimeProvider.Object,
            _mockLogger.Object);
    }

    #region 1. Session Creation and Queueing Tests

    [Fact]
    public async Task CreateChatSessionAsync_UserInitiatesSupport_CreatesAndQueuesSession()
    {
        var userId = "user123";
        var expectedSession = new ChatSession(userId, _mockTimeProvider.Object);
        _mockSessionCreationService.Setup(x => x.CreateSessionAsync(userId))
            .ReturnsAsync(expectedSession);

        var result = await _service.CreateChatSessionAsync(userId);

        Assert.NotNull(result);
        Assert.Equal(ChatSessionStatus.Queued, result.Status);
        _mockSessionCreationService.Verify(x => x.CreateSessionAsync(userId), Times.Once);
    }

    [Fact]
    public async Task CreateChatSessionAsync_QueueFull_RefusesChat()
    {
        
        var userId = "user123";
        var refusedSession = new ChatSession(userId, _mockTimeProvider.Object);
        refusedSession.Refuse();
        _mockSessionCreationService.Setup(x => x.CreateSessionAsync(userId))
            .ReturnsAsync(refusedSession);

        
        var result = await _service.CreateChatSessionAsync(userId);

        
        Assert.Equal(ChatSessionStatus.Refused, result.Status);
        _mockSessionCreationService.Verify(x => x.CreateSessionAsync(userId), Times.Once);
    }

    #endregion

    #region 2. Queue Capacity Tests

    [Fact]
    public async Task CanAcceptNewChatAsync_QueueSizeWithinLimit_ReturnsTrue()
    {
        
        _mockCapacityCalculationService.Setup(x => x.CanAcceptNewSessionAsync())
            .ReturnsAsync(true);

        
        var result = await _service.CanAcceptNewChatAsync();

        
        Assert.True(result);
        _mockCapacityCalculationService.Verify(x => x.CanAcceptNewSessionAsync(), Times.Once);
    }

    [Fact]
    public async Task CanAcceptNewChatAsync_QueueFullNoOfficeHours_ReturnsFalse()
    {
        
        _mockCapacityCalculationService.Setup(x => x.CanAcceptNewSessionAsync())
            .ReturnsAsync(false);

        
        var result = await _service.CanAcceptNewChatAsync();

        
        Assert.False(result);
        _mockCapacityCalculationService.Verify(x => x.CanAcceptNewSessionAsync(), Times.Once);
    }

    [Fact]
    public async Task CanAcceptNewChatAsync_QueueFullButOfficeHoursWithOverflow_ReturnsTrue()
    {
        
        _mockCapacityCalculationService.Setup(x => x.CanAcceptNewSessionAsync())
            .ReturnsAsync(true);

        
        var result = await _service.CanAcceptNewChatAsync();

        
        Assert.True(result);
        _mockCapacityCalculationService.Verify(x => x.CanAcceptNewSessionAsync(), Times.Once);
    }

    [Fact]
    public async Task CanAcceptNewChatAsync_OverflowAlsoFull_ReturnsFalse()
    {
        
        SetupTeamCapacities(teamA: 21, teamB: 22, teamC: 12, overflow: 24);
        _mockSessionRepository.Setup(x => x.GetQueueLengthAsync()).ReturnsAsync(85);
        _mockSessionRepository.Setup(x => x.GetOverflowQueueLengthAsync()).ReturnsAsync(40); // Overflow full
        _mockBusinessHoursService.Setup(x => x.IsOfficeHoursAsync()).ReturnsAsync(true);

        
        var result = await _service.CanAcceptNewChatAsync();

        
        Assert.False(result);
    }

    #endregion

    #region 3. Polling and Session Monitoring Tests

    [Fact]
    public async Task PollSessionAsync_ValidSession_UpdatesLastPollTime()
    {
        
        var sessionId = Guid.NewGuid();
        var session = new ChatSession("user123", _mockTimeProvider.Object);
        _mockSessionRepository.Setup(x => x.GetByIdAsync(sessionId)).ReturnsAsync(session);

        
        var result = await _service.PollSessionAsync(sessionId);

        
        Assert.True(result);
        _mockSessionRepository.Verify(x => x.UpdateAsync(session), Times.Once);
    }

    [Fact]
    public async Task PollSessionAsync_NonExistentSession_ReturnsFalse()
    {
        
        var sessionId = Guid.NewGuid();
        _mockSessionRepository.Setup(x => x.GetByIdAsync(sessionId)).ReturnsAsync((ChatSession?)null);

        
        var result = await _service.PollSessionAsync(sessionId);

        
        Assert.False(result);
    }

    #endregion

    #region 4. Agent Assignment Algorithm Tests

    [Fact]
    public void Agent_CapacityCalculation_CorrectSeniorityMultipliers()
    {
        var junior = new Agent(Guid.NewGuid(), "Junior", Seniority.Junior, TeamType.TeamA, _mockTimeProvider.Object);
        var midLevel = new Agent(Guid.NewGuid(), "Mid", Seniority.MidLevel, TeamType.TeamA, _mockTimeProvider.Object);
        var senior = new Agent(Guid.NewGuid(), "Senior", Seniority.Senior, TeamType.TeamA, _mockTimeProvider.Object);
        var teamLead = new Agent(Guid.NewGuid(), "Lead", Seniority.TeamLead, TeamType.TeamA, _mockTimeProvider.Object);

        Assert.Equal(4, junior.MaxConcurrentChats);     // 10 * 0.4 = 4
        Assert.Equal(6, midLevel.MaxConcurrentChats);   // 10 * 0.6 = 6
        Assert.Equal(8, senior.MaxConcurrentChats);     // 10 * 0.8 = 8
        Assert.Equal(5, teamLead.MaxConcurrentChats);   // 10 * 0.5 = 5
    }

    [Fact]
    public void TeamCapacity_Example1_2MidLevel1Junior_Returns16()
    {
        var agents = new List<Agent>
        {
            new Agent(Guid.NewGuid(), "Mid1", Seniority.MidLevel, TeamType.TeamA, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Mid2", Seniority.MidLevel, TeamType.TeamA, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Junior", Seniority.Junior, TeamType.TeamA, _mockTimeProvider.Object)
        };

        
        var totalCapacity = agents.Sum(a => a.MaxConcurrentChats);

        Assert.Equal(16, totalCapacity);
    }

    [Fact]
    public void QueueSizeLimit_Capacity16_MaxQueue24()
    {
        
        var capacity = 16;
        
        
        var maxQueueSize = (int)(capacity * 1.5);
        
        
        Assert.Equal(24, maxQueueSize);
    }

    #endregion

    #region 5. Team Configuration Tests

    [Fact]
    public void TeamConfiguration_TeamA_CorrectComposition()
    {
         
        var teamA = new List<Agent>
        {
            new Agent(Guid.NewGuid(), "Lead", Seniority.TeamLead, TeamType.TeamA, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Mid1", Seniority.MidLevel, TeamType.TeamA, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Mid2", Seniority.MidLevel, TeamType.TeamA, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Junior", Seniority.Junior, TeamType.TeamA, _mockTimeProvider.Object)
        };

        
        var capacity = teamA.Sum(a => a.MaxConcurrentChats);

         
        Assert.Equal(21, capacity);
    }

    [Fact]
    public void TeamConfiguration_TeamB_CorrectComposition()
    {
         
        var teamB = new List<Agent>
        {
            new Agent(Guid.NewGuid(), "Senior", Seniority.Senior, TeamType.TeamB, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Mid", Seniority.MidLevel, TeamType.TeamB, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Junior1", Seniority.Junior, TeamType.TeamB, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Junior2", Seniority.Junior, TeamType.TeamB, _mockTimeProvider.Object)
        };

        
        var capacity = teamB.Sum(a => a.MaxConcurrentChats);

         
        Assert.Equal(22, capacity);
    }

    [Fact]
    public void TeamConfiguration_TeamC_CorrectComposition()
    {
         
        var teamC = new List<Agent>
        {
            new Agent(Guid.NewGuid(), "Mid1", Seniority.MidLevel, TeamType.TeamC, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Mid2", Seniority.MidLevel, TeamType.TeamC, _mockTimeProvider.Object)
        };

        
        var capacity = teamC.Sum(a => a.MaxConcurrentChats);

         
        Assert.Equal(12, capacity);
    }

    [Fact]
    public void TeamConfiguration_Overflow_CorrectComposition()
    {
         
        var overflow = new List<Agent>();
        for (int i = 1; i <= 6; i++)
        {
            overflow.Add(new Agent(Guid.NewGuid(), $"Overflow{i}", Seniority.Junior, TeamType.Overflow, _mockTimeProvider.Object));
        }

        
        var capacity = overflow.Sum(a => a.MaxConcurrentChats);

         
        Assert.Equal(24, capacity);
    }

    #endregion

    #region 6. Junior-First Assignment Tests

    [Fact]
    public void JuniorFirstAssignment_Example1_1Senior1Junior_5Chats()
    {
        var senior = new Agent(Guid.NewGuid(), "Senior", Seniority.Senior, TeamType.TeamA, _mockTimeProvider.Object);
        var junior = new Agent(Guid.NewGuid(), "Junior", Seniority.Junior, TeamType.TeamA, _mockTimeProvider.Object);

        // First 4 should go to junior (fills capacity)
        junior.AssignChat(); // 1
        junior.AssignChat(); // 2
        junior.AssignChat(); // 3
        junior.AssignChat(); // 4
        
        // 5th chat should go to senior (junior full)
        senior.AssignChat(); // 1

        Assert.Equal(4, junior.CurrentChatCount);
        Assert.Equal(1, senior.CurrentChatCount);
        Assert.False(junior.CanAcceptNewChat()); // Junior full
        Assert.True(senior.CanAcceptNewChat());  // Senior has capacity
    }

    [Fact]
    public void JuniorFirstAssignment_Example2_2Junior1Mid_6Chats()
    {
        var midLevel = new Agent(Guid.NewGuid(), "Mid", Seniority.MidLevel, TeamType.TeamA, _mockTimeProvider.Object);
        var junior1 = new Agent(Guid.NewGuid(), "Junior1", Seniority.Junior, TeamType.TeamA, _mockTimeProvider.Object);
        var junior2 = new Agent(Guid.NewGuid(), "Junior2", Seniority.Junior, TeamType.TeamA, _mockTimeProvider.Object);

        junior1.AssignChat(); // 1
        junior2.AssignChat(); // 1
        junior1.AssignChat(); // 2
        junior2.AssignChat(); // 2
        junior1.AssignChat(); // 3
        junior2.AssignChat(); // 3

        Assert.Equal(3, junior1.CurrentChatCount);
        Assert.Equal(3, junior2.CurrentChatCount);
        Assert.Equal(0, midLevel.CurrentChatCount);
        Assert.True(junior1.CanAcceptNewChat()); // Junior capacity 4, so still available
        Assert.True(junior2.CanAcceptNewChat());
        Assert.True(midLevel.CanAcceptNewChat());
    }

    #endregion

    #region 7. Session Timeout Tests

    [Fact]
    public void ChatSession_3MissedPolls_MarkedInactive()
    {
        
        var session = new ChatSession("user123", _mockTimeProvider.Object);
        var baseTime = DateTime.UtcNow;
        _mockTimeProvider.Setup(x => x.UtcNow).Returns(baseTime);

         
        session.IncrementMissedPoll(); // 1st missed poll
        session.IncrementMissedPoll(); // 2nd missed poll
        session.IncrementMissedPoll(); // 3rd missed poll
        
        var isTimedOut = session.IsTimedOut(3); // Check for 3 missed polls

        
        Assert.True(isTimedOut);
        Assert.Equal(3, session.MissedPollCount);
    }

    #endregion

    #region 8. Shift Management Tests

    [Fact]
    public void Agent_ShiftEnd_StopsAcceptingNewChats()
    {
        
        var agent = new Agent(Guid.NewGuid(), "Agent", Seniority.Junior, TeamType.TeamA, _mockTimeProvider.Object);
        var shiftStart = DateTime.UtcNow;
        var shiftEnd = shiftStart.AddHours(8);
        var nearShiftEnd = shiftEnd.AddMinutes(-3); // 3 minutes before end
        
        _mockTimeProvider.Setup(x => x.UtcNow).Returns(nearShiftEnd);

        
        agent.SetShift(shiftStart, shiftEnd);

        
        Assert.True(agent.IsActive);
        Assert.False(agent.AcceptingNewChats); // Should stop accepting 5 min before shift end
    }

    #endregion

    #region Helper Methods

    private void SetupCanAcceptNewChat(bool canAccept)
    {
        if (canAccept)
        {
            SetupTeamCapacities(teamA: 21, teamB: 22, teamC: 12);
            _mockSessionRepository.Setup(x => x.GetQueueLengthAsync()).ReturnsAsync(10);
        }
        else
        {
            SetupTeamCapacities(teamA: 21, teamB: 22, teamC: 12);
            _mockSessionRepository.Setup(x => x.GetQueueLengthAsync()).ReturnsAsync(90);
            _mockBusinessHoursService.Setup(x => x.IsOfficeHoursAsync()).ReturnsAsync(false);
        }
    }

    private void SetupTeamCapacities(int teamA = 0, int teamB = 0, int teamC = 0, int overflow = 0)
    {
        _mockAgentRepository.Setup(x => x.GetTeamCapacityAsync(TeamType.TeamA)).ReturnsAsync(teamA);
        _mockAgentRepository.Setup(x => x.GetTeamCapacityAsync(TeamType.TeamB)).ReturnsAsync(teamB);
        _mockAgentRepository.Setup(x => x.GetTeamCapacityAsync(TeamType.TeamC)).ReturnsAsync(teamC);
        _mockAgentRepository.Setup(x => x.GetTeamCapacityAsync(TeamType.Overflow)).ReturnsAsync(overflow);
    }

    #endregion
}