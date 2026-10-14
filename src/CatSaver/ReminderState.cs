namespace CatSaver;

/// <summary>
/// Decides, once per second, whether the reminder should be on screen. Pure logic
/// (no UI), so it is unit-tested. Handles:
/// <list type="bullet">
/// <item>scheduled slots (see <see cref="ReminderSchedule"/>)</item>
/// <item>"show now" (manual)</item>
/// <item>dismissing the current slot</item>
/// <item>pausing until a given time</item>
/// <item>postponing while the user is presenting / in a full-screen app, then
/// showing the full duration once they're done (capped at the next slot)</item>
/// </list>
/// </summary>
public sealed class ReminderState
{
    private DateTime? _manualUntil;
    private DateTime? _dismissedSlot;
    private DateTime? _deferredSlot;
    private DateTime? _deferredUntil;

    public ReminderState(ReminderSchedule schedule) => Schedule = schedule;

    /// <summary>Can be replaced when settings change.</summary>
    public ReminderSchedule Schedule { get; set; }

    public bool PostponeWhenBusy { get; set; } = true;

    /// <summary>Reminders are suppressed until this time (null = not paused).</summary>
    public DateTime? PausedUntil { get; private set; }

    /// <summary>True while a reminder is waiting for the user to finish a presentation/full-screen app.</summary>
    public bool IsDeferred => _deferredSlot is not null;

    public bool IsPaused(DateTime now) => PausedUntil is DateTime p && now < p;

    public void ShowNow(DateTime now) => _manualUntil = now + Schedule.Duration;

    /// <summary>Shows the reminder until <paramref name="until"/> (e.g. a settings preview).</summary>
    public void ShowUntil(DateTime until) => _manualUntil = until;

    public void StopManual() => _manualUntil = null;

    /// <summary>True while a "show now" / preview is active.</summary>
    public bool IsManual(DateTime now) => _manualUntil is DateTime m && now < m;

    /// <summary>Hides the current reminder; the next slot still fires.</summary>
    public void Dismiss(DateTime now)
    {
        _manualUntil = null;
        _deferredUntil = null;
        _deferredSlot = null;
        _dismissedSlot = Schedule.SlotStart(now);
    }

    /// <summary>
    /// Skips the reminder that is due right now, if any, without touching anything else.
    /// Used when the app starts, so launching it (e.g. at 10:05) doesn't pop the cat up
    /// straight away; the next slot still fires.
    /// </summary>
    public void SkipCurrentSlot(DateTime now) => _dismissedSlot = Schedule.SlotStart(now);

    public void Pause(DateTime until)
    {
        PausedUntil = until;
        _manualUntil = null;
        _deferredUntil = null;
        _deferredSlot = null;
    }

    public void Resume() => PausedUntil = null;

    /// <summary>
    /// Returns when the reminder should disappear if it should be visible now,
    /// otherwise null.
    /// </summary>
    /// <param name="showing">Whether the overlay is currently on screen.</param>
    /// <param name="userBusy">Whether the user is presenting / in a full-screen app.</param>
    public DateTime? Evaluate(DateTime now, bool showing, bool userBusy)
    {
        if (_manualUntil is DateTime manual)
        {
            if (now < manual) return manual;
            _manualUntil = null;
        }

        if (PausedUntil is DateTime paused)
        {
            if (now < paused) return null;
            PausedUntil = null;
        }

        if (_deferredUntil is DateTime deferredUntil)
        {
            if (now < deferredUntil) return deferredUntil;
            _deferredUntil = null;
        }

        var slot = Schedule.SlotStart(now);
        if (_deferredSlot is DateTime oldDeferred && oldDeferred != slot)
            _deferredSlot = null; // a new slot started; the old postponed one is dropped
        if (_dismissedSlot == slot) return null;

        var end = Schedule.ActiveUntil(now);
        bool due = end is not null || _deferredSlot == slot;
        if (!due) return null;

        if (!showing && PostponeWhenBusy && userBusy)
        {
            _deferredSlot = slot;
            return null;
        }

        if (_deferredSlot == slot && !showing)
        {
            // The user just finished presenting: give them the full reminder.
            _deferredSlot = null;
            var full = now + Schedule.Duration;
            var next = Schedule.NextStart(now);
            _deferredUntil = full < next ? full : next;
            return _deferredUntil;
        }

        return end;
    }
}
