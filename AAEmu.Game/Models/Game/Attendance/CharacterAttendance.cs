using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Attendance;

public class CharacterAttendance(Character owner)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private const int MaxRewards = 28;
    private const int MaxDaysInMonth = 31;
    private readonly Lock _sync = new();
    private AttendanceRecord[] _records = new AttendanceRecord[MaxDaysInMonth];

    public void RegisterDailyAttendance()
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            var dayIndex = now.Day - 1;
            if (IsAttendedThisMonth(_records[dayIndex], now))
                return;

            try
            {
#pragma warning disable CA1508 // CreateConnection returns null when opening fails.
                using var connection = MySQL.CreateConnection();
                if (connection == null)
                    return;
#pragma warning restore CA1508

                using var transaction = connection.BeginTransaction();

                // Lock the account's day row even when this is the first claim for that day.
                using var claim = connection.CreateCommand();
                claim.Transaction = transaction;
                claim.CommandText = "INSERT INTO attendances (id, owner, account_attendance, accept) VALUES (@id, @owner, NULL, 0) ON DUPLICATE KEY UPDATE id = id";
                claim.Parameters.AddWithValue("@id", dayIndex);
                claim.Parameters.AddWithValue("@owner", owner.AccountId);
                claim.ExecuteNonQuery();

                Load(connection, transaction);
                var attendedCount = 0;
                foreach (var record in _records)
                {
                    if (IsAttendedThisMonth(record, now))
                        attendedCount++;
                }

                if (IsAttendedThisMonth(_records[dayIndex], now) || attendedCount >= MaxRewards)
                    return;

                var rewardNumber = attendedCount + 1;
                var (itemId, count) = AttendanceGameData.Instance.GetReward(now.Year, now.Month, rewardNumber);
                var (bonusId, bonusCount) = AttendanceGameData.Instance.GetAdditionalReward(now.Year, now.Month, rewardNumber);
                if (itemId == 0 || count <= 0)
                {
                    Logger.Warn("No attendance reward configured for {0:yyyy-MM}, visit {1}.", now, rewardNumber);
                    return;
                }

                var bag = owner.Inventory.Bag;
                var hasBonus = bonusId > 0 && bonusCount > 0;
                var slots = GetRequiredSlots(bag, itemId, hasBonus && itemId == bonusId ? (long)count + bonusCount : count);
                if (hasBonus && itemId != bonusId)
                    slots += GetRequiredSlots(bag, bonusId, bonusCount);

                if (slots > bag.FreeSlotCount)
                {
                    owner.SendErrorMessage(ErrorMessageType.BagFull);
                    return;
                }

                claim.CommandText = "UPDATE attendances SET account_attendance = @time, accept = 1 WHERE id = @id AND owner = @owner";
                claim.Parameters.AddWithValue("@time", now);
                claim.ExecuteNonQuery();

                if (!bag.AcquireDefaultItem(ItemTaskType.TakeScheduleItem, itemId, count) ||
                    (hasBonus && !bag.AcquireDefaultItem(ItemTaskType.TakeScheduleItem, bonusId, bonusCount)))
                {
                    Logger.Error("Failed to deliver attendance reward {0} for character {1}.", rewardNumber, owner.Id);
                    return;
                }

                var attendance = new AttendanceRecord();
                attendance.MarkAsAttended(now);
                _records[dayIndex] = attendance;
                transaction.Commit();

                var isArchelife = owner.Connection?.Payment?.PremiumState == true;
                owner.SendPacket(new SCDbAttendanceTimePacket(true, now, isArchelife));
                Logger.Info("Character {0} received attendance reward {1} on {2:yyyy-MM-dd}.", owner.Id, rewardNumber, now);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Attendance claim failed for account {0}.", owner.AccountId);
            }
        }
    }

    private static long GetRequiredSlots(ItemContainer bag, uint itemId, long count)
    {
        var template = ItemManager.Instance.GetTemplate(itemId);
        if (template == null || template.MaxCount <= 0)
            throw new InvalidOperationException($"Invalid attendance reward item {itemId}.");

        foreach (var item in bag.Items)
        {
            if (item.TemplateId == itemId && item.Count < template.MaxCount)
                count -= template.MaxCount - item.Count;
        }

        return count <= 0 ? 0 : (count + template.MaxCount - 1) / template.MaxCount;
    }

    public void Send()
    {
        lock (_sync)
        {
            owner.SendPacket(new SCAccountAttendancePacket(GetCalendar(_records, DateTime.UtcNow)));
        }
    }

    public void SendEmptyAttendances()
    {
        var records = new List<AttendanceRecord>(MaxDaysInMonth);
        for (var i = 0; i < MaxDaysInMonth; i++)
            records.Add(new AttendanceRecord());

        owner.SendPacket(new SCAccountAttendancePacket(records));
    }

    internal static List<AttendanceRecord> GetCalendar(AttendanceRecord[] records, DateTime now)
    {
        var calendar = new List<AttendanceRecord>(MaxDaysInMonth);
        foreach (var record in records)
            calendar.Add(IsAttendedThisMonth(record, now) ? record : new AttendanceRecord());

        return calendar;
    }

    private static bool IsAttendedThisMonth(AttendanceRecord record, DateTime now)
    {
        return record is { Accept: true } &&
               record.AccountAttendance.Year == now.Year &&
               record.AccountAttendance.Month == now.Month;
    }

    public void Load(MySqlConnection connection, MySqlTransaction transaction = null)
    {
        lock (_sync)
        {
            var records = new AttendanceRecord[MaxDaysInMonth];

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT id, account_attendance, accept FROM attendances WHERE owner = @owner";
            command.Parameters.AddWithValue("@owner", owner.AccountId);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var dayIndex = reader.GetInt32("id");
                if (dayIndex >= MaxDaysInMonth || dayIndex < 0)
                    continue;

                records[dayIndex] = new AttendanceRecord
                {
                    AccountAttendance = reader.GetDateTimeOrMinValue("account_attendance"),
                    Accept = reader.GetBoolean("accept")
                };
            }

            _records = records;
        }
    }
}
