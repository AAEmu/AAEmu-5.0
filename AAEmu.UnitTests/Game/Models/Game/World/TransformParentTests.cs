using System.Numerics;

using AAEmu.Game.Models.Game.World.Transform;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class TransformParentTests
{
    [Fact]
    public void AttachingNearBoardPreservesWorldPosition()
    {
        var position = new Vector3(15647, 14996, 140);
        using var rider = new Transform(null, null, position);
        using var board = new Transform(null, null, 15648.75f, 14996.67f, 139.7f, 0, 0, MathF.PI / 2);

        rider.Parent = board;

        Assert.InRange(Vector3.Distance(position, rider.World.Position), 0, 0.01f);
        Assert.InRange(rider.Local.Position.Length(), 0, 2f);
    }
}
