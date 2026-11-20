using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

using NLog;

#nullable enable annotations

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command /pathmob — diagnostic tool for NPC combat pathfinding.
///
/// Usage:
///   /pathmob target          — show current path to target
///   /pathmob target repath   — force a repath and show result
///   /pathmob csv             — append current path data to Data/Custom/pathmob.csv
/// </summary>
public class PathMob : ICommand
{
    private static readonly string CsvPath = Path.Combine("Data", "Custom", "pathmob.csv");
    private static bool _csvHeaderWritten;
    private static readonly object _csvLock = new();

    /// <summary>
    /// Maximum CSV file size in bytes before rotation. Default: 10 MB.
    /// When exceeded, the current file is renamed to a timestamped backup and a new one is started.
    /// </summary>
    private const long MaxCsvFileSizeBytes = 10 * 1024 * 1024;

    public string[] CommandNames { get; set; } = new[] { "pathmob" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) [repath|csv]";
    }

    public string GetCommandHelpText()
    {
        return "Shows NPC combat pathfinding diagnostics. Use 'repath' to force path recalculation, 'csv' to log to file.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendNormalText(this, messageOutput, "Usage: /pathmob <target> [repath|csv]");
            return;
        }

        var targetArg = args[0].ToLowerInvariant();
        var mode = args.Length > 1 ? args[1].ToLowerInvariant() : "show";

        // Find the target NPC
        Npc? targetNpc = null;

        // Try by name first
        var player = WorldManager.Instance.GetCharacter(targetArg);
        if (player != null)
        {
            // Target is a player — find an NPC that has them as aggro target
            foreach (var npc in WorldManager.Instance.GetAllNpcs())
            {
                if (npc.Ai?.Owner?.CurrentAggroTarget == player || npc.CurrentAggroTarget == player)
                {
                    targetNpc = npc;
                    break;
                }
            }

            if (targetNpc == null)
            {
                CommandManager.SendNormalText(this, messageOutput, $"No NPC found targeting player '{targetArg}'.");
                return;
            }
        }
        else
        {
            // Try by NPC template ID or objId
            if (uint.TryParse(targetArg, out var npcId))
            {
                targetNpc = WorldManager.Instance.GetAllNpcs().FirstOrDefault(n => n.ObjId == npcId || n.TemplateId == npcId);
            }

            if (targetNpc == null)
            {
                // Try by name
                targetNpc = WorldManager.Instance.GetAllNpcs().FirstOrDefault(n => n.Name?.Equals(targetArg, StringComparison.OrdinalIgnoreCase) == true);
            }
        }

        if (targetNpc == null)
        {
            CommandManager.SendNormalText(this, messageOutput, $"Target '{targetArg}' not found. Use NPC name, template ID, or objId.");
            return;
        }

        var pn = targetNpc.Ai?.PathNode;
        if (pn == null)
        {
            CommandManager.SendNormalText(this, messageOutput, $"NPC {targetNpc.ObjId}:{targetNpc.TemplateId} has no PathNode.");
            return;
        }

        // Force repath if requested
        if (mode == "repath")
        {
            if (targetNpc.CurrentAggroTarget is Unit aggroTarget)
            {
                targetNpc.FindPath(aggroTarget);
                CommandManager.SendNormalText(this, messageOutput, $"Repath triggered for NPC {targetNpc.ObjId}:{targetNpc.TemplateId}.");
            }
            else
            {
                CommandManager.SendNormalText(this, messageOutput, $"NPC {targetNpc.ObjId}:{targetNpc.TemplateId} has no aggro target to repath to.");
                return;
            }
        }

        // Show diagnostics
        var ageMs = (DateTime.UtcNow - pn.LastRepathTime).TotalMilliseconds;
        var status = pn.LastStatus;

        CommandManager.SendNormalText(this, messageOutput,
            $"|cFFFFFFFFPathfinding diagnostics for NPC {targetNpc.ObjId}:{targetNpc.TemplateId}|r");
        CommandManager.SendNormalText(this, messageOutput,
            $"  status=|cFFFFFFFF{status}|r raw=|cFFFFFFFF{pn.LastRawNodeCount}|r reduced=|cFFFFFFFF{pn.FoundPath?.Count ?? 0}|r skippedFirst=|cFFFFFFFF{(pn.LastStartNodeSkipped ? "yes" : "no")}|r ageMs=|cFFFFFFFF{(long)ageMs}|r");

        // v6: optimality ratio echo. Green ~1.0-1.2 (near-straight), yellow up to 1.5,
        // red beyond (real detour worth investigating).
        if (pn.LastOptimalDistance > 0.01f)
        {
            var ratio = pn.LastPathLength / pn.LastOptimalDistance;
            var col = ratio <= 1.2f ? "FFA0FFA0" : ratio <= 1.5f ? "FFFFFF80" : "FFFF8080";
            CommandManager.SendNormalText(this, messageOutput,
                $"  pathLen=|cFFFFFFFF{pn.LastPathLength:F1}|r optimal=|cFFFFFFFF{pn.LastOptimalDistance:F1}|r ratio=|c{col}{ratio:F2}|r");
        }

        // Show waypoints
        if (pn.FoundPath != null && pn.FoundPath.Count > 0)
        {
            var wps = pn.FoundPath.Take(5).ToArray();
            for (var i = 0; i < wps.Length; i++)
            {
                var wp = wps[i];
                var dist = Vector3.Distance(targetNpc.Transform.World.Position, wp);
                CommandManager.SendNormalText(this, messageOutput,
                    $"  wp{i + 1}=|cFFFFFFFF{wp.X:F1},{wp.Y:F1},{wp.Z:F1}|r dist=|cFFFFFFFF{dist:F1}|r");
            }
            if (pn.FoundPath.Count > 5)
                CommandManager.SendNormalText(this, messageOutput, $"  ... and {pn.FoundPath.Count - 5} more waypoints");
        }

        // CSV logging
        if (mode == "csv")
        {
            WriteCsv(targetNpc, pn);
            CommandManager.SendNormalText(this, messageOutput, $"  Data appended to {CsvPath}");
        }
    }

    private static void WriteCsv(Npc npc, PathNode pn)
    {
        try
        {
            var dir = Path.GetDirectoryName(CsvPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var ci = CultureInfo.InvariantCulture;
            string F(float v) => float.IsNaN(v) ? "" : v.ToString("F2", ci);

            // v6: optimality ratio = actual A* path length / crow-flies start→goal distance.
            var pathLen = pn.LastPathLength;
            var optimalDist = pn.LastOptimalDistance;
            var ratioStr = optimalDist > 0.01f ? (pathLen / optimalDist).ToString("F3", ci) : "";

            var wps = pn.FoundPath?.Take(5).ToArray() ?? Array.Empty<Vector3>();
            var wpDists = wps.Select(wp => Vector3.Distance(npc.Transform.World.Position, wp)).ToArray();

            var line = string.Join(";", new[]
            {
                DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                npc.ObjId.ToString(),
                npc.TemplateId.ToString(),
                npc.Name ?? "",
                F(npc.Transform.World.Position.X),
                F(npc.Transform.World.Position.Y),
                F(npc.Transform.World.Position.Z),
                F(pn.LastStartReal.X),
                F(pn.LastStartReal.Y),
                F(pn.LastStartReal.Z),
                F(pn.LastStartNodePos.X),
                F(pn.LastStartNodePos.Y),
                F(pn.LastStartNodePos.Z),
                F(pn.LastEndReal.X),
                F(pn.LastEndReal.Y),
                F(pn.LastEndReal.Z),
                F(pn.LastEndNodePos.X),
                F(pn.LastEndNodePos.Y),
                F(pn.LastEndNodePos.Z),
                pn.LastStatus.ToString(),
                pn.LastRepathReason ?? string.Empty,
                ((long)Math.Round((DateTime.UtcNow - pn.LastRepathTime).TotalMilliseconds)).ToString(ci),
                F(pathLen),
                F(optimalDist),
                ratioStr,
                wps.Length > 0 ? F(wps[0].X) : "", wps.Length > 0 ? F(wps[0].Y) : "", wps.Length > 0 ? F(wps[0].Z) : "", wps.Length > 0 ? F(wpDists[0]) : "",
                wps.Length > 1 ? F(wps[1].X) : "", wps.Length > 1 ? F(wps[1].Y) : "", wps.Length > 1 ? F(wps[1].Z) : "", wps.Length > 1 ? F(wpDists[1]) : "",
                wps.Length > 2 ? F(wps[2].X) : "", wps.Length > 2 ? F(wps[2].Y) : "", wps.Length > 2 ? F(wps[2].Z) : "", wps.Length > 2 ? F(wpDists[2]) : "",
                wps.Length > 3 ? F(wps[3].X) : "", wps.Length > 3 ? F(wps[3].Y) : "", wps.Length > 3 ? F(wps[3].Z) : "", wps.Length > 3 ? F(wpDists[3]) : "",
                wps.Length > 4 ? F(wps[4].X) : "", wps.Length > 4 ? F(wps[4].Y) : "", wps.Length > 4 ? F(wps[4].Z) : "", wps.Length > 4 ? F(wpDists[4]) : "",
                ""
            });

            lock (_csvLock)
            {
                // Rotate the file if it exceeds the maximum size.
                if (File.Exists(CsvPath))
                {
                    var fileInfo = new FileInfo(CsvPath);
                    if (fileInfo.Length > MaxCsvFileSizeBytes)
                    {
                        var backupPath = Path.Combine(
                            Path.GetDirectoryName(CsvPath)!,
                            Path.GetFileNameWithoutExtension(CsvPath) + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".csv");
                        File.Move(CsvPath, backupPath);
                        _csvHeaderWritten = false;
                    }
                }

                if (!_csvHeaderWritten || !File.Exists(CsvPath))
                {
                    var header = "timestamp;npcObjId;npcTemplateId;npcName;" +
                        "npcX;npcY;npcZ;" +
                        "startRealX;startRealY;startRealZ;" +
                        "startNodeX;startNodeY;startNodeZ;" +
                        "endRealX;endRealY;endRealZ;" +
                        "endNodeX;endNodeY;endNodeZ;" +
                        "status;repathReason;ageMs;" +
                        "pathLen;optimalDist;ratio;" +
                        "wp1X;wp1Y;wp1Z;wp1D;wp2X;wp2Y;wp2Z;wp2D;wp3X;wp3Y;wp3Z;wp3D;wp4X;wp4Y;wp4Z;wp4D;wp5X;wp5Y;wp5Z;wp5D;" +
                        "visual";
                    File.WriteAllText(CsvPath, header + Environment.NewLine);
                    _csvHeaderWritten = true;
                }

                File.AppendAllText(CsvPath, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Error(ex, "Failed to write pathmob CSV");
        }
    }
}
