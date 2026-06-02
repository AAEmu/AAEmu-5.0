using System;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Game.NPChar;

public static class NpcDoodadObstacleClassifier
{
    public static bool TryGetNpcAvoidanceRadius(
        Doodad doodad,
        NpcPathfindingConfig config,
        out float radius)
    {
        radius = 0f;

        if (doodad?.Template == null)
            return false;

        var template = doodad.Template;
        if (template.NoCollision)
            return false;

        if (config.RequireDoodadVehicleCollision && !template.CollideVehicle)
            return false;

        if (HasIgnoredModelKeyword(null, config.IgnoredDoodadModelKeywords))
            return false;

        if (!TryGetExplicitAvoidanceRadius(doodad, config, out radius))
        {
            if (config.IgnoreDoodadsWithoutCollisionGeometry)
                return false;

            radius = config.DefaultDoodadAvoidanceRadius;
        }

        radius += config.CollisionPadding;

        return radius >= config.MinDoodadBlockingRadius;
    }

    private static bool TryGetExplicitAvoidanceRadius(
        Doodad doodad,
        NpcPathfindingConfig config,
        out float radius)
    {
        radius = 0f;

        var simRadius = doodad.Template?.SimRadius ?? 0;
        if (simRadius > 0)
        {
            var scale = MathF.Max(0.01f, doodad.Scale);
            radius = simRadius * config.DoodadSimRadiusScale * scale;
            return radius > 0f;
        }

        if (doodad.ModelSize > 0f)
        {
            radius = doodad.ModelSize;
            return true;
        }

        return false;
    }

    private static bool HasIgnoredModelKeyword(string model, string[] ignoredKeywords)
    {
        if (string.IsNullOrWhiteSpace(model) || ignoredKeywords == null || ignoredKeywords.Length == 0)
            return false;

        foreach (var keyword in ignoredKeywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                continue;

            if (model.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
