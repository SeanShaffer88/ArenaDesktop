using System.Runtime.InteropServices;

namespace ArenaDesktop;

/// <summary>
/// How long the PC has gone without keyboard or mouse input, for the idle
/// lock (enhancement pack 10).
///
/// <c>GetLastInputInfo</c> is the same call the OS uses for its own
/// screensaver and idle timers, and it reports input ANYWHERE on the
/// session — not just in this window. That is the right question for a
/// lock: a person typing in another app is present, and locking
/// ArenaDesktop because they were writing an email would be a bug.
///
/// Everything here is best-effort. A failed call returns null, and the
/// caller treats null as "don't know" rather than "idle forever" — see
/// <see cref="Core.IdleLock.Reason.UnknownIdle"/>. Never throwing matters
/// more than being exact on a 49-day uptime edge case (the tick count is a
/// 32-bit millisecond counter), which is why the subtraction is done on
/// unsigned integers and the result is sanity-checked.
/// </summary>
internal static class IdleTime
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    /// <summary>
    /// Time since the last input, or null when the OS won't say. A value
    /// that comes back nonsensical (the last input is "after" now, which
    /// happens across the 49-day wrap) is reported as null rather than
    /// converted into a huge idle time that would lock the app by
    /// accident.
    /// </summary>
    public static TimeSpan? SinceLastInput()
    {
        try
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info))
                return null;
            var now = GetTickCount();
            // Unsigned subtraction is wrap-safe: if the counter rolled over
            // between the two reads, the difference is still correct.
            var elapsed = now - info.Time;
            // A gap this large is a wrap artefact or a broken driver, not
            // real idleness.
            if (elapsed > int.MaxValue)
                return null;
            return TimeSpan.FromMilliseconds(elapsed);
        }
        catch
        {
            // A desktop with no interactive session (a service, a locked
            // RDP teardown) can refuse the call outright.
            return null;
        }
    }
}
