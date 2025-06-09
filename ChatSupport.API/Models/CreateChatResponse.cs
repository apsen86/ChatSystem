namespace ChatSupport.API.Models;

public class CreateChatResponse
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsAccepted { get; set; }
}