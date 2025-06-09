using ChatSupport.Domain.Enums;
using ChatSupport.Domain.Interfaces;

namespace ChatSupport.Domain.Models;

public class Agent
{
    private readonly object _lockObject = new object();
    private readonly ITimeProvider _timeProvider;
    private int _currentChatCount;
    private int _reservedCapacity;

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public Seniority Seniority { get; private set; }
    public TeamType TeamType { get; private set; }
    public DateTime ShiftStart { get; private set; }
    public DateTime ShiftEnd { get; private set; }
    public bool IsActive { get; private set; }
    public bool AcceptingNewChats { get; private set; }

    public Agent(Guid id, string name, Seniority seniority, TeamType teamType, ITimeProvider timeProvider)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Seniority = seniority;
        TeamType = teamType;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _currentChatCount = 0;
        _reservedCapacity = 0;
        IsActive = true;
        AcceptingNewChats = true;
    }

    public int CurrentChatCount
    {
        get
        {
            lock (_lockObject)
            {
                return _currentChatCount;
            }
        }
    }

    public int AvailableCapacity
    {
        get
        {
            lock (_lockObject)
            {
                return Math.Max(0, MaxConcurrentChats - _currentChatCount - _reservedCapacity);
            }
        }
    }

    // Capacity calculation constants
    private const int BASE_CONCURRENT_CHAT_CAPACITY = 10;
    private const double JUNIOR_CAPACITY_MULTIPLIER = 0.4;
    private const double MIDLEVEL_CAPACITY_MULTIPLIER = 0.6;
    private const double SENIOR_CAPACITY_MULTIPLIER = 0.8;
    private const double TEAMLEAD_CAPACITY_MULTIPLIER = 0.5;
    private const int SHIFT_HANDOFF_BUFFER_MINUTES = 5;

    public int MaxConcurrentChats => (int)Math.Floor(BASE_CONCURRENT_CHAT_CAPACITY * GetSeniorityMultiplier());

    public double GetSeniorityMultiplier() => Seniority switch
    {
        Seniority.Junior => JUNIOR_CAPACITY_MULTIPLIER,
        Seniority.MidLevel => MIDLEVEL_CAPACITY_MULTIPLIER,
        Seniority.Senior => SENIOR_CAPACITY_MULTIPLIER,
        Seniority.TeamLead => TEAMLEAD_CAPACITY_MULTIPLIER,
        _ => throw new ArgumentOutOfRangeException()
    };

    public bool CanAcceptNewChat()
    {
        lock (_lockObject)
        {
            return IsActive && AcceptingNewChats &&
                   (_currentChatCount + _reservedCapacity) < MaxConcurrentChats;
        }
    }

    public bool TryReserveCapacity()
    {
        lock (_lockObject)
        {
            if (IsActive && AcceptingNewChats &&
                (_currentChatCount + _reservedCapacity) < MaxConcurrentChats)
            {
                _reservedCapacity++;
                return true;
            }
            return false;
        }
    }

    public void ReleaseReservation()
    {
        lock (_lockObject)
        {
            if (_reservedCapacity > 0)
            {
                _reservedCapacity--;
            }
        }
    }

    public bool ConfirmReservation()
    {
        lock (_lockObject)
        {
            if (_reservedCapacity > 0)
            {
                _reservedCapacity--;
                _currentChatCount++;
                return true;
            }
            return false;
        }
    }

    public void AssignChat()
    {
        lock (_lockObject)
        {
            if (!IsActive || !AcceptingNewChats || _currentChatCount >= MaxConcurrentChats)
                throw new InvalidOperationException("Agent cannot accept new chats");
            
            _currentChatCount++;
        }
    }

    public void CompleteChat()
    {
        lock (_lockObject)
        {
            if (_currentChatCount <= 0)
                throw new InvalidOperationException("No active chats to complete");
            
            _currentChatCount--;
        }
    }

    public bool TryCompleteChat()
    {
        lock (_lockObject)
        {
            if (_currentChatCount > 0)
            {
                _currentChatCount--;
                return true;
            }
            return false;
        }
    }

    public void SetShift(DateTime start, DateTime end)
    {
        ShiftStart = start;
        ShiftEnd = end;
        UpdateShiftStatus();
    }

    public void UpdateShiftStatus()
    {
        var currentTime = _timeProvider.UtcNow;
        IsActive = currentTime >= ShiftStart && currentTime <= ShiftEnd;
        
        // Stop accepting new chats 5 minutes before shift ends
        AcceptingNewChats = IsActive && (ShiftEnd - currentTime).TotalMinutes > SHIFT_HANDOFF_BUFFER_MINUTES;
    }

    public void UpdateShiftStatusWithOverlap(DateTime nextShiftStart)
    {
        var currentTime = _timeProvider.UtcNow;
        IsActive = currentTime >= ShiftStart && currentTime <= ShiftEnd;
        
        // Keep accepting until next shift starts
        var handoffTime = nextShiftStart.AddMinutes(SHIFT_HANDOFF_BUFFER_MINUTES);
        AcceptingNewChats = IsActive && currentTime < handoffTime;
    }

    public void StopAcceptingNewChats()
    {
        AcceptingNewChats = false;
    }

    public bool HasAvailableCapacity()
    {
        lock (_lockObject)
        {
            return AvailableCapacity > 0;
        }
    }

    public double CapacityUtilizationRatio()
    {
        lock (_lockObject)
        {
            if (MaxConcurrentChats == 0) return 0;
            return (double)_currentChatCount / MaxConcurrentChats;
        }
    }

    public double AvailableCapacityRatio()
    {
        lock (_lockObject)
        {
            if (MaxConcurrentChats == 0) return 0;
            return (double)AvailableCapacity / MaxConcurrentChats;
        }
    }
}