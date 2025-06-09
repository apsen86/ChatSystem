using ChatSupport.Application.Services;
using ChatSupport.Domain.Interfaces;
using ChatSupport.Domain.Services;
using ChatSupport.Infrastructure.Repositories;
using ChatSupport.Infrastructure.Services;
using ChatSupport.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddSingleton<ITimeProvider, SystemTimeProvider>();
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<IRoundRobinCoordinator, RoundRobinCoordinator>();
builder.Services.AddScoped<IAgentSelectionService, AgentSelectionService>();

builder.Services.AddScoped<IBusinessHoursService, BusinessHoursService>();
builder.Services.AddScoped<IShiftManagementService, ShiftManagementService>();
builder.Services.AddScoped<ISessionCreationService, SessionCreationService>();
builder.Services.AddScoped<ICapacityCalculationService, CapacityCalculationService>();
builder.Services.AddScoped<IAgentAssignmentService, AgentAssignmentService>();
builder.Services.AddScoped<ISessionTimeoutService, SessionTimeoutService>();

builder.Services.AddScoped<IChatAssignmentService, ChatAssignmentService>();

// in-memory storage
builder.Services.AddSingleton<IChatSessionRepository, ChatSessionRepository>();
builder.Services.AddSingleton<IAgentRepository, AgentRepository>();

builder.Services.AddHostedService<QueueProcessingService>();
builder.Services.AddHostedService<SessionMonitoringService>();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();

// root redirects to swagger
app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapControllers();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Chat Support API started successfully");

// check capacity at startup
using var scope = app.Services.CreateScope();
var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
var businessHoursService = scope.ServiceProvider.GetRequiredService<ChatSupport.Application.Services.IBusinessHoursService>();

foreach (var team in Enum.GetValues<ChatSupport.Domain.Enums.TeamType>())
{
    var capacity = await agentRepo.GetTeamCapacityAsync(team);
    var maxQueue = (int)Math.Floor(capacity * 1.5);
    logger.LogInformation("Team {Team} - Capacity: {Capacity}, Max Queue: {MaxQueue}",
        team, capacity, maxQueue);
}

var isOfficeHours = await businessHoursService.IsOfficeHoursAsync();
logger.LogInformation("Office hours status: {IsOfficeHours}", isOfficeHours);

app.Run();