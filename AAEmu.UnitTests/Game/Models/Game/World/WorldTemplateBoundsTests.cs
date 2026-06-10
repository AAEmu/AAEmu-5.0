using AAEmu.Game.Models.Game.World;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.World;

/// <summary>
/// Тесты для проверки граничных условий WorldTemplate.
/// These tests verify the boundary checks that were fixed to prevent IndexOutOfRangeException.
/// </summary>
public class WorldTemplateBoundsTests
{
    private WorldTemplate CreateTemplate(int cellX = 2, int cellY = 2)
    {
        var template = new WorldTemplate
        {
            Id = 1,
            Name = "test_template",
            CellX = cellX,
            CellY = cellY,
            Cells = new WorldCell[cellX, cellY],
            HeightMaxCoefficient = 1.0f,
            OceanLevel = 0f
        };

        // Initialize cells without calling VerifyCellLoaded (which requires AppConfiguration)
        for (var x = 0; x < cellX; x++)
        {
            for (var y = 0; y < cellY; y++)
            {
                template.Cells[x, y] = new WorldCell(x, y, template);
            }
        }

        return template;
    }

    #region GetCell bounds tests

    [Fact]
    public void GetCell_ValidIndices_ReturnsCell()
    {
        var template = CreateTemplate(2, 2);

        var cell = template.GetCell(0, 0);

        Assert.NotNull(cell);
        Assert.Equal(0, cell.CellX);
        Assert.Equal(0, cell.CellY);
    }

    [Fact]
    public void GetCell_NegativeX_ReturnsNull()
    {
        var template = CreateTemplate(2, 2);

        var cell = template.GetCell(-1, 0);

        Assert.Null(cell);
    }

    [Fact]
    public void GetCell_NegativeY_ReturnsNull()
    {
        var template = CreateTemplate(2, 2);

        var cell = template.GetCell(0, -1);

        Assert.Null(cell);
    }

    [Fact]
    public void GetCell_XEqualToCellX_ReturnsNull()
    {
        var template = CreateTemplate(2, 2);

        // CellX = 2, so valid indices are 0 and 1
        // Index 2 should be out of bounds (fixed: was > CellX, now >= CellX)
        var cell = template.GetCell(2, 0);

        Assert.Null(cell);
    }

    [Fact]
    public void GetCell_YEqualToCellY_ReturnsNull()
    {
        var template = CreateTemplate(2, 2);

        // CellY = 2, so valid indices are 0 and 1
        // Index 2 should be out of bounds (fixed: was > CellY, now >= CellY)
        var cell = template.GetCell(0, 2);

        Assert.Null(cell);
    }

    [Fact]
    public void GetCell_BothCoordinatesOutOfBounds_ReturnsNull()
    {
        var template = CreateTemplate(2, 2);

        var cell = template.GetCell(5, 5);

        Assert.Null(cell);
    }

    #endregion

    #region ValidRegion tests

    [Fact]
    public void ValidRegion_ValidCoordinates_ReturnsTrue()
    {
        var template = CreateTemplate(2, 2);

        var valid = template.ValidRegion(0, 0);

        Assert.True(valid);
    }

    [Fact]
    public void ValidRegion_NegativeCoordinates_ReturnsFalse()
    {
        var template = CreateTemplate(2, 2);

        var valid = template.ValidRegion(-1, -1);

        Assert.False(valid);
    }

    [Fact]
    public void ValidRegion_BeyondBounds_ReturnsFalse()
    {
        var template = CreateTemplate(2, 2);

        // CellX * SECTORS_PER_CELL = 2 * 16 = 32, so 100 is out of bounds
        var valid = template.ValidRegion(100, 100);

        Assert.False(valid);
    }

    #endregion
}