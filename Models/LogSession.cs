using System.IO;
using System.Text.Json;
using FatalisOverlay.Services;

namespace FatalisOverlay.Models;

/// <summary>
/// 一次战斗日志会话的元数据
/// </summary>
public class LogSession
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string DisplayName { get; set; } = ""; // 用户自定义名称
    public DateTime StartTime { get; set; }
    public double Duration { get; set; } // 秒
    public int EntryCount { get; set; }
    public List<LogEntry> Entries { get; set; } = new();

    public string DurationText => Duration > 0
        ? $"{(int)Duration / 60}:{(int)Duration % 60:D2}"
        : "--:--";

    public string StartTimeText => StartTime.ToString("MM-dd HH:mm");

    /// <summary>
    /// 解析 JSONL 文件，返回 LogSession
    /// </summary>
    public static LogSession? Parse(string filePath)
    {
        try
        {
            var lines = File.ReadAllLines(filePath);
            if (lines.Length == 0) return null;

            // Parse all lines into entries (action/damage/enrage) and THK cycles
            var entries = new List<LogEntry>();
            var thkCycles = new List<ThkCycleEntry>();
            double lastTs = 0;

            foreach (var line in lines)
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp)) continue;
                string type = typeProp.GetString() ?? "";

                if (type == "session_end") continue;
                if (type == "thk_cycle")
                {
                    thkCycles.Add(ParseThkCycle(root));
                    continue;
                }

                var entry = JsonSerializer.Deserialize<LogEntry>(line,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (entry == null) continue;
                if (entry.Ts > lastTs) lastTs = entry.Ts;
                entries.Add(entry);
            }

            // Post-hoc matching: match thk_cycle to action by AI distance/angle
            foreach (var entry in entries)
            {
                if (entry.Type != "action" || entry.Aid <= 0) continue;
                var best = FindBestCycle(thkCycles, entry.Ts, entry.Aid, entry.Ai_d, entry.Ai_a);
                if (best != null)
                {
                    entry.ThkPath = best.PathSummary;
                    entry.ThkCycleId = best.CycleId;
                    entry.ThkSegmentsRaw = best.SegmentsJson;
                }
            }

            var fileName = Path.GetFileName(filePath);
            return new LogSession
            {
                FileName = fileName,
                FilePath = filePath,
                DisplayName = "",
                StartTime = ParseDateTimeFromFileName(fileName),
                Duration = lastTs,
                EntryCount = entries.Count,
                Entries = entries
            };
        }
        catch { return null; }
    }

    private static ThkCycleEntry? FindBestCycle(List<ThkCycleEntry> cycles, double actionTs, int actionId,
        double aiDist, double aiAngle)
    {
        // Match by AI distance & angle (identical = same move); fallback to ActionID
        foreach (var c in cycles)
        {
            if (Math.Abs(c.AiDist - aiDist) < 0.1 && Math.Abs(c.AiAngle - aiAngle) < 0.1
                && c.ActionIds.Contains(actionId))
                return c;
        }
        return null;
    }

    private static ThkCycleEntry ParseThkCycle(JsonElement root)
    {
        var ce = new ThkCycleEntry();
        ce.CycleId = root.TryGetProperty("cycle_id", out var p) ? p.GetInt32() : -1;
        ce.Ts = root.TryGetProperty("ts", out var t) ? t.GetDouble() : 0;
        ce.AiDist = root.TryGetProperty("ai_d", out var ad) ? ad.GetDouble() : 0;
        ce.AiAngle = root.TryGetProperty("ai_a", out var aa) ? aa.GetDouble() : 0;

        if (root.TryGetProperty("action_ids", out var aids))
            foreach (var a in aids.EnumerateArray()) ce.ActionIds.Add(a.GetInt32());

        if (root.TryGetProperty("segments", out var segs))
        {
            ce.SegmentCount = segs.GetArrayLength();
            ce.SegmentsJson = segs;
            // Build summary using nack_map_full.json lookup
            int cmRaw = -1, glRaw = -1;
            foreach (var s in segs.EnumerateArray())
            {
                int n = s.TryGetProperty("n", out var pn) ? pn.GetInt32() : -1;
                int act = s.TryGetProperty("act", out var pa) ? pa.GetInt32() : 0;
                int eThk = s.TryGetProperty("ext_thk", out var pe) ? pe.GetInt32() : 0;
                if (eThk == 55 && n > 0) cmRaw = n;
                if (act > 0 && eThk == 0) glRaw = n;
            }
            string cm = cmRaw >= 0 && ThkCycleData.CmIdxToNack.TryGetValue(cmRaw, out var cn) ? $"node_{cn:D3}" : $"n_{cmRaw:D3}";
            string gl = glRaw >= 0 && ThkCycleData.GlobalIdxToNack.TryGetValue(glRaw, out var gn) ? $"Global.node_{gn:D3}" : $"G_{glRaw:D3}";
            ce.PathSummary = cmRaw >= 0 && glRaw >= 0 ? $"{cm} → {gl}" : glRaw >= 0 ? gl : "";
        }
        return ce;
    }

    private static DateTime ParseDateTimeFromFileName(string fileName)
    {
        // fatalis_20260715_185115.jsonl
        try
        {
            var name = Path.GetFileNameWithoutExtension(fileName); // fatalis_20260715_185115
            var parts = name.Split('_');
            if (parts.Length >= 3)
            {
                string date = parts[1]; // 20260715
                string time = parts[2]; // 185115
                return new DateTime(
                    int.Parse(date[..4]), int.Parse(date[4..6]), int.Parse(date[6..8]),
                    int.Parse(time[..2]), int.Parse(time[2..4]), int.Parse(time[4..6]));
            }
        }
        catch { }
        return DateTime.Now;
    }
}

/// <summary>Parsed THK cycle entry.</summary>
public class ThkCycleEntry
{
    public int CycleId { get; set; } = -1;
    public double Ts { get; set; }
    public double AiDist { get; set; }
    public double AiAngle { get; set; }
    public List<int> ActionIds { get; set; } = new();
    public int SegmentCount { get; set; }
    public System.Text.Json.JsonElement SegmentsJson { get; set; }
    public string PathSummary { get; set; } = "";
}

/// <summary>
/// 单条日志记录
/// </summary>
public class LogEntry
{
    public string Type { get; set; } = "";     // action, damage, enrage_start, enrage_end
    public double Ts { get; set; }              // 任务已过秒数
    public int Aid { get; set; }                // 招式 ID
    public string Aname { get; set; } = "";     // 招式名称
    public int Mhp { get; set; }                // 黑龙血量
    public int Delta { get; set; }             // 血量变化
    public int Php { get; set; }               // 玩家血量
    public int H_hp { get; set; }              // 头部血量
    public int C_hp { get; set; }              // 胸部血量
    public int Ca { get; set; }                // 下压值
    public double Dist { get; set; }           // 玩家距离
    public double Ai_d { get; set; }           // AI决策距离
    public double Ai_a { get; set; }           // AI决策角度
    [System.Text.Json.Serialization.JsonPropertyName("slv")]
    public int SpiritLv { get; set; }          // 太刀刃等级 0-3
    public string SpiritText => SpiritLv switch { 3 => "红刃", 2 => "黄刃", 1 => "白刃", _ => "" };
    public string SpiritColor => SpiritLv switch { 3 => "#F44336", 2 => "#FFEB3B", 1 => "#888", _ => "Transparent" };

    // For display: broken into styled parts
    public string TsDisplay => $"{(int)Ts / 60:D2}:{Ts % 60:00.0}";
    public string ActionTitle => Type switch
    {
        "action" => Aname,
        "damage" => $"-{Delta}",
        _ => ""
    };
    public string ActionPost => Type switch
    {
        "action" => "",
        "damage" => $" HP  → 黑龙:{Mhp} 玩家:{Php}",
        _ => ""
    };

    public string DetailDist => $"距离 {Dist:F0}m";
    public string DetailAi => Type == "action" ? $"  |  AI {Ai_d:F0}m / {Ai_a:F0}°" : "";

    [System.Text.Json.Serialization.JsonPropertyName("platform")]
    public string Platform { get; set; } = "";

    // THK behavior tree path
    [System.Text.Json.Serialization.JsonPropertyName("thk_path")]
    public string? ThkPath { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("thk_cycle_id")]
    public int ThkCycleId { get; set; } = -1;

    // Raw THK segments JSON (parsed on demand)
    [System.Text.Json.Serialization.JsonPropertyName("thk_segments")]
    public System.Text.Json.JsonElement? ThkSegmentsRaw { get; set; }

    public bool HasThkPath => !string.IsNullOrEmpty(ThkPath);

    /// <summary>Generate readable path lines for the expander panel (decision points only).</summary>
    public List<string> ThkPathLines
    {
        get
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(ThkPath)) { lines.Add("(无路径数据)"); return lines; }
            lines.Add(ThkPath);
            if (!ThkSegmentsRaw.HasValue || ThkSegmentsRaw.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
                return lines;

            string lastWhere = "";
            foreach (var seg in ThkSegmentsRaw.Value.EnumerateArray())
            {
                int n = seg.TryGetProperty("n", out var pn) ? pn.GetInt32() : -1;
                int s = seg.TryGetProperty("s", out var ps) ? ps.GetInt32() : 0;
                int st = seg.TryGetProperty("st", out var pst) ? pst.GetInt32() : 0;
                int act = seg.TryGetProperty("act", out var pact) ? pact.GetInt32() : 0;
                int eThk = seg.TryGetProperty("ext_thk", out var pet) ? pet.GetInt32() : 0;
                int eNode = seg.TryGetProperty("ext_node", out var pen) ? pen.GetInt32() : 0;
                string desc = seg.TryGetProperty("d", out var pd) ? pd.GetString() ?? "" : "";

                // Only show decision points: if/elif/else, actions, and Global jumps
                bool isDecision = st == 0x02 || st == 0x04;
                bool isAction = act > 0;
                bool isGlobalJump = eThk == 55 && act == 0;
                bool isGlobalFinale = act > 0 && eThk == 0; // ActionID in Global

                if (!isDecision && !isAction && !isGlobalJump) continue;

                string marker = st == 0x02 ? "▶" : st == 0x04 ? "│" : isAction ? "⚔" : " ";
                string where = isGlobalFinale ? $"Global.node_{n:D3}"
                             : n >= 0 ? $"node_{n:D3}/S{s:D2}"
                             : $"Global.node_{eNode:D3}";
                if (where == lastWhere && !isAction && !isDecision) continue; // skip duplicate jump
                lastWhere = where;

                string actName = isAction && FatalisOverlay.Models.ThkCycleData.ActionNames.TryGetValue((uint)act, out var an) ? an : "";
                string extra = isAction ? $" ⚔ {actName}" : isGlobalJump ? $" → Global.node_{eNode:D3}" : "";

                lines.Add($"{marker} {where}  {desc}{extra}");
            }
            return lines;
        }
    }

    public string DetailPlatform => Type == "action" ? $"  |  {Platform}" : "";
    public string PlatformColor => Platform == "对高台" ? "#FF9800" : "#888";
    public string DetailHead => $"头 {H_hp}";
    public string DetailChest => $"胸 {C_hp}";
    public string DetailCa => $"下压 {Ca}";

    public bool ShowBars => Type is "action" or "damage";
    public bool IsEnrageEvent => Type is "enrage_start" or "enrage_end";

    public string Emoji => Type switch
    {
        "enrage_start" => $"[{TsDisplay}] ⚡ 进怒",
        "enrage_end" => $"[{TsDisplay}] 消怒",
        _ => ""
    };

    public string ColorCode => Type switch
    {
        "action" => "#4FC3F7",
        "damage" => "#F44336",
        "enrage_start" => "#FF9800",
        "enrage_end" => "#FFC107",
        _ => "#888"
    };

    // Bar data (Fatalis-specific constants, scaled for 60px bar width)
    public double HeadPercent => 4500 > 0 ? Math.Min(H_hp / 4500.0 * 100, 100) : 0;
    public double ChestPercent => 6750 > 0 ? Math.Min(C_hp / 6750.0 * 100, 100) : 0;
    public double CaPercent => 1500 > 0 ? Math.Min(Ca / 1500.0 * 100, 100) : 0;

    // Scaled widths for 60px bar containers
    public double HeadBarW => HeadPercent * 0.6;
    public double ChestBarW => ChestPercent * 0.6;
    public double CaBarW => CaPercent * 0.6;
    public string HeadText => $"{H_hp}";
    public string ChestText => $"{C_hp}";
    public string CaText => $"{Ca}";
}
