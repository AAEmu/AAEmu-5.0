using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models;

public partial class AppConfiguration
{
    public NpcPathfindingConfig NpcPathfinding { get; set; } = new();
}
