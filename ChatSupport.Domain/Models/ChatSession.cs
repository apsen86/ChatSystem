using ChatSupport.Domain.Enums;
using ChatSupport.Domain.Interfaces;

namespace ChatSupport.Domain.Models;

public class ChatSession
{
    private readonly ITimeProvider _timeProvider;
    private readonly object _pollLock = new object();
    private int _missedPollCount = 0;
    
    public Guid Id { get; private set; }
    public string UserId { get; private set; }
    public ChatSessionStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? AssignedAt { get; private set; }
    public DateTime LastPolledAt { get; private set; }
    public Guid? AssignedAgentId { get; private set; }
    public int PollCount { get; private set; }
    public bool IsInOverflow { get; private set; }
    public int MissedPollCount
    {
        get
        {
            lock (_pollLock)
            {
                return _missedPollCount;
            }
        }
    }

    public ChatSession(string userId, ITimeProvider timeProvider)
    {
        Id = Guid.NewGuid();
        UserId = userId ?? throw new ArgumentNullException(nameof(userId));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        Status = ChatSessionStatus.Queued;
        CreatedAt = _timeProvider.UtcNow;
        LastPolledAt = _timeProvider.UtcNow;
        PollCount = 0;
        IsInOverflow = false;
    }

    public void AssignToAgent(Guid agentId)
    {
        if (Status != ChatSessionStatus.Queued)
            throw new InvalidOperationException("Can only assign queued sessions");

        AssignedAgentId = agentId;
        AssignedAt = _timeProvider.UtcNow;
        Status = ChatSessionStatus.Assigned;
    }

    public void MarkActive()
    {
        if (Status != ChatSessionStatus.Assigned)
            throw new InvalidOperationException("Can only activate assigned sessions");

        Status = ChatSessionStatus.Active;
    }

    public void Poll()
    {
        lock (_pollLock)
        {
            LastPolledAt = _timeProvider.UtcNow;
            PollCount++;
            _missedPollCount = 0;
            
            // Handle state transition within the lock to prevent race conditions
            if (Status == ChatSessionStatus.Assigned)
            {
                Status = ChatSessionStatus.Active;
            }
        }
    }

    public void IncrementMissedPoll()
    {
        lock (_pollLock)
        {
            _missedPollCount++;
        }
    }

    // Check if stale and increment missed polls atomically
    public bool IncrementMissedPollIfStale(ITimeProvider timeProvider, double expectedIntervalSeconds = 1.0)
    {
        lock (_pollLock)
        {
            var timeSinceLastPoll = timeProvider.UtcNow - LastPolledAt;
            if (timeSinceLastPoll.TotalSeconds >= expectedIntervalSeconds)
            {
                _missedPollCount++;
                return true; // Incremented
            }
            return false; // Not stale, no increment
        }
    }

    public void MarkInactive()
    {
        Status = ChatSessionStatus.Inactive;
    }

    public void Complete()
    {
        Status = ChatSessionStatus.Completed;
    }

    public void Refuse()
    {
        Status = ChatSessionStatus.Refused;
    }

    public void MoveToOverflow()
    {
        IsInOverflow = true;
    }

    private const int DEFAULT_MAX_MISSED_POLLS = 3;
    
    public bool IsTimedOut(int maxMissedPolls = DEFAULT_MAX_MISSED_POLLS)
    {
        lock (_pollLock)
        {
            return _missedPollCount >= maxMissedPolls;
        }
    }

    public void ResetMissedPolls()
    {
        lock (_pollLock)
        {
            _missedPollCount = 0;
        }
    }
}