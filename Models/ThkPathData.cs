using System.Runtime.InteropServices;

namespace FatalisOverlay.Models;

public class ThkCycleData
{
    public uint CycleId { get; set; }
    public float QuestElapsedSec { get; set; }
    public List<uint> ActionIds { get; set; } = [];
    public List<ThkSegmentInfo> Segments { get; set; } = [];

    public static System.Collections.Generic.Dictionary<uint, string> ActionNames { get; set; } = new();
    private static string GetActionName(uint aid)
        => ActionNames.TryGetValue(aid, out var n) ? n : $"ACT({aid})";

    // ── Mapping from nack_map_full.json: Binary Index/ID → .nack number ──
    public static System.Collections.Generic.Dictionary<int, int> CmIdxToNack { get; set; } = [];
    public static System.Collections.Generic.Dictionary<int, int> GlobalIdxToNack { get; set; } = [];
    public static System.Collections.Generic.Dictionary<int, int> GlobalIdToNack { get; set; } = [];
    public static System.Collections.Generic.HashSet<int> NoProbGlobalNodes { get; set; } = [20, 23, 24, 25];
    public static System.Collections.Generic.HashSet<(int cm, int gl)> NoProbPairs { get; set; } = [(4, 18)];

    public string GetShortLabel()
    {
        if (Segments.Count == 0) return "";

        var actions = new List<(int rawNi, int aid)>();
        for (int i = 0; i < Segments.Count; i++)
            if (Segments[i].ActionId > 0)
                actions.Add((Segments[i].RawNi, Segments[i].ActionId));

        int cmRaw = -1;
        for (int i = 0; i < Segments.Count; i++)
            if (Segments[i].ExtRefThkId == 55 && Segments[i].RawNi > 0)
                cmRaw = Segments[i].RawNi; // last, not first

        string FmtCm(int r) => CmIdxToNack.TryGetValue(r, out var n) ? $"node_{n:D3}" : $"n_{r:D3}";
        string FmtG(int r) => GlobalIdxToNack.TryGetValue(r, out var n) ? $"Global.node_{n:D3}" : $"G_{r:D3}";

        // Hide probability if excluded: Global-wide OR specific (cm,global) pair
        int lastProb = 0;
        bool noProb = actions.Count > 0
            && GlobalIdxToNack.TryGetValue(actions[^1].rawNi, out var gn)
            && (NoProbGlobalNodes.Contains(gn)
                || (CmIdxToNack.TryGetValue(cmRaw, out var cn) && NoProbPairs.Contains((cn, gn))));
        if (!noProb)
            for (int i = 0; i < Segments.Count; i++)
                if (Segments[i].CheckType == 0 && Segments[i].Parameter1 > 0)
                    lastProb = Segments[i].Parameter1;
        string probStr = lastProb > 0 ? $" [{lastProb}%]" : "";

        var parts = new List<string>();
        if (cmRaw >= 0) parts.Add(FmtCm(cmRaw));
        foreach (var (rawNi, aid) in actions)
            parts.Add($"{FmtG(rawNi)} → {GetActionName((uint)aid)}");

        return parts.Count > 0 ? string.Join(" → ", parts) + probStr : "";
    }

    public string GetPathSummary()
    {
        if (Segments.Count == 0) return "(empty)";
        var parts = new List<string>();
        int lastNode = -2; bool lastWasJump = false;
        for (int i = 0; i < Segments.Count; i++)
        {
            var s = Segments[i];
            bool isDecision = s.SegType == 0x02 || s.SegType == 0x04;
            bool isAction = s.ActionId > 0;
            bool isJump = !isDecision && !isAction && s.SegType == 0 && s.CheckType == 2;
            bool isGlobal = s.ExtRefThkId == 55;
            if (isJump && lastWasJump && s.RawNi == lastNode) continue;
            lastWasJump = isJump; lastNode = s.RawNi;
            if (isAction && s.ExtRefThkId == 0)
                parts.Add($"G_{s.RawNi:D3}→{GetActionName((uint)s.ActionId)}");
            else if (isDecision)
                parts.Add($"if {CheckTypeName(s.CheckType, out _)}");
            else if (isAction)
                parts.Add($"[{GetActionName((uint)s.ActionId)}]");
            else if (isGlobal)
                parts.Add($"G_{s.ExtRefNodeId:D3}");
            else if (isJump && s.CheckType == 2)
                { if (s.RawNi >= 0) parts.Add($"n_{s.RawNi:D3}"); }
            else
            {
                string name = CheckTypeName(s.CheckType, out _);
                if (name.Length > 0 && name != "else/goto") parts.Add(name);
                else if (s.RawNi >= 0) parts.Add($"n_{s.RawNi:D3}");
            }
        }
        return string.Join(" → ", parts);
    }

    public List<string> GetPathLines()
    {
        var lines = new List<string>();
        for (int i = 0; i < Segments.Count; i++)
        {
            var s = Segments[i];
            string marker = s.SegType == 0x02 ? "▶ " : s.SegType == 0x04 ? "│ " : s.SegType == 0x08 ? "◀ " : s.ActionId > 0 ? "⚔ " : s.Interrupt == 0x80 ? "↺ " : s.Interrupt == 0x08 ? "← " : "  ";
            string desc = s.GetDescription();
            string where = s.ActionId > 0 && s.ExtRefThkId == 0 ? $"G_{s.RawNi:D3}"
                : s.RawNi >= 0 ? $"n_{s.RawNi:D3}/S{s.SegIdx:D2}"
                : s.ExtRefThkId == 55 ? $"G_{s.ExtRefNodeId:D3}"
                : $"thk{s.ExtRefThkId}.{s.ExtRefNodeId}";
            string extra = "";
            if (s.ActionId > 0) extra += $" ⚔ ACT({s.ActionId})";
            if (s.ExtRefThkId == 55 && s.ActionId == 0) extra += $" → G_{s.ExtRefNodeId:D3}";
            lines.Add($"{marker}{where} {desc}{extra}");
        }
        return lines;
    }

    public static string CheckTypeName(int checkType, out bool hasParam)
    {
        hasParam = true;
        return checkType switch
        {
            0 => "always", 2 => "else/goto", 3 => "targetEnemy(P1)", 4 => "targetUnknown(P1,P2)",
            5 => "targetArea(P1)", 6 => "dist3D.leq(P1)", 7 => "dist3D.gt(P1)",
            8 => "dist2D.leq(P1)", 9 => "dist2D.gt(P1)", 10 => "dist3D_recalc.leq(P1)",
            11 => "dist3D_recalc.gt(P1)", 12 => "dist2D_recalc.leq(P1)", 13 => "dist2D_recalc.gt(P1)",
            14 => "vertDist.leq(P1)", 15 => "vertDist.gt(P1)", 16 => "above/below(P1)",
            19 => "above/below_area(P1)", 20 => "angle_ccw(P1,P2)", 21 => "angular_15(P1,P2)",
            22 => "angle_cw(P1,P2)", 23 => "angular_17(P1,P2)", 24 => "angular_18(P1,P2)",
            25 => "angular_19(P1,P2)", 26 => "angular_1A(P1,P2)", 27 => "angular_1B(P1,P2)",
            28 => "in_combat", 29 => "alert_out_of_combat", 30 => "enraged", 31 => "fatigued",
            32 => "poisoned", 33 => "defense_downed", 34 => "miasmaed", 35 => "hookable",
            36 => "target_on_part(P1)", 37 => "mounted", 38 => "mount_finisher_ready",
            39 => "mount_stabbed", 40 => "mount_staggered_twice", 42 => "target.pinned",
            43 => "enrage/fatigue_time(P1,P2)", 44 => "hp_percent.leq(P2)", 45 => "function#2D(P1)",
            46 => "target(P1)", 47 => "target_is(P1)", 55 => "quest_id(P1,P2)",
            93 => "function#5D", 94 => "clearTarget", 112 => "flying",
            118 => "part(P1).is_broken(P2)", 174 => "force_area_change", 177 => "force_area_change2",
            184 => "quest.is_rank(P1)", 191 => "in_map(P1).in_area(P2)",
            257 => "function#101", 258 => "function#102(P1)", 259 => "function#103",
            260 => "function#104", 262 => "function#106", 264 => "function#108",
            268 => "function#10C", 269 => "function#10D", 272 => "function#110(P1)",
            273 => "function#111", 274 => "function#112(P1,P2)", 275 => "function#113(P1,P2)",
            12288 => "heal/damage(P1,P2)", 12289 => "stamina(P1,P2)",
            12290 => "enrage/refresh(P1)", 12291 => "corpseDuration(P2)",
            >= 0x80 and <= 0xAB => $"RegVar{(checkType - 0x80)}",
            _ => $"unknown({checkType})"
        };
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ThkSegmentInfo
{
    public short RawNi;         // Binary Index from GetIndices (-1=unknown)
    public short SegIdx;        // segment index within the node
    public short CheckType;     // functionType
    public byte SegType;        // branchingControl: 0x2=if, 0x4=elif/else, 0x8=endif
    public byte Interrupt;      // flowControl: 0x4=repeat, 0x8=return, 0x80=reset
    public ushort ActionId;     // 0 = not a move segment
    public ushort ExtRefThkId;  // external THK ID (55=Global)
    public short ExtRefNodeId;  // raw Global Node ID (-1=none)
    public int Parameter1;      // P1
    public int Parameter2;      // P2

    public readonly string GetDescription()
    {
        if (ActionId > 0) return $"finale ACT({ActionId})";
        string chkName = ThkCycleData.CheckTypeName(CheckType, out _);
        return SegType switch
        {
            0x02 => $"if {chkName}",
            0x04 => CheckType == 2 ? "else" : $"elif {chkName}",
            0x08 => "endif", 0x10 => "endf",
            _ => chkName
        };
    }
}
