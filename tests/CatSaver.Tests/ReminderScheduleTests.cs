using CatSaver;
using Xunit;

public class ReminderScheduleTests
{
    private static readonly ReminderSchedule Hourly = new(TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(16));

    private static DateTime At(int h, int m, int s = 0) => new(2026, 10, 14, h, m, s);

    [Fact]
    public void ShowsAtTopOfTheHour()
    {
        Assert.Equal(At(10, 16), Hourly.ActiveUntil(At(10, 0)));
    }

    [Theory]
    [InlineData(10, 0, 1)]
    [InlineData(10, 8, 0)]
    [InlineData(10, 15, 59)]
    public void StaysVisibleFor16Minutes(int h, int m, int s)
    {
        Assert.Equal(At(10, 16), Hourly.ActiveUntil(At(h, m, s)));
    }

    [Theory]
    [InlineData(10, 16, 0)]
    [InlineData(10, 30, 0)]
    [InlineData(10, 59, 59)]
    public void HiddenForTheRestOfTheHour(int h, int m, int s)
    {
        Assert.Null(Hourly.ActiveUntil(At(h, m, s)));
    }

    [Fact]
    public void RepeatsEveryHour()
    {
        for (int h = 0; h < 24; h++)
        {
            Assert.Equal(At(h, 16), Hourly.ActiveUntil(At(h, 0)));
            Assert.Null(Hourly.ActiveUntil(At(h, 20)));
        }
    }

    [Fact]
    public void NextStartIsNextExactHour()
    {
        Assert.Equal(At(11, 0), Hourly.NextStart(At(10, 0)));
        Assert.Equal(At(11, 0), Hourly.NextStart(At(10, 42, 7)));
        Assert.Equal(At(0, 0).AddDays(1), Hourly.NextStart(At(23, 30)));
    }

    [Fact]
    public void SlotStartIdentifiesTheCurrentHour()
    {
        Assert.Equal(At(10, 0), Hourly.SlotStart(At(10, 59, 59)));
        Assert.Equal(At(11, 0), Hourly.SlotStart(At(11, 0)));
    }

    [Fact]
    public void CustomIntervalAndDuration()
    {
        var s = new ReminderSchedule(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5));
        Assert.Equal(At(10, 35), s.ActiveUntil(At(10, 31)));
        Assert.Null(s.ActiveUntil(At(10, 36)));
        Assert.Equal(At(11, 0), s.NextStart(At(10, 36)));
    }

    [Fact]
    public void DurationIsCappedAtInterval()
    {
        var s = new ReminderSchedule(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30));
        Assert.Equal(TimeSpan.FromMinutes(10), s.Duration);
    }

    [Fact]
    public void IntervalNotDividingADayRestartsAtMidnight()
    {
        var s = new ReminderSchedule(TimeSpan.FromMinutes(7 * 60), TimeSpan.FromMinutes(120));
        // Slots: 00:00, 07:00, 14:00, 21:00 – the 21:00 slot ends at midnight.
        Assert.Equal(At(21, 0), s.SlotStart(At(23, 0)));
        Assert.Equal(At(0, 0).AddDays(1), s.NextStart(At(22, 0)));
    }
}
