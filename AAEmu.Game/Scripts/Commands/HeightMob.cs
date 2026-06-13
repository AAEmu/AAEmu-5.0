using System;
using System.Globalization;
using System.IO;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;

using NLog;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command /heightmob — diagnostic tool for NPC spawn-Z resolution.
///
/// Usage:
///   /heightmob target  — show spawn-Z resolution data for the targeted NPC
///   /heightmob csv     — append data to Data/Custom/heightmob.csv
/// </summary>
public class HeightMob : ICommand
{
    private static readonly string CsvPath = Path.Combine("Data", "Custom", "heightmob.csv");
    private static bool _csvHeaderWritten;
    private static readonly object _csvLock = new();

    /// <summary>
    /// Maximum CSV file size in bytes before rotation. Default: 10 MB.
    /// When exceeded, the current file is renamed to a timestamped backup and a new one is started.
    /// </summary>
    private const long MaxCsvFileSizeBytes = 10 * 1024 * 1024;

    public string[] CommandNames { get; set; } = new[] { "heightmob" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) [csv]";
    }

    public string GetCommandHelpText()
    {
        return "Shows NPC spawn-Z resolution diagnostics. Use 'csv' to log to file.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendNormalText(this, messageOutput, "Usage: /heightmob <target> [csv]");
            return;
        }

        var targetArg = args[0].ToLowerInvariant();
        var mode = args.Length > 1 ? args[1].ToLowerInvariant() : "show";

        // Find the target NPC
        Npc? targetNpc = null;

        // Try by objId or templateId
        if (uint.TryParse(targetArg, out var npcId))
        {
            targetNpc = WorldManager.Instance.GetAllNpcs().FirstOrDefault(n => n.ObjId == npcId || n.TemplateId == npcId);
        }

        if (targetNpc == null)
        {
            // Try by name
            targetNpc = WorldManager.Instance.GetAllNpcs().FirstOrDefault(n => n.Name?.Equals(targetArg, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (targetNpc == null)
        {
            CommandManager.SendNormalText(this, messageOutput, $"Target '{targetArg}' not found. Use NPC name, template ID, or objId.");
            return;
        }

        // Get diagnostic data
        if (!SpawnHeightDiagnostics.Instance.TryGet(targetNpc.ObjId, out var entry))
        {
            CommandManager.SendNormalText(this, messageOutput, $"No spawn-Z diagnostic data for NPC {targetNpc.ObjId}:{targetNpc.TemplateId}. The NPC may have been spawned before the patch was applied.");
            return;
        }

        var ci = CultureInfo.InvariantCulture;
        string F(float v) => float.IsNaN(v) ? "N/A" : v.ToString("F2", ci);

        CommandManager.SendNormalText(this, messageOutput,
            $"|cFFFFFFFFSpawn-Z diagnostics for NPC {targetNpc.ObjId}:{targetNpc.TemplateId} ({targetNpc.Name ?? "?"})|r");
        CommandManager.SendNormalText(this, messageOutput,
            $"  spawnPos=|cFFFFFFFF{entry.SpawnPos.X:F1},{entry.SpawnPos.Y:F1},{entry.SpawnPos.Z:F1}|r");
        CommandManager.SendNormalText(this, messageOutput,
            $"  originalSpawnZ=|cFFFFFFFF{entry.OriginalSpawnZ:F2}|r finalZ=|cFFFFFFFF{entry.FinalZ:F2}|r delta=|cFFFFFFFF{(entry.FinalZ - entry.OriginalSpawnZ):F2}|r");
        CommandManager.SendNormalText(this, messageOutput,
            $"  heightmapZ=|cFFFFFFFF{F(entry.HeightmapZ)}|r hasHmap=|cFFFFFFFF{entry.HasHeightmap}|r");
        CommandManager.SendNormalText(this, messageOutput,
            $"  baiZ=|cFFFFFFFF{F(entry.BaiZ)}|r baiD2D=|cFFFFFFFF{F(entry.BaiDistance2D)}|r baiType=|cFFFFFFFF{entry.BaiNodeType}|r baiSource=|cFFFFFFFF{entry.BaiSource}|r");
        CommandManager.SendNormalText(this, messageOutput,
            $"  decision=|cFFFFFFFF{entry.Decision}|r");

        // Current position comparison
        var currentZ = targetNpc.Transform.World.Position.Z;
        var currentDelta = currentZ - entry.FinalZ;
        CommandManager.SendNormalText(this, messageOutput,
            $"  currentZ=|cFFFFFFFF{currentZ:F2}|r currentDeltaFromResolved=|cFFFFFFFF{currentDelta:F2}|r");

        // CSV logging
        if (mode == "csv")
        {
            WriteCsv(targetNpc, entry);
            CommandManager.SendNormalText(this, messageOutput, $"  Data appended to {CsvPath}");
        }
    }

    private static void WriteCsv(Npc npc, SpawnHeightDiagnostics.Entry entry)
    {
        try
        {
            var dir = Path.GetDirectoryName(CsvPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var ci = CultureInfo.InvariantCulture;
            string F(float v) => float.IsNaN(v) ? "" : v.ToString("F2", ci);

            var currentZ = npc.Transform.World.Position.Z;
            var currentDelta = currentZ - entry.FinalZ;

            var line = string.Join(";", new[]
            {
                DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                npc.ObjId.ToString(),
                npc.TemplateId.ToString(),
                npc.Name ?? "",
                entry.SpawnerTemplateId.ToString(),
                F(entry.SpawnPos.X),
                F(entry.SpawnPos.Y),
                F(entry.SpawnPos.Z),
                F(entry.OriginalSpawnZ),
                F(entry.HeightmapZ),
                entry.HasHeightmap.ToString(),
                F(entry.BaiZ),
                F(entry.BaiDistance2D),
                entry.BaiNodeType.ToString(),
                entry.BaiSource ?? "",
                F(entry.FinalZ),
                (entry.FinalZ - entry.OriginalSpawnZ).ToString("F2", ci),
                entry.Decision ?? "",
                F(currentZ),
                currentDelta.ToString("F2", ci)
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
                        "spawnerTemplateId;" +
                        "spawnX;spawnY;spawnZ;" +
                        "originalSpawnZ;" +
                        "heightmapZ;hasHeightmap;" +
                        "baiZ;baiD2D;baiNodeType;baiSource;" +
                        "finalZ;delta;" +
                        "decision;" +
                        "currentZ;currentDelta";
                    File.WriteAllText(CsvPath, header + Environment.NewLine);
                    _csvHeaderWritten = true;
                }

                File.AppendAllText(CsvPath, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Error(ex, "Failed to write heightmob CSV");
        }
    }
}
