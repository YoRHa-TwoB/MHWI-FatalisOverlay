using System.IO;
using System.Text.Json;
using FatalisOverlay.Models;

namespace FatalisOverlay.Services;

/// <summary>
/// Writes THK cycle data to the same JSONL file as battle log entries.
/// Type: "thk_cycle". Matching to action entries happens in LogSession.Parse().
/// </summary>
public static class ThkCycleLogger
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Write all pending THK cycles to the battle log writer as "thk_cycle" entries.
    /// </summary>
    public static void LogCycles(List<ThkCycleData> cycles, StreamWriter writer, double aiDist, double aiAngle)
    {
        foreach (var cycle in cycles)
        {
            var entry = new Dictionary<string, object>
            {
                ["type"] = "thk_cycle",
                ["cycle_id"] = cycle.CycleId,
                ["ts"] = Math.Round(cycle.QuestElapsedSec, 2),
                ["ai_d"] = Math.Round(aiDist, 1),
                ["ai_a"] = Math.Round(aiAngle, 1),
                ["action_ids"] = cycle.ActionIds.Select(a => (int)a).ToList(),
                ["segments"] = cycle.Segments.Select(s => new
                {
                    n = s.RawNi,
                    s = s.SegIdx,
                    c = s.CheckType,
                    st = s.SegType,
                    it = s.Interrupt,
                    act = s.ActionId,
                    ext_thk = s.ExtRefThkId,
                    ext_node = s.ExtRefNodeId,
                    p1 = s.Parameter1,
                    p2 = s.Parameter2,
                    d = s.GetDescription()
                }).ToList()
            };
            writer.WriteLine(JsonSerializer.Serialize(entry, _jsonOpts));
        }
    }
}
