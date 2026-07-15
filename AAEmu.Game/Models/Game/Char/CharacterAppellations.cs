using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterAppellations
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public List<uint> Appellations { get; set; }
    public uint ActiveAppellation { get; set; }
    public uint ActiveAppellationBuffId { get; set; }

    public Character Owner { get; set; }

    private readonly Dictionary<uint, uint> _appellationBuffIds;

    public CharacterAppellations(Character owner)
    {
        Owner = owner;
        Appellations = new List<uint>();
        ActiveAppellation = 0;
        ActiveAppellationBuffId = 0;
        _appellationBuffIds = new Dictionary<uint, uint>();
    }

    private void AddBuff(uint buffId)
    {
        if (buffId == 0)
        {
            Logger.Info("Skip appellation buff apply: buff is 0, owner {0}, active appellation {1}", Owner.Id, ActiveAppellation);
            return;
        }

        var effectBuffId = ResolveEffectBuffId(buffId);
        if (effectBuffId == 0)
            return;

        Logger.Info("Applying appellation buff {0}, effect buff {1}, owner {2}, active appellation {3}, objId {4}", buffId, effectBuffId, Owner.Id, ActiveAppellation, Owner.ObjId);
        Owner.Buffs.AddBuff(effectBuffId, Owner);
        Logger.Info("Applied appellation buff {0}, effect buff {1}, owner {2}, active appellation {3}, hasBuff {4}", buffId, effectBuffId, Owner.Id, ActiveAppellation, Owner.Buffs.CheckBuff(effectBuffId));
    }

    private void RemoveCurrentBuff()
    {
        if (ActiveAppellationBuffId == 0)
            return;

        var effectBuffId = ResolveEffectBuffId(ActiveAppellationBuffId);
        if (effectBuffId == 0)
            return;

        Owner.Buffs.RemoveBuff(effectBuffId);
        Logger.Info("Removing appellation buff {0}, effect buff {1}, owner {2}", ActiveAppellationBuffId, effectBuffId, Owner.Id);
    }

    public void ApplyActiveBuff()
    {
        Logger.Info("ApplyActiveBuff called: owner {0}, objId {1}, active appellation {2}, active buff {3}, available appellations {4}",
            Owner.Id, Owner.ObjId, ActiveAppellation, ActiveAppellationBuffId, Appellations.Count);

        var effectBuffId = ResolveEffectBuffId(ActiveAppellationBuffId);
        if (effectBuffId == 0)
            return;

        if (Owner.Buffs.CheckBuff(effectBuffId))
        {
            Logger.Info("Skip appellation buff apply: buff {0}, effect buff {1} already active, owner {2}, active appellation {3}",
                ActiveAppellationBuffId, effectBuffId, Owner.Id, ActiveAppellation);
            return;
        }

        AddBuff(ActiveAppellationBuffId);
    }

    public void SendActiveAppellation()
    {
        Logger.Info("Sending active appellation state: owner {0}, objId {1}, active appellation {2}, active buff {3}",
            Owner.Id, Owner.ObjId, ActiveAppellation, ActiveAppellationBuffId);
        Owner.BroadcastPacket(new SCAppellationChangedPacket(Owner.ObjId, ActiveAppellation, ActiveAppellationBuffId), true);
    }

    public uint GetActivePassiveBuffIdForClient()
    {
        if (ActiveAppellationBuffId == 0)
            return 0;

        try
        {
            var passiveBuffTemplate = SkillManager.Instance.GetPassiveBuffTemplate(ActiveAppellationBuffId);
            if (passiveBuffTemplate is null)
                return 0;

            if (passiveBuffTemplate.BuffId == 0 || SkillManager.Instance.GetBuffTemplate(passiveBuffTemplate.BuffId) is null)
                return 0;

            return ActiveAppellationBuffId;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Unable to inspect active appellation passive buff {0}, owner {1}, active appellation {2}",
                ActiveAppellationBuffId, Owner.Id, ActiveAppellation);
            return 0;
        }
    }

    private void AddInternal(uint id, uint buffId)
    {
        Appellations.Add(id);
        _appellationBuffIds[id] = buffId;
    }

    private uint ResolveAppellationBuffId(uint appellationId)
    {
        if (_appellationBuffIds.TryGetValue(appellationId, out var buffId))
            return buffId;

        return CharacterManager.Instance.GetAppellationsTemplate(appellationId)?.BuffId ?? 0;
    }

    private uint ResolveEffectBuffId(uint buffId)
    {
        if (buffId == 0)
            return 0;

        try
        {
            var buffTemplate = SkillManager.Instance.GetBuffTemplate(buffId);
            if (buffTemplate is not null)
                return buffId;

            var passiveBuffTemplate = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
            if (passiveBuffTemplate is null)
            {
                Logger.Warn("Skip appellation buff apply: buff {0} not found as buff or passive buff, owner {1}, active appellation {2}", buffId, Owner.Id, ActiveAppellation);
                return 0;
            }

            if (passiveBuffTemplate.BuffId == 0)
            {
                Logger.Warn("Skip appellation buff apply: passive buff {0} has no effect buff, owner {1}, active appellation {2}", buffId, Owner.Id, ActiveAppellation);
                return 0;
            }

            if (SkillManager.Instance.GetBuffTemplate(passiveBuffTemplate.BuffId) is null)
            {
                Logger.Warn("Skip appellation buff apply: passive buff {0} points to missing effect buff {1}, owner {2}, active appellation {3}",
                    buffId, passiveBuffTemplate.BuffId, Owner.Id, ActiveAppellation);
                return 0;
            }

            Logger.Info("Resolved appellation buff {0} through passive buff template to effect buff {1}, owner {2}, active appellation {3}",
                buffId, passiveBuffTemplate.BuffId, Owner.Id, ActiveAppellation);
            return passiveBuffTemplate.BuffId;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Unable to inspect appellation buff template {0}, owner {1}, active appellation {2}; applying raw buff id", buffId, Owner.Id, ActiveAppellation);
            return buffId;
        }
    }

    private bool IsAvailableBuff(uint buffId)
    {
        if (buffId == 0)
            return true;

        for (var i = 0; i < Appellations.Count; i++)
        {
            if (ResolveAppellationBuffId(Appellations[i]) == buffId)
                return true;
        }

        return false;
    }

    public void Add(uint id)
    {
        if (Appellations.Contains(id))
        {
            Logger.Warn("Duplicate add {0}, ownerId {1}", id, Owner.Id);
            return;
        }

        var buffId = CharacterManager.Instance.GetAppellationsTemplate(id)?.BuffId ?? 0;
        AddInternal(id, buffId);
        Owner.SendPacket(new SCAppellationGainedPacket(id));
    }

    internal void Change(uint appellationId, uint appellationBuffId)
    {
        if (appellationId == 0)
        {
            RemoveCurrentBuff();
            ActiveAppellation = 0;
            ActiveAppellationBuffId = 0;
        }
        else
        {
            if (Appellations.Contains(appellationId))
            {
                if (!IsAvailableBuff(appellationBuffId))
                {
                    Logger.Warn("Buff {0} isn't available for owner {1}, appellation {2}", appellationBuffId, Owner.Id, appellationId);
                    Owner.BroadcastPacket(new SCAppellationChangedPacket(Owner.ObjId, ActiveAppellation, ActiveAppellationBuffId), true);
                    return;
                }

                RemoveCurrentBuff();
                ActiveAppellation = appellationId;
                ActiveAppellationBuffId = appellationBuffId;
                AddBuff(appellationBuffId);
            }
            else
            {
                Logger.Warn("Id {0}, buff {1} doesn't exist, owner {2}", appellationId, appellationBuffId, Owner.Id);
            }
        }

        Owner.BroadcastPacket(new SCAppellationChangedPacket(Owner.ObjId, ActiveAppellation, ActiveAppellationBuffId), true);
    }

    public void Send()
    {
        for (var i = 0; i < Appellations.Count; i += 512)
        {
            var result = new (uint, bool)[Appellations.Count - i <= 512 ? Appellations.Count - i : 512];

            for (var j = 0; j < result.Length; j++)
                result[j] = (Appellations[i + j], Appellations[i + j] == ActiveAppellation);

            Owner.SendPacket(new SCAppellationsPacket(result));
        }
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, active, buff_id FROM appellations WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var id = reader.GetUInt32("id");
                    var active = reader.GetBoolean("active");
                    var buffId = reader.GetUInt32("buff_id");

                    AddInternal(id, buffId);
                    Logger.Info("Loaded appellation row: owner {0}, appellation {1}, active {2}, buff {3}", Owner.Id, id, active, buffId);
                    if (active)
                    {
                        ActiveAppellation = id;
                        ActiveAppellationBuffId = buffId;
                        Logger.Info("Loaded active appellation: owner {0}, appellation {1}, buff {2}", Owner.Id, ActiveAppellation, ActiveAppellationBuffId);
                    }
                }
            }
        }

        Logger.Info("Loaded appellations summary: owner {0}, count {1}, active appellation {2}, active buff {3}",
            Owner.Id, Appellations.Count, ActiveAppellation, ActiveAppellationBuffId);
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        foreach (var appellation in Appellations)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO appellations(`id`,`active`,`owner`,`buff_id`) VALUES (@id, @active, @owner, @buff_id)";
                command.Parameters.AddWithValue("@id", appellation);
                command.Parameters.AddWithValue("@active", ActiveAppellation == appellation);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Parameters.AddWithValue("@buff_id", ActiveAppellation == appellation ? ActiveAppellationBuffId : ResolveAppellationBuffId(appellation));
                command.ExecuteNonQuery();
            }
        }
    }
}
