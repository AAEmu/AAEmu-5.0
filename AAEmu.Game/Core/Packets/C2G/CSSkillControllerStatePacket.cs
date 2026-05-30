using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills.SkillControllers;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSkillControllerStatePacket : GamePacket
{
    public CSSkillControllerStatePacket() : base(CSOffsets.CSSkillControllerStatePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var scType = stream.ReadByte();
        float? len = null;
        bool? teared = null;
        bool? cutouted = null;
        if (scType == 0)
        {
            len = stream.ReadSingle();
            teared = stream.ReadBoolean();
            cutouted = stream.ReadBoolean();
        }

        if (Connection.ActiveChar != null && scType == 0 && len.HasValue && teared.HasValue && cutouted.HasValue)
            ShipHarpoonRopeController.TryApplySkillControllerState(Connection.ActiveChar, objId, len.Value, teared.Value, cutouted.Value);
    }
}
