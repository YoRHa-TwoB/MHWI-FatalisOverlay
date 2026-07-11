using System.Diagnostics;

namespace FatalisOverlay.Services;

public class ProcessService
{
    // Offsets (MHW v421810)
    private const long PLAYER_BASE = 0x050139A0;
    private const long QUEST_BASE = 0x0500ED30;
    private const long MONSTER_ARRAY_BASE = 0x0500CF40;
    private const long COUNTERATTACK_BASE = 0x05013C50;

    private IntPtr _processHandle = IntPtr.Zero;
    private long _gameBase;
    private int _pid;
    private bool _counterattackScaled;

    public IntPtr Handle => _processHandle;
    public long GameBase => _gameBase;
    public bool IsConnected => _processHandle != IntPtr.Zero && _gameBase != 0;

    // Fatalis normal parts in memory order (from MonsterData.xml, excluding Id=0 which is severable)
    // Slot 0→Id=1(Head), 1→2(Neck), 2→3(Chest), 3→4(Body), 4→5(LArm), 5→6(RArm),
    //       6→7(LLeg), 7→8(RLeg), 8→9(LWing), 9→10(RWing), 10→11(Tail)
    private static readonly (int Id, string Name, string Key, int Break1, int Break2)[] NormalPartSlots =
    {
        (1,  "头",   "Head",  3, 6),
        (2,  "脖子", "Neck",  0, 0),
        (3,  "胸",   "Chest", 2, 0),
        (4,  "身体", "Body",  0, 0),
        (5,  "左手", "LArm",  0, 0),
        (6,  "右手", "RArm",  0, 0),
        (7,  "左腿", "LLeg",  0, 0),
        (8,  "右腿", "RLeg",  0, 0),
        (9,  "左翼", "LWing", 1, 0),
        (10, "右翼", "RWing", 1, 0),
        (11, "尾",   "Tail",  0, 0),
    };

    // Only map parts the user cares about
    private static readonly HashSet<string> MappedKeys = new()
        { "Head", "Neck", "Chest", "LArm", "RArm", "LLeg", "RLeg", "LWing", "RWing" };

    // Known ailment names by ID
    // Correct IDs from MonsterData.xml Ailments section
    private static readonly Dictionary<int, string> AilmentNames = new()
    {
        { 1, "毒" }, { 2, "麻" }, { 3, "眠" }, { 4, "爆" },
        { 5, "骑乘" }, { 6, "减气" }, { 7, "晕" }, { 99, "怒" }
    };

    public bool TryConnect()
    {
        if (IsConnected && !Process.GetProcessById(_pid).HasExited)
            return true;

        Disconnect();

        var processes = Process.GetProcessesByName("MonsterHunterWorld");
        if (processes.Length == 0) return false;

        var process = processes[0];
        _pid = process.Id;
        _gameBase = process.MainModule?.BaseAddress.ToInt64() ?? 0;

        if (_gameBase == 0) return false;

        _processHandle = MemoryReader.OpenProcessHandle(_pid);
        _counterattackScaled = false;
        return _processHandle != IntPtr.Zero;
    }

    public void Disconnect()
    {
        if (_processHandle != IntPtr.Zero)
            MemoryReader.CloseProcessHandle(_processHandle);
        _processHandle = IntPtr.Zero;
        _gameBase = 0;
        _pid = 0;
    }

    public GameData ReadGameData()
    {
        var data = new GameData();

        if (!IsConnected)
        {
            data.StatusText = "等待 MonsterHunterWorld.exe...";
            return data;
        }

        try
        {
            data.Connected = true;
            ReadQuestData(data);

            var fatalisPtr = FindFatalisMonster();
            if (fatalisPtr == 0)
            {
                data.StatusText = data.InQuest ? "未找到黑龙" : "等待进入任务...";
                return data;
            }

            data.IsFatalis = true;
            data.MonsterId = 101;
            data.StatusText = "";

            ReadMonsterHealth(data, fatalisPtr);
            ReadCounterattack(data);
            ReadMonsterParts(data, fatalisPtr);
            ReadTenderizeData(data, fatalisPtr);
            ReadMonsterAilments(data, fatalisPtr);
            ReadAiDecision(data);
            ReadAction(data, fatalisPtr);
            ReadEnrage(data, fatalisPtr);
            ReadPlayerDistance(data, fatalisPtr);
        }
        catch { }

        return data;
    }

    // ── Quest ──

    private void ReadQuestData(GameData data)
    {
        var questPtr = MemoryReader.ReadPointer(_processHandle, _gameBase + QUEST_BASE);
        if (questPtr == IntPtr.Zero) return;

        int questState = MemoryReader.Read<int>(_processHandle, questPtr + 0x54);
        int questId = MemoryReader.Read<int>(_processHandle, questPtr + 0x4C);
        data.InQuest = questState == 2 && questId > 0;
        data.QuestId = questId;

        if (data.InQuest)
        {
            // Timer is a ulong directly at questPtr + 0x13180 (HunterPie's DerefAsync with ReadAsync, not ReadPtrAsync)
            ulong timerRaw = MemoryReader.Read<ulong>(_processHandle, questPtr.ToInt64() + 0x13180);
            if (timerRaw > 0)
            {
                // LiterallyWhyCapcom: value / 60.0f = remaining seconds
                double remainingSecs = timerRaw / 60.0;
                double elapsed = Math.Max(0, GetQuestMaxSeconds(questId) - remainingSecs);
                var ts = TimeSpan.FromSeconds(elapsed);
                data.QuestElapsed = $"{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
        }
    }

    // Known quest max times in seconds: 15, 20, 30, 35, 50 minutes
    private static readonly double[] QuestMaxSeconds = { 900, 1200, 1800, 2100, 3000 };

    private static double GetQuestMaxSeconds(int questId)
    {
        // Default to 30 min for Fatalis quests; could be extended for other quests
        return 1800;
    }

    // ── Monster finding ──

    private long FindFatalisMonster()
    {
        try
        {
            var arrayPtr = MemoryReader.ReadPointer(_processHandle, _gameBase + MONSTER_ARRAY_BASE);
            if (arrayPtr == IntPtr.Zero) return 0;
            arrayPtr = MemoryReader.ReadPointer(_processHandle, arrayPtr + 0x38);
            if (arrayPtr == IntPtr.Zero) return 0;

            for (int i = 0; i < 128; i++)
            {
                var compPtr = MemoryReader.ReadPointer(_processHandle, arrayPtr + i * 8);
                if (compPtr == IntPtr.Zero) continue;

                var monsterPtr = MemoryReader.ReadPointer(_processHandle, compPtr + 0x138);
                if (monsterPtr == IntPtr.Zero) continue;

                var emPtr = MemoryReader.ReadPointer(_processHandle, monsterPtr + 0x2A0);
                if (emPtr == IntPtr.Zero) continue;
                string em = MemoryReader.ReadString(_processHandle, emPtr + 0x0C, 32);
                if (string.IsNullOrEmpty(em)) continue;
                if (!em.StartsWith("em\\em") || em.StartsWith("em\\ems")) continue;

                int monsterId = MemoryReader.Read<int>(_processHandle, monsterPtr + 0x12280);
                if (monsterId == 101)
                    return monsterPtr.ToInt64();
            }
        }
        catch { }
        return 0;
    }

    // ── Health ──

    private void ReadMonsterHealth(GameData data, long monsterPtr)
    {
        var hpPtr = MemoryReader.ReadPointer(_processHandle, (IntPtr)(monsterPtr + 0x7670));
        if (hpPtr == IntPtr.Zero) return;
        data.MaxHealth = MemoryReader.Read<float>(_processHandle, hpPtr + 0x60);
        data.Health = MemoryReader.Read<float>(_processHandle, hpPtr + 0x64);
    }

    // ── Counterattack ──

    private void ReadCounterattack(GameData data)
    {
        var ptr1 = MemoryReader.ReadPointer(_processHandle, _gameBase + COUNTERATTACK_BASE);
        if (ptr1 == IntPtr.Zero) return;

        var ptr2 = MemoryReader.ReadPointer(_processHandle, ptr1 + 0xE58);
        if (ptr2 == IntPtr.Zero) return;

        float raw = MemoryReader.Read<float>(_processHandle, ptr2 + 0x18388);

        if (raw > 0)
        {
            data.HasCounterattack = true;
            data.CounterattackDisplay = _counterattackScaled ? raw / 0.7f : raw;
            data.CounterattackScaled = _counterattackScaled;
        }
    }

    private void ReadAction(GameData data, long monsterPtr)
    {
        int actionId = MemoryReader.Read<int>(_processHandle, (IntPtr)(monsterPtr + 0x61C8 + 0xB0));
        data.ActionId = actionId;
        if (actionId == 179) _counterattackScaled = true;
    }

    // ── Parts ──

    private void ReadMonsterParts(GameData data, long monsterPtr)
    {
        var partPtr = MemoryReader.ReadPointer(_processHandle, (IntPtr)(monsterPtr + 0x1D058));
        if (partPtr == IntPtr.Zero) return;

        // Normal parts: at +0x40, stride 0x1F8, in definition order
        long baseAddr = partPtr.ToInt64() + 0x40;
        for (int slot = 0; slot < NormalPartSlots.Length; slot++)
        {
            long addr = baseAddr + slot * 0x1F8;
            float maxHp = MemoryReader.Read<float>(_processHandle, (IntPtr)(addr + 0x0C));
            float hp = MemoryReader.Read<float>(_processHandle, (IntPtr)(addr + 0x10));
            int counter = MemoryReader.Read<int>(_processHandle, (IntPtr)(addr + 0x18));

            if (!(maxHp > 0)) continue;

            var (id, name, key, b1, b2) = NormalPartSlots[slot];

            if (!MappedKeys.Contains(key)) continue;

            var part = new FatalisPart
            {
                Id = id, Name = name,
                MaxHealth = maxHp, Health = hp,
                Counter = counter,
                BreakThreshold1 = b1, BreakThreshold2 = b2
            };

            AssignPart(data, key, part);
        }
    }

    private static void AssignPart(GameData data, string key, FatalisPart part)
    {
        switch (key)
        {
            case "Head": data.PartHead = part; break;
            case "Chest": data.PartChest = part; break;
            case "LArm": data.PartLArm = part; break;
            case "RArm": data.PartRArm = part; break;
            case "LLeg": data.PartLLeg = part; break;
            case "RLeg": data.PartRLeg = part; break;
            case "Neck": data.PartNeck = part; break;
            case "LWing": data.PartLWing = part; break;
            case "RWing": data.PartRWing = part; break;
        }
    }

    // ── Ailments ──

    private void ReadMonsterAilments(GameData data, long monsterPtr)
    {
        data.Ailments.Clear();
        long ailmentArray = monsterPtr + 0x1BC40;

        for (int i = 0; i < 32; i++)
        {
            var ailmentPtr = MemoryReader.ReadPointer(_processHandle, (IntPtr)(ailmentArray + i * 8));
            if (ailmentPtr.ToInt64() <= 1) break;

            long dataAddr = ailmentPtr.ToInt64() + 0x148;

            var owner = MemoryReader.ReadPointer(_processHandle, (IntPtr)(dataAddr));
            if (owner.ToInt64() != monsterPtr) continue;

            // Offsets from MHWMonsterAilmentStructure
            int id = MemoryReader.Read<int>(_processHandle, (IntPtr)(dataAddr + 0x10));
            int isActive = MemoryReader.Read<int>(_processHandle, (IntPtr)(dataAddr + 0x08));
            float maxDuration = MemoryReader.Read<float>(_processHandle, (IntPtr)(dataAddr + 0x14));
            float buildup = MemoryReader.Read<float>(_processHandle, (IntPtr)(dataAddr + 0x30));
            float maxBuildup = MemoryReader.Read<float>(_processHandle, (IntPtr)(dataAddr + 0x40));
            float duration = MemoryReader.Read<float>(_processHandle, (IntPtr)(dataAddr + 0x70));
            int counter = MemoryReader.Read<int>(_processHandle, (IntPtr)(dataAddr + 0x78));

            if (!(maxBuildup > 0 || maxDuration > 0)) continue;
            if (!AilmentNames.TryGetValue(id, out var name)) continue;

            data.Ailments[id] = new AilmentInfo
            {
                Id = id,
                Name = name,
                Buildup = buildup,
                MaxBuildup = maxBuildup,
                Timer = duration,
                MaxTimer = maxDuration,
                TriggerCount = counter
            };
        }
    }

    // ── AI Decision ──

    private void ReadAiDecision(GameData data)
    {
        var ptr1 = MemoryReader.ReadPointer(_processHandle, _gameBase + COUNTERATTACK_BASE);
        if (ptr1 == IntPtr.Zero) return;
        var ptr2 = MemoryReader.ReadPointer(_processHandle, ptr1 + 0xE58);
        if (ptr2 == IntPtr.Zero) return;
        var ptr3 = MemoryReader.ReadPointer(_processHandle, ptr2 + 0x1008);
        if (ptr3 == IntPtr.Zero) return;
        data.AiDist = MemoryReader.Read<float>(_processHandle, ptr3 + 0x5FC);
        data.AiAngle = MemoryReader.Read<float>(_processHandle, ptr3 + 0x604);
    }

    // ── Tenderize ──

    private void ReadTenderizeData(GameData data, long monsterPtr)
    {
        long baseAddr = monsterPtr + 0x1C458;
        for (int i = 0; i < 10; i++)
        {
            long addr = baseAddr + i * 0x40; // stride from structure size
            uint partId = MemoryReader.Read<uint>(_processHandle, (IntPtr)(addr + 0x30)); // PartId offset
            float duration = MemoryReader.Read<float>(_processHandle, (IntPtr)(addr + 0x08)); // Duration (after long Address)
            float maxDuration = MemoryReader.Read<float>(_processHandle, (IntPtr)(addr + 0x0C)); // MaxDuration

            if (partId == uint.MaxValue) continue;
            // Duration counts UP from 0 (= elapsed). IsTenderized when PartId is valid.
            if (!(maxDuration > 0)) continue;

            SetPartTenderize(data, (int)partId, duration, maxDuration);
        }
    }

    // Tenderize PartId → our part keys (from MonsterData.xml TenderizeIds)
    // PartId 0,3: Head only | PartId 1,4: LArm+RArm+Chest | PartId 2: LLeg | PartId 5: RLeg
    private static void SetPartTenderize(GameData data, int partId, float duration, float maxDuration)
    {
        switch (partId)
        {
            case 0: case 3: // Head
                ApplyTenderize(data.PartHead, duration, maxDuration);
                break;
            case 1: case 4: // Arms + Chest (shared)
                ApplyTenderize(data.PartLArm, duration, maxDuration);
                ApplyTenderize(data.PartRArm, duration, maxDuration);
                ApplyTenderize(data.PartChest, duration, maxDuration);
                break;
            case 2: // L Leg
                ApplyTenderize(data.PartLLeg, duration, maxDuration);
                break;
            case 5: // R Leg
                ApplyTenderize(data.PartRLeg, duration, maxDuration);
                break;
        }
    }

    private static void ApplyTenderize(FatalisPart? part, float duration, float maxDuration)
    {
        if (part == null) return;
        // Duration counts UP (elapsed). Tenderized while duration hasn't exceeded max.
        part.IsTenderized = duration < maxDuration;
        part.TenderizeDuration = duration;
        part.TenderizeMaxDuration = maxDuration;
    }

    // ── Enrage ──

    private void ReadEnrage(GameData data, long monsterPtr)
    {
        // MHWMonsterStatusStructure at monsterPtr + 0x1BE30
        long addr = monsterPtr + 0x1BE30;
        int isActive = MemoryReader.Read<int>(_processHandle, (IntPtr)(addr + 0x14));
        float duration = MemoryReader.Read<float>(_processHandle, (IntPtr)(addr + 0x24));
        float maxDuration = MemoryReader.Read<float>(_processHandle, (IntPtr)(addr + 0x28));
        float buildup = MemoryReader.Read<float>(_processHandle, (IntPtr)(addr + 0x18));
        float maxBuildup = MemoryReader.Read<float>(_processHandle, (IntPtr)(addr + 0x3C));

        data.IsEnraged = isActive != 0;
        data.EnrageRemaining = isActive != 0 ? Math.Max(0, maxDuration - duration) : 0;
        data.EnrageMaxDuration = maxDuration;
        data.EnrageBuildup = buildup;
        data.EnrageMaxBuildup = maxBuildup;
    }

    // ── Player Distance ──

    private void ReadPlayerDistance(GameData data, long monsterPtr)
    {
        var playerPtr = MemoryReader.ReadPointer(_processHandle, _gameBase + PLAYER_BASE);
        if (playerPtr == IntPtr.Zero) return;
        var playerBase = MemoryReader.ReadPointer(_processHandle, playerPtr + 0x50);
        if (playerBase == IntPtr.Zero) return;

        float px = MemoryReader.Read<float>(_processHandle, playerBase + 0x390);
        float py = MemoryReader.Read<float>(_processHandle, playerBase + 0x394);
        float pz = MemoryReader.Read<float>(_processHandle, playerBase + 0x398);

        float mx = MemoryReader.Read<float>(_processHandle, (IntPtr)(monsterPtr + 0x160));
        float my = MemoryReader.Read<float>(_processHandle, (IntPtr)(monsterPtr + 0x164));
        float mz = MemoryReader.Read<float>(_processHandle, (IntPtr)(monsterPtr + 0x168));

        data.PlayerDistance = MathF.Sqrt((px - mx) * (px - mx) + (pz - mz) * (pz - mz));
    }

    public void ResetOnNewQuest() { _counterattackScaled = false; }
}
