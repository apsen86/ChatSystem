namespace ChatSupport.API.Models;

public class HealthResponse
{
    public bool IsHealthy { get; set; }
    public bool CanAcceptNewChats { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Message { get; set; }
}