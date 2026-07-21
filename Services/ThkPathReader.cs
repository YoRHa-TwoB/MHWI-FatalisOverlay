using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using FatalisOverlay.Models;

namespace FatalisOverlay.Services;

/// <summary>
/// Reads THK behavior tree path cycles from the shared memory ring buffer
/// written by THKLogger.dll (loaded by Stracker's Loader into MHW process).
/// </summary>
public class ThkPathReader : IDisposable
{
    // ── Shared memory layout constants (must match DLL) ──
    private const uint Magic = 0x54484B50;
    private const int ShmemSize = 65536;
    private const int HeaderSize = 0x28;  // must match DLL's ShmemLayout!
    private const int MaxCycles = 5;
    private const int MaxSegments = 512;
    private const int MaxActions = 8;

    // CycleEntryHeader: 44 bytes
    //   uint32 cycle_id         (4)
    //   uint32 quest_elapsed_ms (4)
    //   uint16 segment_count    (2)
    //   uint16 action_count     (2)
    //   uint32 action_ids[8]   (32)
    // THKPathSegment: 14 bytes
    // Max cycle = 44 + 512*14 = 7212 bytes
    // 5 cycles = 36060 bytes (fits in 62KB data area)

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private uint _lastReadCycle;
    private bool _disposed;

    public bool IsConnected => _mmf != null;

    /// <summary>
    /// Try to connect to the shared memory created by THKLogger.dll.
    /// Returns true if connected successfully.
    /// </summary>
    public bool TryConnect()
    {
        if (_mmf != null) return true;

        try
        {
            _mmf = MemoryMappedFile.OpenExisting("Local\\FatalisOverlay_THK_Path");
            _accessor = _mmf.CreateViewAccessor(0, ShmemSize);

            // Verify magic
            uint magic = _accessor.ReadUInt32(0);
            if (magic != Magic)
            {
                Disconnect();
                return false;
            }

            // Start reading from cycle 0; CommitCycle uses sequential IDs (0,1,2,…).
            // Stale slots are rejected by ReadCycle's storedCycleId mismatch check.
            _lastReadCycle = 0;
            return true;
        }
        catch (FileNotFoundException)
        {
            // Shared memory not created yet (DLL not injected or hook not active)
            return false;
        }
        catch
        {
            Disconnect();
            return false;
        }
    }

    /// <summary>
    /// Read all new cycles since last call. Thread-safe.
    /// </summary>
    public List<ThkCycleData> ReadNewCycles()
    {
        var result = new List<ThkCycleData>();
        if (_accessor == null || _mmf == null) return result;

        try
        {
            uint writeCycle = _accessor.ReadUInt32(0x08);

            while (_lastReadCycle < writeCycle && result.Count < 10)
            {
                var cycle = ReadCycle(_lastReadCycle);
                if (cycle != null)
                    result.Add(cycle);
                _lastReadCycle++;
            }

            if (result.Count > 0)
                _accessor.Write(0x0C, _lastReadCycle);

            uint overflow = _accessor.ReadUInt32(0x10);
            if (overflow > 0)
            {
                _accessor.Write(0x10, (uint)0);
                _lastReadCycle = writeCycle > (uint)MaxCycles ? writeCycle - (uint)MaxCycles : 0;
            }

            return result;
        }
        catch
        {
            return result;
        }
    }

    private ThkCycleData? ReadCycle(uint cycleId)
    {
        if (_accessor == null) return null;

        uint slot = cycleId % MaxCycles;
        int slotOffset = HeaderSize + (int)slot * GetMaxCycleSize();

        // Read header
        uint storedCycleId = _accessor.ReadUInt32(slotOffset);
        if (storedCycleId != cycleId)
        {
            // Slot has been overwritten (buffer wrap) — skip
            return null;
        }

        uint elapsedMs = _accessor.ReadUInt32(slotOffset + 4);
        ushort segCount = _accessor.ReadUInt16(slotOffset + 8);
        ushort actionCount = _accessor.ReadUInt16(slotOffset + 10);

        if (segCount == 0 || segCount > MaxSegments) return null;

        var cycle = new ThkCycleData
        {
            CycleId = cycleId,
            QuestElapsedSec = elapsedMs / 1000.0f,
        };

        // Read action IDs
        for (int i = 0; i < actionCount && i < MaxActions; i++)
        {
            uint aid = _accessor.ReadUInt32(slotOffset + 12 + i * 4);
            if (aid > 0) cycle.ActionIds.Add(aid);
        }

        // Read segments (start after CycleHeaderSize = 44 bytes)
        int segStart = slotOffset + CycleHeaderSize;
        const int segSize = 22; // PathSeg: rawNi+si+chk+st+it+act+extThk+extNode+p1+p2

        for (int i = 0; i < segCount; i++)
        {
            int offset = segStart + i * segSize;
            if (offset + segSize > ShmemSize) break;

            var seg = new ThkSegmentInfo
            {
                RawNi       = _accessor.ReadInt16(offset),
                SegIdx      = _accessor.ReadInt16(offset + 2),
                CheckType   = _accessor.ReadInt16(offset + 4),
                SegType     = _accessor.ReadByte(offset + 6),
                Interrupt   = _accessor.ReadByte(offset + 7),
                ActionId    = _accessor.ReadUInt16(offset + 8),
                ExtRefThkId = _accessor.ReadUInt16(offset + 10),
                ExtRefNodeId = _accessor.ReadInt16(offset + 12),
                Parameter1  = _accessor.ReadInt32(offset + 14),
                Parameter2  = _accessor.ReadInt32(offset + 18),
            };
            cycle.Segments.Add(seg);
        }

        return cycle;
    }

    /// <summary>
    /// Check whether the DLL hook is active (via state_flags in shared memory).
    /// </summary>
    public bool IsHookActive
    {
        get
        {
            if (_accessor == null) return false;
            try
            {
                uint flags = _accessor.ReadUInt32(0x14);
                return (flags & 1) != 0;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Enable or disable THK tracking at the DLL level.
    /// When disabled, the DLL stops accumulating segments entirely (zero overhead).
    /// </summary>
    public bool TrackingEnabled
    {
        set
        {
            if (_accessor == null) return;
            try
            {
                uint flags = _accessor.ReadUInt32(0x14);
                if (value)
                    flags |= 2;   // set bit1
                else
                    flags &= ~2u;  // clear bit1
                _accessor.Write(0x14, flags);
            }
            catch { }
        }
    }

    /// <summary>
    /// Get latest quest elapsed time from shared memory (updated by DLL).
    /// </summary>
    public uint QuestElapsedMs
    {
        get
        {
            if (_accessor == null) return 0;
            try { return _accessor.ReadUInt32(0x18); }
            catch { return 0; }
        }
    }

    /// <summary>Diagnostic: total hook calls by DLL (offset 0x1C).</summary>
    public uint HookCallCount => _accessor != null ? SafeReadU32(0x1C) : 0;
    /// <summary>Diagnostic: last node index seen by hook (offset 0x20).</summary>
    public int LastNi => _accessor != null ? (int)SafeReadU32(0x20) : -999;
    /// <summary>Diagnostic: last node count seen by hook (offset 0x24).</summary>
    public int LastNc => _accessor != null ? (int)SafeReadU32(0x24) : -999;
    /// <summary>Diagnostic: write_cycle from DLL.</summary>
    public uint WriteCycle => _accessor != null ? SafeReadU32(0x08) : 0;

    private uint SafeReadU32(int offset)
    {
        try { return _accessor!.ReadUInt32(offset); }
        catch { return 0xDEAD; }
    }

    private const int CycleHeaderSize = 44; // CycleEntryHeader (44 bytes)

    private static int GetMaxCycleSize()
    {
        return CycleHeaderSize + MaxSegments * 22;  // header (44) + segments (22 bytes each)
    }

    public void Disconnect()
    {
        _accessor?.Dispose();
        _accessor = null;
        _mmf?.Dispose();
        _mmf = null;
        _lastReadCycle = 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Disconnect();
            _disposed = true;
        }
    }
}
