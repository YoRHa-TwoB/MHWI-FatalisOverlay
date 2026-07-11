namespace FatalisOverlay.Services;

public class GameData
{
    public bool Connected { get; set; }
    public string StatusText { get; set; } = "等待游戏...";

    // Quest
    public bool InQuest { get; set; }
    public int QuestId { get; set; }
    public string QuestElapsed { get; set; } = "--:--";

    // Monster
    public bool IsFatalis { get; set; }
    public int MonsterId { get; set; }
    public float Health { get; set; }
    public float MaxHealth { get; set; }
    public double HealthPercent => MaxHealth > 0 ? Health / MaxHealth * 100 : 0;

    // Counterattack
    public float CounterattackDisplay { get; set; }
    public bool CounterattackScaled { get; set; }
    public float CounterattackMax => CounterattackScaled ? 2143f : 1500f;
    public double CounterattackPercent => CounterattackDisplay > 0
        ? Math.Min(CounterattackDisplay / CounterattackMax * 100, 100) : 0;
    public string CounterattackMaxDisplay => CounterattackScaled ? "2,143" : "1,500";
    public bool HasCounterattack { get; set; }

    // Parts (exactly mapped)
    public FatalisPart? PartHead { get; set; }
    public FatalisPart? PartChest { get; set; }
    public FatalisPart? PartLArm { get; set; }
    public FatalisPart? PartRArm { get; set; }
    public FatalisPart? PartLLeg { get; set; }
    public FatalisPart? PartRLeg { get; set; }
    public FatalisPart? PartNeck { get; set; }
    public FatalisPart? PartLWing { get; set; }
    public FatalisPart? PartRWing { get; set; }

    // Ailments (pre-populated so WPF bindings don't throw)
    public Dictionary<int, AilmentInfo> Ailments { get; set; } = new()
    {
        {1, new AilmentInfo { Id = 1, Name = "毒" }},
        {2, new AilmentInfo { Id = 2, Name = "麻" }},
        {3, new AilmentInfo { Id = 3, Name = "眠" }},
        {4, new AilmentInfo { Id = 4, Name = "爆" }},
        {5, new AilmentInfo { Id = 5, Name = "骑" }},
        {6, new AilmentInfo { Id = 6, Name = "减气" }},
        {7, new AilmentInfo { Id = 7, Name = "晕" }},
    };

    // AI
    public float AiDist { get; set; }
    public float AiAngle { get; set; }

    // Enrage
    public bool IsEnraged { get; set; }
    public float EnrageRemaining { get; set; }   // MaxDuration - Duration (countdown)
    public float EnrageMaxDuration { get; set; }
    public float EnrageBuildup { get; set; }
    public float EnrageMaxBuildup { get; set; }
    public string EnrageCountdown => IsEnraged && EnrageRemaining > 0
        ? $"{(int)EnrageRemaining / 60}:{(int)EnrageRemaining % 60:D2}"
        : "";
    public double EnrageBuildupPercent => EnrageMaxBuildup > 0
        ? EnrageBuildup / EnrageMaxBuildup * 100 : 0;

    // Player
    public float PlayerDistance { get; set; }

    // Action
    public int ActionId { get; set; }
}

public class FatalisPart
{
    public int Id { get; set; }
    public float Health { get; set; }
    public float MaxHealth { get; set; }
    public int Counter { get; set; }
    public int BreakThreshold1 { get; set; }
    public int BreakThreshold2 { get; set; }
    public string Name { get; set; } = "";
    public double Percent => MaxHealth > 0 ? Health / MaxHealth * 100 : 0;

    // Current break level based on counter vs thresholds
    public int BreakLevel
    {
        get
        {
            if (BreakThreshold2 > 0 && Counter >= BreakThreshold2) return 2;
            if (BreakThreshold1 > 0 && Counter >= BreakThreshold1) return 1;
            return 0;
        }
    }

    public string BreakText => BreakLevel switch
    {
        2 => " ★★",
        1 => " ★",
        _ => ""
    };

    // Tenderize (软化)
    public bool IsTenderized { get; set; }
    public float TenderizeDuration { get; set; }
    public float TenderizeMaxDuration { get; set; }
    public double TenderizePercent => IsTenderized && TenderizeMaxDuration > 0
        ? Math.Max(0, (TenderizeMaxDuration - TenderizeDuration) / TenderizeMaxDuration * 100) : 0;
}

public class AilmentInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public float Buildup { get; set; }
    public float MaxBuildup { get; set; }
    public float Timer { get; set; }
    public float MaxTimer { get; set; }
    public int TriggerCount { get; set; }  // how many times triggered
    public bool IsActive => Buildup > 0 || Timer > 0;
    public double Percent => MaxBuildup > 0 ? Buildup / MaxBuildup * 100 : 0;
}
