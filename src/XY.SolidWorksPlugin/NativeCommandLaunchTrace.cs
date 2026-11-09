using System;
using System.Diagnostics;
using System.Threading;

namespace XY.SolidWorksPlugin;

// Optional in-process diagnostics. No timer, allocation or logging when unused.
internal static class NativeCommandLaunchTrace
{
    internal static Action<string, string, long> Observer;

    internal static void Mark(string stage, string command)
    {
        var observer = Volatile.Read(ref Observer);
        if (observer == null) return;
        try { observer(stage, command, Stopwatch.GetTimestamp()); }
        catch { /* A diagnostic listener must never prevent a host command. */ }
    }
}
