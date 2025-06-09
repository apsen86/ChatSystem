using System.ComponentModel.DataAnnotations;

namespace ChatSupport.API.Models;

public class CreateChatRequest
{
    [Required]
    public Guid UserId { get; set; }
}