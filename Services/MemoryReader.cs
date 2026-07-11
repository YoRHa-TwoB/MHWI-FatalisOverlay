using System.Runtime.InteropServices;

namespace FatalisOverlay.Services;

/// <summary>
/// Low-level memory reading via kernel32 ReadProcessMemory
/// </summary>
public static unsafe class MemoryReader
{
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, void* lpBuffer, int dwSize, out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    public static IntPtr OpenProcessHandle(int pid)
    {
        return OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
    }

    public static void CloseProcessHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            CloseHandle(handle);
    }

    public static T Read<T>(IntPtr handle, IntPtr address) where T : unmanaged
    {
        int size = sizeof(T);
        T result = default;
        ReadProcessMemory(handle, address, &result, size, out _);
        return result;
    }

    public static T Read<T>(IntPtr handle, long address) where T : unmanaged
        => Read<T>(handle, (IntPtr)address);

    public static IntPtr ReadPointer(IntPtr handle, IntPtr address)
        => Read<IntPtr>(handle, address);

    public static IntPtr ReadPointer(IntPtr handle, long address)
        => Read<IntPtr>(handle, (IntPtr)address);

    public static string ReadString(IntPtr handle, IntPtr address, int maxLen = 64)
    {
        byte[] buffer = new byte[maxLen];
        ReadProcessMemory(handle, address, buffer, maxLen, out _);
        int nullTerm = Array.IndexOf(buffer, (byte)0);
        if (nullTerm >= 0)
            return System.Text.Encoding.ASCII.GetString(buffer, 0, nullTerm);
        return System.Text.Encoding.ASCII.GetString(buffer);
    }

    /// <summary>
    /// Follow a pointer chain: [[address] + offsets[0]] + offsets[1]] ... then read T at final position
    /// </summary>
    public static T ReadChain<T>(IntPtr handle, long baseAddress, params int[] offsets) where T : unmanaged
    {
        IntPtr current = (IntPtr)baseAddress;

        for (int i = 0; i < offsets.Length - 1; i++)
        {
            current = ReadPointer(handle, current);
            if (current == IntPtr.Zero) return default;
            current = (IntPtr)(current.ToInt64() + offsets[i]);
        }

        // Last offset: read pointer, then add final offset, then read T
        if (offsets.Length > 0)
        {
            current = ReadPointer(handle, current);
            if (current == IntPtr.Zero) return default;
            current = (IntPtr)(current.ToInt64() + offsets[^1]);
        }

        return Read<T>(handle, current);
    }

    /// <summary>
    /// Follow pointer chain to get an address (returns final address without reading a value)
    /// </summary>
    public static IntPtr ResolveAddress(IntPtr handle, long baseAddress, params int[] offsets)
    {
        IntPtr current = (IntPtr)baseAddress;

        foreach (int offset in offsets)
        {
            current = ReadPointer(handle, current);
            if (current == IntPtr.Zero) return IntPtr.Zero;
            current = (IntPtr)(current.ToInt64() + offset);
        }

        return current;
    }

    /// <summary>
    /// Read an array of T at the given address
    /// </summary>
    public static T[] ReadArray<T>(IntPtr handle, IntPtr address, int count) where T : unmanaged
    {
        var result = new T[count];
        int size = sizeof(T);
        byte[] buffer = new byte[size * count];
        ReadProcessMemory(handle, address, buffer, buffer.Length, out _);
        for (int i = 0; i < count; i++)
            result[i] = MemoryMarshal.Read<T>(buffer.AsSpan(i * size, size));
        return result;
    }
}
