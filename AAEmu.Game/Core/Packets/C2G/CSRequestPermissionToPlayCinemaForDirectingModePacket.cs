using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRequestPermissionToPlayCinemaForDirectingModePacket : GamePacket
{
    public CSRequestPermissionToPlayCinemaForDirectingModePacket() : base(CSOffsets.CSRequestPermissionToPlayCinemaForDirectingModePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var questContextId = stream.ReadUInt32();
        var npcObjId = stream.ReadBc();
        var doodadObjId = stream.ReadBc();

        Logger.Debug("CSRequestPermissionToPlayCinemaForDirectingModePacket questContextId={0}", questContextId);

        var character = Connection?.ActiveChar;
        if (character != null)
        {
            character.Events.OnCinemaStarted(character, new OnCinemaStartedArgs { CinemaId = character.CurrentlyPlayingCinemaId });
        }
    }
}
