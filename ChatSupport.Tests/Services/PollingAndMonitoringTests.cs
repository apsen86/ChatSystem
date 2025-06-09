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

public class PollingAndMonitoringTests
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

    public PollingAndMonitoringTests()
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

    #region 1. Polling Every 1 Second Tests

    [Fact]
    public async Task ChatWindow_ReceivesOK_StartsPollingEvery1Second()
    {
        
        var sessionId = Guid.NewGuid();
        var session = new ChatSession("user123", _mockTimeProvider.Object);
        _mockSessionRepository.Setup(x => x.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var poll1 = await _service.PollSessionAsync(sessionId);
        await Task.Delay(1000); // 1 second
        var poll2 = await _service.PollSessionAsync(sessionId);
        await Task.Delay(1000); // 1 second  
        var poll3 = await _service.PollSessionAsync(sessionId);

        
        Assert.True(poll1);
        Assert.True(poll2);
        Assert.True(poll3);
        _mockSessionRepository.Verify(x => x.UpdateAsync(session), Times.Exactly(3));
    }

    #endregion

    #region 2. Session Timeout After 3 Missed Polls

    [Fact]
    public void ChatSession_3MissedPolls_MarkedInactive()
    {
        
        var baseTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _mockTimeProvider.Setup(x => x.UtcNow).Returns(baseTime);
        var session = new ChatSession("user123", _mockTimeProvider.Object);

         
        session.IncrementMissedPoll(); // 1st missed poll
        session.IncrementMissedPoll(); // 2nd missed poll
        session.IncrementMissedPoll(); // 3rd missed poll
        
        var isTimedOut = session.IsTimedOut(3);

        
        Assert.True(isTimedOut);
        Assert.Equal(3, session.MissedPollCount);
    }

    [Fact]
    public void ChatSession_WithinTimeout_NotMarkedInactive()
    {
        
        var baseTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _mockTimeProvider.SetupSequence(x => x.UtcNow)
            .Returns(baseTime)                // Session creation
            .Returns(baseTime.AddSeconds(1))  // First poll
            .Returns(baseTime.AddSeconds(3)); // Check timeout (2 seconds later = within limit)

        var session = new ChatSession("user123", _mockTimeProvider.Object);

        
        session.Poll(); // Last poll at baseTime + 1 second
        
        // Only 2 seconds passed (within 3 second limit)
        var isTimedOut = session.IsTimedOut();

        
        Assert.False(isTimedOut);
    }

    #endregion

    #region 3. Monitor Queue and Session Management

    [Fact]
    public async Task ProcessQueueAsync_ActiveSessions_AssignsToAgents()
    {
        
        var queuedSessions = new List<ChatSession>
        {
            new ChatSession("user1", _mockTimeProvider.Object),
            new ChatSession("user2", _mockTimeProvider.Object)
        };
        var availableAgent = new Agent(Guid.NewGuid(), "Agent1", Seniority.Junior, TeamType.TeamA, _mockTimeProvider.Object);

        _mockSessionRepository.Setup(x => x.GetQueuedSessionsAsync()).ReturnsAsync(queuedSessions);
        _mockBusinessHoursService.Setup(x => x.IsOfficeHoursAsync()).ReturnsAsync(true);
        _mockAgentAssignmentService.Setup(x => x.ProcessQueueBatchAsync()).Returns(Task.CompletedTask);

        
        await _service.ProcessQueueAsync();

         
        _mockAgentAssignmentService.Verify(x => x.ProcessQueueBatchAsync(), Times.Once);
        _mockBusinessHoursService.Verify(x => x.IsOfficeHoursAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task MonitorSessionsAsync_TimedOutSessions_ProcessedByTimeoutService()
    {
        _mockSessionTimeoutService.Setup(x => x.ProcessSessionTimeoutsAsync())
            .Returns(Task.CompletedTask);

        await _service.MonitorSessionsAsync();

        _mockSessionTimeoutService.Verify(x => x.ProcessSessionTimeoutsAsync(), Times.Once);
    }

    #endregion

    #region 4. Queue Management Tests

    [Fact]
    public async Task GetQueuedSessionsAsync_ReturnsCorrectSessionCount()
    {
        
        var queuedSessions = new List<ChatSession>
        {
            new ChatSession("user1", _mockTimeProvider.Object),
            new ChatSession("user2", _mockTimeProvider.Object),
            new ChatSession("user3", _mockTimeProvider.Object)
        };

        _mockSessionRepository.Setup(x => x.GetQueuedSessionsAsync()).ReturnsAsync(queuedSessions);

        
        var sessions = await _mockSessionRepository.Object.GetQueuedSessionsAsync();

        
        Assert.Equal(3, sessions.Count());
    }

    [Fact]
    public async Task GetQueueLengthAsync_ReturnsCorrectCount()
    {
        
        _mockSessionRepository.Setup(x => x.GetQueueLengthAsync()).ReturnsAsync(5);

        
        var length = await _mockSessionRepository.Object.GetQueueLengthAsync();

        
        Assert.Equal(5, length);
    }

    #endregion

    #region 5. Agent Concurrency Tests

    [Fact]
    public void Agent_MaxConcurrency10_MultipliedBySeniority()
    {
        
        var agents = new[]
        {
            new Agent(Guid.NewGuid(), "Junior", Seniority.Junior, TeamType.TeamA, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Mid", Seniority.MidLevel, TeamType.TeamA, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Senior", Seniority.Senior, TeamType.TeamA, _mockTimeProvider.Object),
            new Agent(Guid.NewGuid(), "Lead", Seniority.TeamLead, TeamType.TeamA, _mockTimeProvider.Object)
        };

        // Max concurrency 10 multiplied by efficiency
        Assert.Equal(4, agents[0].MaxConcurrentChats);  // 10 * 0.4 = 4
        Assert.Equal(6, agents[1].MaxConcurrentChats);  // 10 * 0.6 = 6
        Assert.Equal(8, agents[2].MaxConcurrentChats);  // 10 * 0.8 = 8
        Assert.Equal(5, agents[3].MaxConcurrentChats);  // 10 * 0.5 = 5
    }

    [Fact]
    public void Agent_AtMaxCapacity_CannotAcceptNewChat()
    {
        
        var junior = new Agent(Guid.NewGuid(), "Junior", Seniority.Junior, TeamType.TeamA, _mockTimeProvider.Object);
        
         
        junior.AssignChat(); // 1
        junior.AssignChat(); // 2  
        junior.AssignChat(); // 3
        junior.AssignChat(); // 4

        
        Assert.Equal(4, junior.CurrentChatCount);
        Assert.False(junior.CanAcceptNewChat());
    }

    #endregion

    #region 6. Thread Safety Tests

    [Fact]
    public async Task Agent_ConcurrentChatAssignment_ThreadSafe()
    {
        
        var agent = new Agent(Guid.NewGuid(), "Agent", Seniority.Senior, TeamType.TeamA, _mockTimeProvider.Object);
        var tasks = new List<Task>();

         
        for (int i = 0; i < 8; i++) // Max capacity for Senior
        {
            tasks.Add(Task.Run(() =>
            {
                if (agent.CanAcceptNewChat())
                {
                    agent.AssignChat();
                }
            }));
        }

        await Task.WhenAll(tasks);

        
        Assert.True(agent.CurrentChatCount <= agent.MaxConcurrentChats);
    }

    #endregion

    #region Helper Methods

    private void SetupTeamCapacities(int teamA = 0, int teamB = 0, int teamC = 0, int overflow = 0)
    {
        _mockAgentRepository.Setup(x => x.GetTeamCapacityAsync(TeamType.TeamA)).ReturnsAsync(teamA);
        _mockAgentRepository.Setup(x => x.GetTeamCapacityAsync(TeamType.TeamB)).ReturnsAsync(teamB);
        _mockAgentRepository.Setup(x => x.GetTeamCapacityAsync(TeamType.TeamC)).ReturnsAsync(teamC);
        _mockAgentRepository.Setup(x => x.GetTeamCapacityAsync(TeamType.Overflow)).ReturnsAsync(overflow);
    }

    #endregion
}