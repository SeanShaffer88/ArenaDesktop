namespace ArenaDesktop.Core;

// ───────────────────────── write coalescing (hardening pack) ─────────────────────────

/// <summary>
/// Decides when a frequently-requested save should actually touch the
/// disk (hardening pack).
///
/// Why this exists: <c>SaveSettings</c> runs on every successful
/// navigation — that is what keeps "reopen the last page" honest — and
/// each call serializes the whole settings object, writes a temp file,
/// swaps it in, and then asks the backup service whether a backup is
/// due. Paging around Arena quickly turns that into a burst of
/// identical-shaped writes to the same file on the UI thread.
///
/// The rule is deliberately simple, because a settings save that is
/// LOST is much worse than one that is late:
/// - the first request always writes immediately (a quiet app must never
///   sit on an unsaved change);
/// - a request arriving within <see cref="MinInterval"/> of the last
///   write is DEFERRED rather than dropped — the caller keeps a timer
///   running and asks <see cref="IsDue"/> until it says yes;
/// - <see cref="Flush"/> forces the deferred write out, for the paths
///   that cannot wait (window closing, profile switch, lock).
///
/// This type holds no timer and does no I/O: the shell owns the
/// DispatcherTimer, the tests own the clock.
/// </summary>
public sealed class SaveCoalescer
{
    /// <summary>The shortest gap between two writes caused by ordinary (non-forced) requests.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2);

    private DateTimeOffset? _lastWriteUtc;
    private bool _pending;

    /// <summary>True when a request was deferred and still has not been written.</summary>
    public bool IsPending => _pending;

    /// <summary>
    /// A save was requested. True = write it now (and the caller should
    /// call <see cref="MarkWritten"/>); false = it was folded into a
    /// deferred write the caller must flush later.
    /// </summary>
    public bool Request(DateTimeOffset nowUtc)
    {
        if (_lastWriteUtc is not { } last || nowUtc - last >= MinInterval)
        {
            _pending = false;
            return true;
        }
        _pending = true;
        return false;
    }

    /// <summary>
    /// A deferred write is due once the interval has passed. False while
    /// nothing is pending, so an idle app never writes by itself.
    /// </summary>
    public bool IsDue(DateTimeOffset nowUtc) =>
        _pending && (_lastWriteUtc is not { } last || nowUtc - last >= MinInterval);

    /// <summary>
    /// Take the pending flag without deciding anything — used by a flush
    /// path that writes unconditionally. Returns true when there was
    /// something to write.
    /// </summary>
    public bool TakePending()
    {
        var had = _pending;
        _pending = false;
        return had;
    }

    /// <summary>A write happened at <paramref name="nowUtc"/>: restart the interval and clear the pending flag.</summary>
    public void MarkWritten(DateTimeOffset nowUtc)
    {
        _lastWriteUtc = nowUtc;
        _pending = false;
    }

    /// <summary>Forget everything — used when the settings were replaced wholesale (an import or a reset).</summary>
    public void Reset()
    {
        _lastWriteUtc = null;
        _pending = false;
    }
}

// ───────────────────────── settings-write report (hardening pack) ─────────────────────────

/// <summary>
/// A tiny tally of what the settings writer did, shown in Diagnostics
/// (hardening pack). Counts only — no content, no paths — so it cannot
/// leak anything and is cheap to keep for the life of the process.
/// </summary>
public sealed class SettingsWriteStats
{
    public int Writes { get; private set; }
    public int Deferred { get; private set; }
    public int Coalesced { get; private set; }
    public int Failures { get; private set; }

    public void NoteWrite() => Writes++;
    public void NoteDeferred() => Deferred++;
    public void NoteCoalesced() => Coalesced++;
    public void NoteFailure() => Failures++;

    /// <summary>The Diagnostics line, or null when nothing has been written yet (so a fresh run says nothing).</summary>
    public string? ReportLine => Writes + Deferred + Failures == 0
        ? null
        : $"  Settings writes {Writes} · deferred {Deferred} · coalesced {Coalesced} · failures {Failures}";
}
