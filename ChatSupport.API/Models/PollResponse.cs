namespace ChatSupport.API.Models;

public class PollResponse
{
    public Guid SessionId { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}