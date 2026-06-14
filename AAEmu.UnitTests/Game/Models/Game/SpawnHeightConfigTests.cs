using AAEmu.Game.Models.Game;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game;

/// <summary>
/// Unit tests for <see cref="SpawnHeightConfig"/> default values and property setters.
/// </summary>
public class SpawnHeightConfigTests
{
    [Fact]
    public void Defaults_AreConservative()
    {
        var config = new SpawnHeightConfig();

        Assert.True(config.Enabled);
        Assert.Equal(6.0f, config.MaxBaiDistance2D);
        Assert.Equal(3.0f, config.MaxBaiVsHeightmapDelta);
        Assert.Equal(5.0f, config.MaxSnapDistance);
        Assert.Equal(1.5f, config.UnmeshedStructureThreshold);
        Assert.Equal(0.5f, config.MaxDownwardSnapDistance);
        Assert.Equal(3.0f, config.SpawnHomeXyRadius);
        Assert.Equal(4.0f, config.SpawnHomeBlendBand);
        Assert.True(config.PreferHeightmapForGroundZ);
        Assert.False(config.LogResolution);
        Assert.False(config.UseLegacyBai3DLookup);
        Assert.False(config.TrustBaiNearSpawnZ);
    }

    [Fact]
    public void Enabled_CanBeDisabled()
    {
        var config = new SpawnHeightConfig { Enabled = false };
        Assert.False(config.Enabled);
    }

    [Fact]
    public void MaxBaiDistance2D_CanBeSet()
    {
        var config = new SpawnHeightConfig { MaxBaiDistance2D = 10.0f };
        Assert.Equal(10.0f, config.MaxBaiDistance2D);
    }

    [Fact]
    public void MaxBaiVsHeightmapDelta_CanBeSet()
    {
        var config = new SpawnHeightConfig { MaxBaiVsHeightmapDelta = 5.0f };
        Assert.Equal(5.0f, config.MaxBaiVsHeightmapDelta);
    }

    [Fact]
    public void MaxSnapDistance_CanBeSet()
    {
        var config = new SpawnHeightConfig { MaxSnapDistance = 10.0f };
        Assert.Equal(10.0f, config.MaxSnapDistance);
    }

    [Fact]
    public void UnmeshedStructureThreshold_CanBeSet()
    {
        var config = new SpawnHeightConfig { UnmeshedStructureThreshold = 2.0f };
        Assert.Equal(2.0f, config.UnmeshedStructureThreshold);
    }

    [Fact]
    public void MaxDownwardSnapDistance_CanBeSet()
    {
        var config = new SpawnHeightConfig { MaxDownwardSnapDistance = 1.0f };
        Assert.Equal(1.0f, config.MaxDownwardSnapDistance);
    }

    [Fact]
    public void SpawnHomeXyRadius_CanBeSet()
    {
        var config = new SpawnHeightConfig { SpawnHomeXyRadius = 5.0f };
        Assert.Equal(5.0f, config.SpawnHomeXyRadius);
    }

    [Fact]
    public void SpawnHomeBlendBand_CanBeSet()
    {
        var config = new SpawnHeightConfig { SpawnHomeBlendBand = 6.0f };
        Assert.Equal(6.0f, config.SpawnHomeBlendBand);
    }

    [Fact]
    public void PreferHeightmapForGroundZ_CanBeDisabled()
    {
        var config = new SpawnHeightConfig { PreferHeightmapForGroundZ = false };
        Assert.False(config.PreferHeightmapForGroundZ);
    }

    [Fact]
    public void LogResolution_CanBeEnabled()
    {
        var config = new SpawnHeightConfig { LogResolution = true };
        Assert.True(config.LogResolution);
    }

    [Fact]
    public void UseLegacyBai3DLookup_CanBeEnabled()
    {
        var config = new SpawnHeightConfig { UseLegacyBai3DLookup = true };
        Assert.True(config.UseLegacyBai3DLookup);
    }

    [Fact]
    public void TrustBaiNearSpawnZ_CanBeEnabled()
    {
        var config = new SpawnHeightConfig { TrustBaiNearSpawnZ = true };
        Assert.True(config.TrustBaiNearSpawnZ);
    }

    [Fact]
    public void SpawnHomeXyRadius_ZeroDisablesGuard()
    {
        // Setting to 0 disables the near-home guard entirely
        var config = new SpawnHeightConfig { SpawnHomeXyRadius = 0f };
        Assert.Equal(0f, config.SpawnHomeXyRadius);
    }

    [Fact]
    public void SpawnHomeBlendBand_ZeroRestoresHardCut()
    {
        // Setting to 0 restores V5's hard binary cut
        var config = new SpawnHeightConfig { SpawnHomeBlendBand = 0f };
        Assert.Equal(0f, config.SpawnHomeBlendBand);
    }

    [Fact]
    public void MaxDownwardSnapDistance_LargeValueDisablesGuard()
    {
        // Setting to a very large value effectively disables the downward snap guard
        var config = new SpawnHeightConfig { MaxDownwardSnapDistance = 99f };
        Assert.Equal(99f, config.MaxDownwardSnapDistance);
    }
}
