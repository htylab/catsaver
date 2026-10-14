using CatSaver;
using Xunit;

public class ReminderStateTests
{
    private static DateTime At(int h, int m, int s = 0) => new(2026, 10, 14, h, m, s);

    private static ReminderState NewState() =>
        new(new ReminderSchedule(TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(16)));

    [Fact]
    public void ShowsDuringScheduledWindowOnly()
    {
        var s = NewState();
        Assert.Equal(At(10, 16), s.Evaluate(At(10, 0), showing: false, userBusy: false));
        Assert.Equal(At(10, 16), s.Evaluate(At(10, 15), showing: true, userBusy: false));
        Assert.Null(s.Evaluate(At(10, 16), showing: true, userBusy: false));
    }

    [Fact]
    public void DismissHidesUntilNextSlot()
    {
        var s = NewState();
        s.Evaluate(At(10, 0), false, false);
        s.Dismiss(At(10, 3));
        Assert.Null(s.Evaluate(At(10, 4), false, false));
        Assert.Equal(At(11, 16), s.Evaluate(At(11, 0), false, false));
    }

    [Fact]
    public void StartingMidReminderWaitsForTheNextSlot()
    {
        var s = NewState();
        s.SkipCurrentSlot(At(10, 5));
        Assert.Null(s.Evaluate(At(10, 5), false, false));
        Assert.Equal(At(11, 16), s.Evaluate(At(11, 0), false, false));
    }

    [Fact]
    public void StartingOutsideAReminderStillShowsTheNextOne()
    {
        var s = NewState();
        s.SkipCurrentSlot(At(10, 30));
        Assert.Equal(At(11, 16), s.Evaluate(At(11, 0), false, false));
    }

    [Fact]
    public void ShowNowLastsOneDuration()
    {
        var s = NewState();
        s.ShowNow(At(10, 30));
        Assert.Equal(At(10, 46), s.Evaluate(At(10, 31), false, false));
        Assert.Null(s.Evaluate(At(10, 46), true, false));
    }

    [Fact]
    public void PauseSuppressesUntilItEnds()
    {
        var s = NewState();
        s.Pause(At(12, 30));
        Assert.True(s.IsPaused(At(11, 0)));
        Assert.Null(s.Evaluate(At(11, 0), false, false));
        Assert.Null(s.Evaluate(At(12, 5), false, false));
        Assert.Equal(At(13, 16), s.Evaluate(At(13, 0), false, false));
        Assert.Null(s.PausedUntil);
    }

    [Fact]
    public void PauseEndingMidWindowShowsTheRest()
    {
        var s = NewState();
        s.Pause(At(10, 5));
        Assert.Null(s.Evaluate(At(10, 1), false, false));
        Assert.Equal(At(10, 16), s.Evaluate(At(10, 5), false, false));
    }

    [Fact]
    public void ResumeCancelsPause()
    {
        var s = NewState();
        s.Pause(At(23, 59));
        s.Resume();
        Assert.Equal(At(10, 16), s.Evaluate(At(10, 0), false, false));
    }

    [Fact]
    public void BusyUserGetsFullReminderAfterwards()
    {
        var s = NewState();
        Assert.Null(s.Evaluate(At(10, 0), showing: false, userBusy: true));
        Assert.True(s.IsDeferred);
        Assert.Null(s.Evaluate(At(10, 20), false, true)); // still presenting after the window
        // Presentation ends at 10:30: full 16 minutes from now.
        Assert.Equal(At(10, 46), s.Evaluate(At(10, 30), false, false));
        Assert.False(s.IsDeferred);
        Assert.Equal(At(10, 46), s.Evaluate(At(10, 40), true, false));
        Assert.Null(s.Evaluate(At(10, 46), true, false));
        Assert.Null(s.Evaluate(At(10, 50), false, false)); // not shown again
    }

    [Fact]
    public void DeferredReminderIsCappedAtNextSlot()
    {
        var s = NewState();
        s.Evaluate(At(10, 0), false, true);
        Assert.Equal(At(11, 0), s.Evaluate(At(10, 50), false, false));
    }

    [Fact]
    public void DeferredReminderIsDroppedWhenNextSlotStarts()
    {
        var s = NewState();
        s.Evaluate(At(10, 0), false, true);
        // Busy for over an hour; at 11:00 the new slot takes over (still busy → deferred again).
        Assert.Null(s.Evaluate(At(11, 0), false, true));
        Assert.Equal(At(11, 25), s.Evaluate(At(11, 9), false, false));
    }

    [Fact]
    public void BusyDoesNotHideAReminderAlreadyOnScreen()
    {
        var s = NewState();
        Assert.Equal(At(10, 16), s.Evaluate(At(10, 0), false, false));
        Assert.Equal(At(10, 16), s.Evaluate(At(10, 5), showing: true, userBusy: true));
    }

    [Fact]
    public void PostponeCanBeTurnedOff()
    {
        var s = NewState();
        s.PostponeWhenBusy = false;
        Assert.Equal(At(10, 16), s.Evaluate(At(10, 0), false, true));
    }
}
