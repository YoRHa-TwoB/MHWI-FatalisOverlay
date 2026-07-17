using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FatalisOverlay.Models;

public class AppConfig : INotifyPropertyChanged
{
    private bool _showHealth = true;
    private bool _showCounterattack = true;
    private bool _showAiDecision = true;
    private bool _showBattleLog;
    private bool _showQuestTimer = true;
    private bool _showDistance = true;
    private bool _showPlatform = true;

    // Individual part toggles
    private bool _showPartHead = true;
    private bool _showPartChest = true;
    private bool _showPartLArm = true;
    private bool _showPartRArm = true;
    private bool _showPartLLeg = true;
    private bool _showPartRLeg = true;
    private bool _showPartNeck = true;
    private bool _showPartLWing = true;
    private bool _showPartRWing = true;

    // Individual ailment toggles
    private bool _showAilmentPoison = true;     // 0
    private bool _showAilmentPara = true;       // 1
    private bool _showAilmentSleep = true;      // 2
    private bool _showAilmentBlast = true;      // 3
    private bool _showAilmentStun = true;       // 4
    private bool _showAilmentExhaust = true;    // 5
    private bool _showAilmentMount = true;      // 7
    private bool _showAilmentEnrage = true;     // 99

    private double _windowX = 100;
    private double _windowY = 100;
    private double _scale = 1.0;
    private string _healthColor = "#4CAF50";
    private string _counterattackColor = "#FF9800";
    private string _partColor = "#2196F3";
    private string _backgroundColor = "#CC1A1A1A";
    private int _pollingRateMs = 28;

    // Display toggles
    public bool ShowHealth { get => _showHealth; set { _showHealth = value; OnPropertyChanged(); } }
    public bool ShowCounterattack { get => _showCounterattack; set { _showCounterattack = value; OnPropertyChanged(); } }
    public bool ShowAiDecision { get => _showAiDecision; set { _showAiDecision = value; OnPropertyChanged(); } }
    public bool ShowQuestTimer { get => _showQuestTimer; set { _showQuestTimer = value; OnPropertyChanged(); } }
    public bool ShowDistance { get => _showDistance; set { _showDistance = value; OnPropertyChanged(); } }
    public bool ShowPlatform { get => _showPlatform; set { _showPlatform = value; OnPropertyChanged(); } }
    public bool ShowBattleLog { get => _showBattleLog; set { _showBattleLog = value; OnPropertyChanged(); } }

    // Part toggles
    public bool ShowPartHead { get => _showPartHead; set { _showPartHead = value; OnPropertyChanged(); } }
    public bool ShowPartChest { get => _showPartChest; set { _showPartChest = value; OnPropertyChanged(); } }
    public bool ShowPartLArm { get => _showPartLArm; set { _showPartLArm = value; OnPropertyChanged(); } }
    public bool ShowPartRArm { get => _showPartRArm; set { _showPartRArm = value; OnPropertyChanged(); } }
    public bool ShowPartLLeg { get => _showPartLLeg; set { _showPartLLeg = value; OnPropertyChanged(); } }
    public bool ShowPartRLeg { get => _showPartRLeg; set { _showPartRLeg = value; OnPropertyChanged(); } }
    public bool ShowPartNeck { get => _showPartNeck; set { _showPartNeck = value; OnPropertyChanged(); } }
    public bool ShowPartLWing { get => _showPartLWing; set { _showPartLWing = value; OnPropertyChanged(); } }
    public bool ShowPartRWing { get => _showPartRWing; set { _showPartRWing = value; OnPropertyChanged(); } }

    // Ailment toggles
    public bool ShowAilmentPoison { get => _showAilmentPoison; set { _showAilmentPoison = value; OnPropertyChanged(); } }
    public bool ShowAilmentPara { get => _showAilmentPara; set { _showAilmentPara = value; OnPropertyChanged(); } }
    public bool ShowAilmentSleep { get => _showAilmentSleep; set { _showAilmentSleep = value; OnPropertyChanged(); } }
    public bool ShowAilmentBlast { get => _showAilmentBlast; set { _showAilmentBlast = value; OnPropertyChanged(); } }
    public bool ShowAilmentStun { get => _showAilmentStun; set { _showAilmentStun = value; OnPropertyChanged(); } }
    public bool ShowAilmentExhaust { get => _showAilmentExhaust; set { _showAilmentExhaust = value; OnPropertyChanged(); } }
    public bool ShowAilmentMount { get => _showAilmentMount; set { _showAilmentMount = value; OnPropertyChanged(); } }
    public bool ShowAilmentEnrage { get => _showAilmentEnrage; set { _showAilmentEnrage = value; OnPropertyChanged(); } }

    // Appearance
    public double WindowX { get => _windowX; set { _windowX = value; OnPropertyChanged(); } }
    public double WindowY { get => _windowY; set { _windowY = value; OnPropertyChanged(); } }
    public double Scale { get => _scale; set { _scale = value; OnPropertyChanged(); } }
    public string HealthColor { get => _healthColor; set { _healthColor = value; OnPropertyChanged(); } }
    public string CounterattackColor { get => _counterattackColor; set { _counterattackColor = value; OnPropertyChanged(); } }
    public string PartColor { get => _partColor; set { _partColor = value; OnPropertyChanged(); } }
    public string BackgroundColor { get => _backgroundColor; set { _backgroundColor = value; OnPropertyChanged(); } }
    public int PollingRateMs { get => _pollingRateMs; set { _pollingRateMs = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded != null) return loaded;
            }
        }
        catch { }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, opts));
        }
        catch { }
    }

    // Helper to check if a part is enabled by name
    public bool IsPartEnabled(string partKey) => partKey switch
    {
        "Head" => ShowPartHead,
        "Chest" => ShowPartChest,
        "LArm" => ShowPartLArm,
        "RArm" => ShowPartRArm,
        "LLeg" => ShowPartLLeg,
        "RLeg" => ShowPartRLeg,
        "Neck" => ShowPartNeck,
        "LWing" => ShowPartLWing,
        "RWing" => ShowPartRWing,
        _ => false
    };

    public bool IsAilmentEnabled(int id) => id switch
    {
        1 => ShowAilmentPoison,
        2 => ShowAilmentPara,
        3 => ShowAilmentSleep,
        4 => ShowAilmentBlast,
        5 => ShowAilmentMount,
        6 => ShowAilmentExhaust,
        7 => ShowAilmentStun,
        99 => ShowAilmentEnrage,
        _ => false
    };
}
