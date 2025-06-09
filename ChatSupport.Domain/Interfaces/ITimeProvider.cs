using System;

namespace ChatSupport.Domain.Interfaces;

public interface ITimeProvider
{
    DateTime UtcNow { get; }
}