using ChatSupport.Domain.Interfaces;

namespace ChatSupport.Infrastructure.Services;

public class SystemTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}