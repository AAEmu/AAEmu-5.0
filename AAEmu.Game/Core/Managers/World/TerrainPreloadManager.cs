using System;
using System.Collections.Generic;
using System.Threading;

using AAEmu.Commons.Utils;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Preloads terrain cells around online players on a background thread.
/// </summary>
public class TerrainPreloadManager : Singleton<TerrainPreloadManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const int PreloadRadiusCells = 2;
    private const int SleepMs = 500;

    private Thread _thread;
    private volatile bool _running;

    public void Start()
    {
        if (_running)
            return;
        _running = true;
        _thread = new Thread(Loop) { Name = "TerrainPreload", IsBackground = true };
        _thread.Start();
        Logger.Info("TerrainPreloadManager started (radius {0} cells, every {1}ms)", PreloadRadiusCells, SleepMs);
    }

    public void Stop()
    {
        _running = false;
    }

    private void Loop()
    {
        while (_running)
        {
            try
            {
                PreloadAroundPlayers();
            }
            catch (Exception e)
            {
                Logger.Warn(e, "TerrainPreloadManager: preload pass failed");
            }

            Thread.Sleep(SleepMs);
        }
    }

    private void PreloadAroundPlayers()
    {
        var characters = WorldManager.Instance.GetAllCharacters();
        if (characters == null || characters.Count == 0)
            return;

        var visited = new HashSet<(uint, int, int)>();

        foreach (var character in characters)
        {
            if (character?.Transform == null)
                continue;

            var worldId = character.Transform.WorldId;
            var world = WorldManager.Instance.GetWorld(worldId);
            var template = world?.Template;
            var cells = template?.Cells;
            if (cells == null)
                continue;

            var maxX = cells.GetLength(0);
            var maxY = cells.GetLength(1);

            var pos = character.Transform.World.Position;
            var centerX = (int)(pos.X / WorldManager.CELL_SIZE);
            var centerY = (int)(pos.Y / WorldManager.CELL_SIZE);

            for (var dx = -PreloadRadiusCells; dx <= PreloadRadiusCells; dx++)
            for (var dy = -PreloadRadiusCells; dy <= PreloadRadiusCells; dy++)
            {
                var cx = centerX + dx;
                var cy = centerY + dy;
                if (cx < 0 || cy < 0 || cx >= maxX || cy >= maxY)
                    continue;
                if (!visited.Add((worldId, cx, cy)))
                    continue;

                var cell = cells[cx, cy];
                if (cell == null || cell.Loaded)
                    continue;

                cell.VerifyCellLoaded();
            }
        }
    }
}
