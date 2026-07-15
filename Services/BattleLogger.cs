using System.IO;
using System.Text.Json;

namespace FatalisOverlay.Services;

/// <summary>
/// 战斗日志模块：检测招式变化、血量下降、怒状态变化，输出 JSONL 格式日志
/// </summary>
public static class BattleLogger
{
    private static StreamWriter? _writer;
    private static string? _currentFilePath;

    // 变化检测状态
    private static int _lastActionId = -1;
    private static float _lastMonsterHp = float.MaxValue;
    private static bool _lastIsEnraged;
    private static int _lastQuestId;
    private static bool _hasReachedArena; // 玩家是否曾经降到地面附近

    // 招式名映射
    private static Dictionary<int, string> _actionNames = new();

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 加载招式名映射文件
    /// </summary>
    public static void LoadActionNames()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "action_names.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var entries = JsonSerializer.Deserialize<List<ActionNameEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                _actionNames = entries?.ToDictionary(e => e.ActionId, e => e.Name)
                    ?? new Dictionary<int, string>();
            }
        }
        catch { /* 加载失败就用空映射，不影响日志功能 */ }
    }

    private static string ResolveActionName(int actionId)
        => _actionNames.TryGetValue(actionId, out var name) ? name : "";

    /// <summary>
    /// 每帧轮询时调用
    /// </summary>
    public static void Log(GameData data, bool enabled)
    {
        if (!data.IsFatalis || !data.InQuest)
        {
            // 不在黑龙任务中，关闭当前日志
            if (_writer != null)
            {
                WriteEntry(new { type = "session_end" });
                CloseLog();
            }
            return;
        }

        if (!enabled)
        {
            if (_writer != null) CloseLog();
            return;
        }

        // 检测重开
        if (DetectNewSession(data))
        {
            if (_writer != null)
            {
                WriteEntry(new { type = "session_end" });
                CloseLog();
            }
            StartNewLog();
            ResetState(data);
            return;
        }

        if (_writer == null)
        {
            StartNewLog();
            ResetState(data);
        }

        // 检测变化并写入
        var changes = DetectChanges(data);
        foreach (var entry in changes)
            WriteEntry(entry);
    }

    /// <summary>
    /// 检测是否重开任务：黑龙血量上升（重置）或 QuestId 变化
    /// 仅当玩家曾经下到地面后才激活检测
    /// </summary>
    private static bool DetectNewSession(GameData data)
    {
        // 玩家降到地面附近后，标记为已到达竞技场
        if (data.PlayerY < 500 && data.PlayerY > -100)
            _hasReachedArena = true;

        // 还没到过地面之前不触发任何重开
        if (!_hasReachedArena) return false;

        // 黑龙血量明显回升（说明重开了）
        if (_lastMonsterHp > 0 && _lastMonsterHp < float.MaxValue
            && data.Health > _lastMonsterHp + 500)
        {
            return true;
        }

        // QuestId 变了也是新任务
        if (data.InQuest && _lastQuestId != 0 && _lastQuestId != data.QuestId)
            return true;

        return false;
    }

    /// <summary>
    /// 检测本帧变化，返回需要写入的日志条目列表
    /// </summary>
    private static List<object> DetectChanges(GameData data)
    {
        var entries = new List<object>();

        // 1. 消怒（优先处理，出现在招式变化之前）
        if (_lastIsEnraged && !data.IsEnraged)
        {
            entries.Add(new { type = "enrage_end", ts = data.QuestElapsedSeconds });
        }

        // 2. 招式变化
        if (data.ActionId != _lastActionId)
        {
            var entry = BuildActionEntry(data);
            if (entry != null) entries.Add(entry);
        }

        // 3. 进怒
        if (!_lastIsEnraged && data.IsEnraged)
        {
            entries.Add(new { type = "enrage_start", ts = data.QuestElapsedSeconds });
        }

        // 4. 血量下降
        if (_lastMonsterHp < float.MaxValue
            && data.Health < _lastMonsterHp - 1.0f)
        {
            entries.Add(BuildDamageEntry(data));
        }

        // 更新状态
        _lastActionId = data.ActionId;
        _lastMonsterHp = data.Health;
        _lastIsEnraged = data.IsEnraged;

        return entries;
    }

    private static object? BuildActionEntry(GameData data)
    {
        return new
        {
            type = "action",
            ts = Math.Round(data.QuestElapsedSeconds, 2),
            aid = data.ActionId,
            aname = ResolveActionName(data.ActionId),
            mhp = (int)data.Health,
            php = (int)data.PlayerHealth,
            slv = data.SpiritLevel,
            h_hp = (int)(data.PartHead?.Health ?? 0),
            c_hp = (int)(data.PartChest?.Health ?? 0),
            ca = (int)data.CounterattackDisplay,
            dist = Math.Round(data.PlayerDistance, 1),
            ai_d = Math.Round(data.AiDist, 1),
            ai_a = Math.Round(data.AiAngle, 1)
        };
    }

    private static object BuildDamageEntry(GameData data)
    {
        return new
        {
            type = "damage",
            ts = Math.Round(data.QuestElapsedSeconds, 2),
            mhp = (int)data.Health,
            delta = (int)(_lastMonsterHp - data.Health),
            php = (int)data.PlayerHealth,
            slv = data.SpiritLevel,
            h_hp = (int)(data.PartHead?.Health ?? 0),
            c_hp = (int)(data.PartChest?.Health ?? 0),
            ca = (int)data.CounterattackDisplay,
            dist = Math.Round(data.PlayerDistance, 1)
        };
    }

    private static void StartNewLog()
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(dir);
        var fileName = $"fatalis_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl";
        _currentFilePath = Path.Combine(dir, fileName);
        _writer = new StreamWriter(_currentFilePath, append: false)
        {
            AutoFlush = true
        };
    }

    private static void CloseLog()
    {
        _writer?.Dispose();
        _writer = null;
        _currentFilePath = null;
    }

    private static void ResetState(GameData data)
    {
        _lastActionId = -1;
        _lastMonsterHp = float.MaxValue;
        _lastIsEnraged = data.IsEnraged;
        _lastQuestId = data.QuestId;
        _hasReachedArena = false;
    }

    private static void WriteEntry(object entry)
    {
        if (_writer == null) return;
        try
        {
            _writer.WriteLine(JsonSerializer.Serialize(entry, _jsonOpts));
        }
        catch { /* 写入失败不中断程序 */ }
    }

    private class ActionNameEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("action_id")]
        public int ActionId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }
}
