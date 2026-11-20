using System;
using System.Numerics;
using System.Threading.Tasks;
using AAEmu.Game.Models.Game.NavMesh;
using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.NavMesh;

public class NavigationMeshCollectorTests
{
    [Fact]
    public async Task GetTrianglesInRadius_PointInsideTriangle_ReturnsTriangle()
    {
        // Arrange
        using var collector = new NavigationMeshCollector();
        uint zoneId = 1;
        var triangle = new NavMeshTriangle
        {
            ZoneId = zoneId,
            A = new Vector3(0, 0, 0),
            B = new Vector3(10, 0, 0),
            C = new Vector3(5, 10, 0)
        };
        collector.AddSample(zoneId, triangle.A, triangle.B, triangle.C);
        await collector.ForceCompactionAsync();

        // Act - проверяем точку внутри треугольника
        var center = new Vector3(5, 5, 0);
        var radius = 1.0f;
        var result = collector.GetTrianglesInRadius(zoneId, center, radius);

        // Assert
        Assert.Single(result);
        var resultTri = result[0];
        Assert.Equal(zoneId, resultTri.ZoneId);
        Assert.Equal(triangle.A, resultTri.A);
        Assert.Equal(triangle.B, resultTri.B);
        Assert.Equal(triangle.C, resultTri.C);
    }

    [Fact]
    public async Task GetTrianglesInRadius_PointNearVertex_ReturnsTriangle()
    {
        // Arrange
        using var collector = new NavigationMeshCollector();
        uint zoneId = 1;
        var triangle = new NavMeshTriangle
        {
            ZoneId = zoneId,
            A = new Vector3(0, 0, 0),
            B = new Vector3(10, 0, 0),
            C = new Vector3(5, 10, 0)
        };
        collector.AddSample(zoneId, triangle.A, triangle.B, triangle.C);
        await collector.ForceCompactionAsync();

        // Act - проверяем точку рядом с вершиной
        var center = new Vector3(0.5f, 0.5f, 0);
        var radius = 1.0f;
        var result = collector.GetTrianglesInRadius(zoneId, center, radius);

        // Assert
        Assert.Single(result);
        var resultTri = result[0];
        Assert.Equal(zoneId, resultTri.ZoneId);
    }

    [Fact]
    public async Task GetTrianglesInRadius_DifferentZLevels_ReturnsCorrectTriangle()
    {
        // Arrange
        using var collector = new NavigationMeshCollector();
        uint zoneId = 1;

        // Треугольник на высоте 0
        collector.AddSample(zoneId,
            new Vector3(0, 0, 0),
            new Vector3(10, 0, 0),
            new Vector3(5, 10, 0));

        // Треугольник на высоте 10
        collector.AddSample(zoneId,
            new Vector3(0, 0, 10),
            new Vector3(10, 0, 10),
            new Vector3(5, 10, 10));

        await collector.ForceCompactionAsync();

        // Act - проверяем точку на уровне z=10
        var center = new Vector3(5, 5, 10);
        var radius = 1.0f;
        var result = collector.GetTrianglesInRadius(zoneId, center, radius);

        // Assert - оба треугольника должны попасть в результат (по XY), но один из них на нужной высоте
        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.A.Z == 10f && t.B.Z == 10f && t.C.Z == 10f);
        Assert.Contains(result, t => t.A.Z == 0f && t.B.Z == 0f && t.C.Z == 0f);
    }

    [Fact]
    public async Task GetTrianglesInRadius_AdjacentTriangles_ReturnsCompactedResult()
    {
        // Arrange
        using var collector = new NavigationMeshCollector();
        uint zoneId = 1;

        // Добавляем два смежных треугольника на одной высоте
        collector.AddSample(zoneId,
            new Vector3(0, 0, 0),
            new Vector3(5, 0, 0),
            new Vector3(0, 5, 0));

        collector.AddSample(zoneId,
            new Vector3(5, 0, 0),
            new Vector3(5, 5, 0),
            new Vector3(0, 5, 0));

        await collector.ForceCompactionAsync();

        // Act - проверяем точку между треугольниками
        var center = new Vector3(2.5f, 2.5f, 0);
        var radius = 1.0f;
        var result = collector.GetTrianglesInRadius(zoneId, center, radius);

        // Assert - из-за компактификации может быть возвращен один объединенный треугольник
        Assert.True(result.Count >= 1);
        foreach (var tri in result)
        {
            Assert.Equal(zoneId, tri.ZoneId);
            Assert.Equal(0f, tri.A.Z);
            Assert.Equal(0f, tri.B.Z);
            Assert.Equal(0f, tri.C.Z);
        }
    }

    [Fact]
    public async Task GetTrianglesInRadius_PointOutside_ReturnsEmpty()
    {
        // Arrange
        using var collector = new NavigationMeshCollector();
        uint zoneId = 1;
        var triangle = new NavMeshTriangle
        {
            ZoneId = zoneId,
            A = new Vector3(0, 0, 0),
            B = new Vector3(10, 0, 0),
            C = new Vector3(5, 10, 0)
        };
        collector.AddSample(zoneId, triangle.A, triangle.B, triangle.C);
        await collector.ForceCompactionAsync();

        // Act - проверяем точку далеко от треугольника
        var center = new Vector3(100, 100, 0);
        var radius = 1.0f;
        var result = collector.GetTrianglesInRadius(zoneId, center, radius);

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(0)] // нулевой радиус
    [InlineData(-1.0f)] // отрицательный радиус
    public void GetTrianglesInRadius_InvalidRadius_ThrowsArgumentException(float radius)
    {
        // Arrange
        using var collector = new NavigationMeshCollector();
        var center = new Vector3(0, 0, 0);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => collector.GetTrianglesInRadius(1, center, radius));
    }

    [Fact]
    public async Task GetTrianglesInRadius_DifferentZones_ReturnsOnlyRequestedZone()
    {
        // Arrange
        using var collector = new NavigationMeshCollector();

        // Добавляем треугольники в разные зоны
        collector.AddSample(1,
            new Vector3(0, 0, 0),
            new Vector3(10, 0, 0),
            new Vector3(5, 10, 0));

        collector.AddSample(2,
            new Vector3(0, 0, 0),
            new Vector3(10, 0, 0),
            new Vector3(5, 10, 0));

        await collector.ForceCompactionAsync();

        // Act
        var center = new Vector3(5, 5, 0);
        var radius = 1.0f;
        var result = collector.GetTrianglesInRadius(1, center, radius);

        // Assert
        Assert.Single(result);
        Assert.Equal(1u, result[0].ZoneId);
    }
}
