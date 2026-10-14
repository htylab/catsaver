namespace CatSaver;

/// <summary>
/// Pure scheduling logic (no UI), so it can be unit-tested anywhere.
/// Reminder slots are aligned to local midnight: with a 60-minute interval the
/// slots start at 00:00, 01:00, ..., 23:00. Each slot is "active" for
/// <see cref="Duration"/> after it starts.
/// </summary>
public sealed class ReminderSchedule
{
    public TimeSpan Interval { get; }
    public TimeSpan Duration { get; }

    public ReminderSchedule(TimeSpan interval, TimeSpan duration)
    {
        if (interval < TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be at least 1 minute.");
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");

        Interval = interval;
        // A reminder longer than the interval would never end; cap it.
        Duration = duration > interval ? interval : duration;
    }

    /// <summary>Start of the slot that contains <paramref name="now"/>.</summary>
    public DateTime SlotStart(DateTime now)
    {
        var midnight = now.Date;
        var sinceMidnight = now - midnight;
        long slots = sinceMidnight.Ticks / Interval.Ticks;
        return midnight + TimeSpan.FromTicks(slots * Interval.Ticks);
    }

    /// <summary>
    /// If a reminder should be visible at <paramref name="now"/>, returns the time
    /// it should disappear; otherwise null.
    /// </summary>
    public DateTime? ActiveUntil(DateTime now)
    {
        var start = SlotStart(now);
        var end = start + Duration;
        // Slots never run past midnight; the next day starts a fresh slot at 00:00.
        var nextMidnight = now.Date.AddDays(1);
        if (end > nextMidnight) end = nextMidnight;
        return now < end ? end : null;
    }

    /// <summary>The next time a reminder will begin (strictly after <paramref name="now"/>).</summary>
    public DateTime NextStart(DateTime now)
    {
        var next = SlotStart(now) + Interval;
        var nextMidnight = now.Date.AddDays(1);
        return next > nextMidnight ? nextMidnight : next;
    }
}
