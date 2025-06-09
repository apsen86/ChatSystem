using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ChatSupport.API.Controllers;
using ChatSupport.API.Models;
using ChatSupport.Application.Services;
using ChatSupport.Domain.Interfaces;
using ChatSupport.Domain.Models;
using ChatSupport.Domain.Enums;
using System;
using System.Threading.Tasks;

namespace ChatSupport.Tests.Controllers;

public class ChatControllerIntegrationTests
{
    private readonly Mock<IChatAssignmentService> _mockChatService;
    private readonly Mock<IChatSessionRepository> _mockSessionRepository;
    private readonly Mock<ILogger<ChatController>> _mockLogger;
    private readonly ChatController _controller;

    public ChatControllerIntegrationTests()
    {
        _mockChatService = new Mock<IChatAssignmentService>();
        _mockSessionRepository = new Mock<IChatSessionRepository>();
        _mockLogger = new Mock<ILogger<ChatController>>();
        
        _controller = new ChatController(_mockChatService.Object, _mockSessionRepository.Object, _mockLogger.Object);
    }

    #region 1. Chat Session Creation API Tests

    [Fact]
    public async Task CreateChatSession_ValidRequest_ReturnsSuccessResponse()
    {
        
        var request = new CreateChatRequest { UserId = Guid.NewGuid() };
        var mockTimeProvider = new Mock<ITimeProvider>();
        var session = new ChatSession(request.UserId.ToString(), mockTimeProvider.Object);
        
        _mockSessionRepository.Setup(x => x.GetActiveSessionByUserIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ChatSession?)null);
        _mockChatService.Setup(x => x.CreateChatSessionAsync(It.IsAny<string>()))
            .ReturnsAsync(session);

        
        var result = await _controller.CreateChatSession(request);

        
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<CreateChatResponse>(okResult.Value);
        Assert.True(response.IsAccepted);
        Assert.Equal("Queued", response.Status);
        Assert.Equal("Chat session created successfully", response.Message);
    }

    [Fact]
    public async Task CreateChatSession_DuplicateUser_ReturnsExistingSession()
    {
        
        var userId = Guid.NewGuid();
        var request = new CreateChatRequest { UserId = userId };
        var mockTimeProvider = new Mock<ITimeProvider>();
        var existingSession = new ChatSession(userId.ToString(), mockTimeProvider.Object);
        
        _mockSessionRepository.Setup(x => x.GetActiveSessionByUserIdAsync(userId.ToString()))
            .ReturnsAsync(existingSession);

        
        var result = await _controller.CreateChatSession(request);

        
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<CreateChatResponse>(okResult.Value);
        Assert.True(response.IsAccepted);
        Assert.Contains("already have an active session", response.Message);
    }

    [Fact]
    public async Task CreateChatSession_QueueFull_ReturnsRefusedResponse()
    {
        
        var request = new CreateChatRequest { UserId = Guid.NewGuid() };
        var mockTimeProvider = new Mock<ITimeProvider>();
        var refusedSession = new ChatSession(request.UserId.ToString(), mockTimeProvider.Object);
        refusedSession.Refuse();
        
        _mockSessionRepository.Setup(x => x.GetActiveSessionByUserIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ChatSession?)null);
        _mockChatService.Setup(x => x.CreateChatSessionAsync(It.IsAny<string>()))
            .ReturnsAsync(refusedSession);

        
        var result = await _controller.CreateChatSession(request);

        
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<CreateChatResponse>(okResult.Value);
        Assert.False(response.IsAccepted);
        Assert.Equal("Refused", response.Status);
        Assert.Contains("refused", response.Message);
    }

    #endregion

    #region 2. Polling API Tests

    [Fact]
    public async Task PollSession_ValidSession_ReturnsSuccessResponse()
    {
        
        var sessionId = Guid.NewGuid();
        _mockChatService.Setup(x => x.PollSessionAsync(sessionId)).ReturnsAsync(true);

        
        var result = await _controller.PollSession(sessionId);

        
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PollResponse>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal("Poll successful", response.Message);
        Assert.Equal(sessionId, response.SessionId);
    }

    [Fact]
    public async Task PollSession_InvalidSession_ReturnsFailureResponse()
    {
        
        var sessionId = Guid.NewGuid();
        _mockChatService.Setup(x => x.PollSessionAsync(sessionId)).ReturnsAsync(false);

        
        var result = await _controller.PollSession(sessionId);

        
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PollResponse>(okResult.Value);
        Assert.False(response.Success);
        Assert.Equal("Session not found", response.Message);
    }

    #endregion

    #region 3. Health Check API Tests

    [Fact]
    public async Task GetHealth_SystemHealthy_ReturnsHealthyResponse()
    {
        
        _mockChatService.Setup(x => x.CanAcceptNewChatAsync()).ReturnsAsync(true);

        
        var result = await _controller.GetHealth();

        
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HealthResponse>(okResult.Value);
        Assert.True(response.IsHealthy);
        Assert.True(response.CanAcceptNewChats);
    }

    [Fact]
    public async Task GetHealth_SystemAtCapacity_ReturnsHealthyButNoCapacity()
    {
        
        _mockChatService.Setup(x => x.CanAcceptNewChatAsync()).ReturnsAsync(false);

        
        var result = await _controller.GetHealth();

        
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HealthResponse>(okResult.Value);
        Assert.True(response.IsHealthy);
        Assert.False(response.CanAcceptNewChats);
    }

    #endregion

    #region 4. Error Handling Tests

    [Fact]
    public async Task CreateChatSession_ServiceThrowsException_Returns500()
    {
        
        var request = new CreateChatRequest { UserId = Guid.NewGuid() };
        _mockSessionRepository.Setup(x => x.GetActiveSessionByUserIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ChatSession?)null);
        _mockChatService.Setup(x => x.CreateChatSessionAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("Database error"));

        
        var result = await _controller.CreateChatSession(request);

        
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task PollSession_ServiceThrowsException_Returns500()
    {
        
        var sessionId = Guid.NewGuid();
        _mockChatService.Setup(x => x.PollSessionAsync(sessionId))
            .ThrowsAsync(new Exception("Service error"));

        
        var result = await _controller.PollSession(sessionId);

        
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetHealth_ServiceThrowsException_Returns500WithUnhealthy()
    {
        
        _mockChatService.Setup(x => x.CanAcceptNewChatAsync())
            .ThrowsAsync(new Exception("System error"));

        
        var result = await _controller.GetHealth();

        
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
        var response = Assert.IsType<HealthResponse>(statusResult.Value);
        Assert.False(response.IsHealthy);
        Assert.False(response.CanAcceptNewChats);
    }

    #endregion

    #region 5. End-to-End Workflow Tests

    [Fact]
    public async Task EndToEndWorkflow_CreateSessionThenPoll_Success()
    {
        
        var userId = Guid.NewGuid();
        var createRequest = new CreateChatRequest { UserId = userId };
        var mockTimeProvider = new Mock<ITimeProvider>();
        var session = new ChatSession(userId.ToString(), mockTimeProvider.Object);
        
        _mockSessionRepository.Setup(x => x.GetActiveSessionByUserIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ChatSession?)null);
        _mockChatService.Setup(x => x.CreateChatSessionAsync(It.IsAny<string>()))
            .ReturnsAsync(session);
        _mockChatService.Setup(x => x.PollSessionAsync(session.Id)).ReturnsAsync(true);

        var createResult = await _controller.CreateChatSession(createRequest);
        var createResponse = ((OkObjectResult)createResult.Result!).Value as CreateChatResponse;
        Assert.NotNull(createResponse);
        
        var pollResult = await _controller.PollSession(createResponse.SessionId);
        var pollResponse = ((OkObjectResult)pollResult.Result!).Value as PollResponse;
        Assert.NotNull(pollResponse);

        Assert.True(createResponse.IsAccepted);
        Assert.True(pollResponse.Success);
        Assert.Equal(createResponse.SessionId, pollResponse.SessionId);
    }

    #endregion

    #region 6. Business Rule Validation Tests

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")] // Empty GUID
    public async Task CreateChatSession_InvalidUserId_HandledGracefully(string userIdString)
    {
        var userId = Guid.Parse(userIdString);
        var request = new CreateChatRequest { UserId = userId };

        var result = await _controller.CreateChatSession(request);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateChatSession_ConcurrentRequests_HandlesGracefully()
    {
        
        var userId = Guid.NewGuid();
        var request1 = new CreateChatRequest { UserId = userId };
        var request2 = new CreateChatRequest { UserId = userId };
        var mockTimeProvider = new Mock<ITimeProvider>();
        var session = new ChatSession(userId.ToString(), mockTimeProvider.Object);
        
        _mockSessionRepository.SetupSequence(x => x.GetActiveSessionByUserIdAsync(userId.ToString()))
            .ReturnsAsync((ChatSession?)null)  // First request - no existing session
            .ReturnsAsync(session);            // Second request - session exists
        _mockChatService.Setup(x => x.CreateChatSessionAsync(It.IsAny<string>()))
            .ReturnsAsync(session);

        
        var result1 = await _controller.CreateChatSession(request1);
        var result2 = await _controller.CreateChatSession(request2);

        
        var response1 = ((OkObjectResult)result1.Result!).Value as CreateChatResponse;
        var response2 = ((OkObjectResult)result2.Result!).Value as CreateChatResponse;
        Assert.NotNull(response1);
        Assert.NotNull(response2);
        
        Assert.True(response1.IsAccepted);
        Assert.True(response2.IsAccepted);
        Assert.Contains("already have an active session", response2.Message);
    }

    #endregion
}