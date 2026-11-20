using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCRankerInformationPacket : GamePacket
    {
        //private readonly byte _race;
        //private readonly byte _ability1;
        //private readonly byte _ability2;
        //private readonly byte _ability3;
        //private readonly int _health;
        //private readonly int _mana;
        //private readonly byte _lvl;
        //private readonly uint _gearScore;
        //private readonly uint _leadership;
        //private readonly uint _pvpHonorPoint;
        //private readonly uint _pvpKillCount;
        //private readonly string _expeditionName;

        public SCRankerInformationPacket(byte worldId, uint type)
            : base(SCOffsets.SCRankerInformationPacket, 5)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            return stream;
        }
    }
}
