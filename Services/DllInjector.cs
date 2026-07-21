using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;

namespace FatalisOverlay.Services;

/// <summary>
/// Deploys THKLogger.dll to the game's nativePC/plugins/ folder.
/// Stracker's Loader picks it up automatically on next MHW launch.
/// No CreateRemoteThread injection needed.
/// </summary>
public static class DllInjector
{
    private const string SharedMemoryName = "Local\\FatalisOverlay_THK_Path";

    private static string? _diagStatus;
    public static string DiagStatus => _diagStatus ?? "";

    /// <summary>
    /// Find MHW install directory by looking at the running process.
    /// </summary>
    public static string? FindMhwDirectory()
    {
        try
        {
            var procs = Process.GetProcessesByName("MonsterHunterWorld");
            if (procs.Length == 0) return null;
            return Path.GetDirectoryName(procs[0].MainModule?.FileName);
        }
        catch { return null; }
    }

    /// <summary>
    /// Find the game's nativePC/plugins/ directory.
    /// Falls back to common Steam install paths if game isn't running.
    /// </summary>
    public static string? FindPluginsDirectory()
    {
        var mhwDir = FindMhwDirectory();
        if (mhwDir != null)
        {
            var dir = Path.Combine(mhwDir, "nativePC", "plugins");
            if (Directory.Exists(dir)) return dir;
            // Create if the parent nativePC exists
            var nativePc = Path.Combine(mhwDir, "nativePC");
            if (Directory.Exists(nativePc))
            {
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
        return null;
    }

    /// <summary>
    /// Ensure THKLogger.dll is deployed to nativePC/plugins/.
    /// Returns null on success, or an error message string on failure.
    /// </summary>
    public static string? EnsureDeployed()
    {
        try
        {
            var pluginsDir = FindPluginsDirectory();
            if (pluginsDir == null)
            {
                _diagStatus = "❌ 找不到 nativePC/plugins/ (MHW在运行吗?)";
                return _diagStatus;
            }

            string destPath = Path.Combine(pluginsDir, "THKLogger.dll");

            // Extract embedded DLL
            var asm = Assembly.GetExecutingAssembly();
            string? resourceName = null;
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("THKLogger.dll", StringComparison.OrdinalIgnoreCase))
                { resourceName = name; break; }
            }

            if (resourceName == null)
            {
                _diagStatus = "❌ 内嵌DLL资源丢失,请重新下载";
                return _diagStatus;
            }

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                _diagStatus = "❌ 无法读取内嵌DLL";
                return _diagStatus;
            }

            // Check if existing DLL is the same version
            if (File.Exists(destPath))
            {
                var existing = new FileInfo(destPath);
                if (existing.Length == stream.Length)
                {
                    _diagStatus = IsSharedMemoryActive() ? "✅ DLL已就绪" : "✅ DLL已就绪,请重启MHW";
                    return null;
                }
            }

            // Write DLL to plugins folder
            using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fs);
            _diagStatus = $"✅ DLL已部署,请重启MHW";
            return null;
        }
        catch (Exception ex)
        {
            _diagStatus = $"❌ 部署失败: {ex.Message}";
            return _diagStatus;
        }
    }

    /// <summary>
    /// Check if the DLL is loaded in the game and the shared memory is active.
    /// </summary>
    public static bool IsSharedMemoryActive()
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName);
            using var accessor = mmf.CreateViewAccessor(0, 32);
            uint magic = accessor.ReadUInt32(0);
            uint flags = accessor.ReadUInt32(0x14);
            return magic == 0x54484B50 && (flags & 1) != 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Create the shared memory from C# so the DLL can open it.
    /// </summary>
    private static MemoryMappedFile? _persistentMmf;
    public static void CreateSharedMemory()
    {
        try
        {
            try { using var _ = MemoryMappedFile.OpenExisting(SharedMemoryName); return; }
            catch (FileNotFoundException) { }

            _persistentMmf = MemoryMappedFile.CreateNew(SharedMemoryName, 65536, MemoryMappedFileAccess.ReadWrite);
            using var accessor = _persistentMmf.CreateViewAccessor(0, 32);
            accessor.Write(0, 0x54484B50u);
            accessor.Write(4, 1u);
            accessor.Write(8, 0u);
            accessor.Write(12, 0u);
            accessor.Write(16, 0u);
            accessor.Write(20, 2u);  // bit1=tracking ON by default (matches DLL)
        }
        catch { }
    }
}
