using System.Runtime.InteropServices;

namespace Dolly;

/// Chiamate native di Windows: un "job object" che chiude mpv se l'app muore (anche con un arresto forzato) e il blocco della sospensione durante la proiezione.
static class Win32
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attrs, string? name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint size);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    [StructLayout(LayoutKind.Sequential)] struct BasicLimit { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinWs, MaxWs; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint Priority, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong a, b, c, d, e, f; }
    [StructLayout(LayoutKind.Sequential)] struct ExtLimit { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcMem, JobMem, PeakProc, PeakJob; }

    static IntPtr job;
    /// mpv entra nel job: se l'app termina, Windows chiude mpv insieme (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE).
    public static void BindToJob(System.Diagnostics.Process p)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (job == IntPtr.Zero)
            {
                job = CreateJobObject(IntPtr.Zero, null);
                var info = new ExtLimit(); info.Basic.LimitFlags = 0x2000;
                int size = Marshal.SizeOf<ExtLimit>(); IntPtr buf = Marshal.AllocHGlobal(size);
                try { Marshal.StructureToPtr(info, buf, false); SetInformationJobObject(job, 9, buf, (uint)size); } finally { Marshal.FreeHGlobal(buf); }
            }
            AssignProcessToJobObject(job, p.Handle);
        }
        catch (Exception e) { U.Log("job object:", e.Message); }
    }
    /// Il PC non deve né spegnere lo schermo né dormire mentre l'app è aperta.
    public static void KeepAwake(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        const uint CONTINUOUS = 0x80000000, SYSTEM = 1, DISPLAY = 2;
        try { SetThreadExecutionState(on ? CONTINUOUS | SYSTEM | DISPLAY : CONTINUOUS); } catch { }
    }
}
