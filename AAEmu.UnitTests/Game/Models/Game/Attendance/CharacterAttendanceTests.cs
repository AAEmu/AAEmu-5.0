using AAEmu.Game.Models.Game.Attendance;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Attendance;

public class CharacterAttendanceTests
{
    [Fact]
    public void GetCalendar_IncludesOnlyClaimsFromCurrentMonthAndYear()
    {
        var now = new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc);
        var records = new AttendanceRecord[31];
        records[0] = new AttendanceRecord { Accept = true, AccountAttendance = now.AddMonths(-1).AddDays(-2) };
        records[1] = new AttendanceRecord { Accept = true, AccountAttendance = now.AddYears(-1).AddDays(-1) };
        records[2] = new AttendanceRecord { Accept = true, AccountAttendance = now };

        var calendar = CharacterAttendance.GetCalendar(records, now);

        Assert.Equal(31, calendar.Count);
        Assert.False(calendar[0].Accept);
        Assert.False(calendar[1].Accept);
        Assert.True(calendar[2].Accept);
        Assert.Equal(now, calendar[2].AccountAttendance);
        Assert.False(calendar[30].Accept);
    }
}
