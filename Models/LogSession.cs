using System.IO;
using System.Text.Json;

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

            var entries = new List<LogEntry>();
            double lastTs = 0;

            // Extract timestamp from filename: fatalis_20260715_185115.jsonl
            var fileName = Path.GetFileName(filePath);
            var dt = ParseDateTimeFromFileName(fileName);

            foreach (var line in lines)
            {
                var entry = JsonSerializer.Deserialize<LogEntry>(line,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (entry == null) continue;

                if (entry.Type == "session_end") continue; // skip session markers in display

                if (entry.Ts > lastTs) lastTs = entry.Ts;
                entries.Add(entry);
            }

            return new LogSession
            {
                FileName = fileName,
                FilePath = filePath,
                DisplayName = "",
                StartTime = dt,
                Duration = lastTs,
                EntryCount = entries.Count,
                Entries = entries
            };
        }
        catch { return null; }
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
