using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartedCinema2Packet() : GamePacket(CSOffsets.CSStartedCinema2Packet, 5)
{
    public override void Read(PacketStream stream)
    {
        // Empty struct
        // NOTE: Do NOT dismount/despawn vehicles on cinema packets. The client sends these (esp.
        // CSStartedCinema2 / opcode 0x071) very frequently - on map open (M), camera changes and other
        // UI actions, not only for real cutscenes - so dismounting here desummoned the player's mount on
        // every map-open. Runaway/orphaned-vehicle protection lives in CSMoveUnitPacket (out-of-bounds
        // movement guard + "invalid target while attached" recovery) instead.

        //Connection.ActiveChar.Events.OnCinemaStarted(Connection.ActiveChar, new OnCinemaStartedArgs() { CinemaId = Connection.ActiveChar.CurrentlyPlayingCinemaId });
    }
}
