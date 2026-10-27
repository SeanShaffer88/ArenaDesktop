namespace ArenaDesktop.Core;

/// <summary>
/// The app's command-line surface (S3 #23; completed by S11 #100):
/// the flags the app itself launches with. Unknown arguments are
/// tolerated (collected, never fatal) so a stray argument from a
/// shortcut or a future flag can never stop the app from starting.
/// Two flags are process-level information, not actions:
/// <see cref="ShowHelp"/> and <see cref="ShowVersion"/> are answered
/// by the launching process itself, before single-instance — they
/// never wake or disturb a running instance. Everything actionable
/// becomes a <see cref="LaunchIntent"/> (see
/// <see cref="LaunchIntent.FromCommandLine"/>), the one model the
/// running instance applies, whether the launch was a shortcut, a
/// Jump List task, a <c>arenadesktop://</c> link, or a script.
/// </summary>
public sealed class CommandLineOptions
{
    /// <summary>Launch quietly in the tray (the Start-with-Windows entry uses this).</summary>
    public bool Background { get; init; }

    /// <summary>CI mode: construct the real window, prove WebView2 initializes, exit.</summary>
    public bool SmokeTest { get; init; }

    /// <summary>Open the Settings window once the main window is up (S4 #33 — the taskbar Jump List's Settings task uses this).</summary>
    public bool OpenSettings { get; init; }

    /// <summary>Safe mode (S8 #72): start at the Arena home page (no last-page restore) and skip the update check. The app lock still applies. Holding Shift at launch is the other route in — the shell checks the keyboard state itself.</summary>
    public bool SafeMode { get; init; }

    /// <summary>The --profile value as given, or null when absent/valueless. Validated against the profile list by the caller (Profiles.ResolveLaunchProfile).</summary>
    public string? ProfileId { get; init; }

    /// <summary>Go to the Arena home page (S11 #100): on a cold start the launch opens at Home; on a running instance it navigates there.</summary>
    public bool OpenHome { get; init; }

    /// <summary>Open the Downloads window (S11 #100; the Jump List's Downloads-adjacent routes and scripts use this).</summary>
    public bool OpenDownloads { get; init; }

    /// <summary>Quick ask (S11 #97): show the window and focus the Arena page, ready to type. The tray item and the Jump List task ride this flag (--dictate stays as an alias).</summary>
    public bool QuickDictate { get; init; }

    /// <summary>Go to an Arena mode (ArenaDesktop v0.1.0): the normalized mode id from --battle / --direct / --side-by-side / --leaderboard / --mode &lt;id&gt;, validated by <see cref="ArenaModes.TryGet"/> at intent time. Unknown mode names land in <see cref="Unknown"/> instead, never here.</summary>
    public string? ArenaModeId { get; init; }

    /// <summary>Show the command-line usage and exit (S11 #100). Answered in the launching process, before single-instance.</summary>
    public bool ShowHelp { get; init; }

    /// <summary>Show the app version and exit (S11 #100). Answered in the launching process, before single-instance.</summary>
    public bool ShowVersion { get; init; }

    /// <summary>A <c>arenadesktop://</c> link handed to this launch (S11 #99), raw as received. Parsed and validated by <see cref="LaunchIntent.ParseProtocolUri"/> against the profile list — never opened as a web address.</summary>
    public string? ProtocolUri { get; init; }

    /// <summary>Everything that wasn't a recognized flag — ignored by the app, kept for logging/tests.</summary>
    public IReadOnlyList<string> Unknown { get; init; } = Array.Empty<string>();
}

public static class CommandLine
{
    public static CommandLineOptions Parse(IEnumerable<string> args)
    {
        var background = false;
        var smokeTest = false;
        var openSettings = false;
        var safeMode = false;
        var openHome = false;
        var openDownloads = false;
        var quickDictate = false;
        string? arenaModeId = null;
        var showHelp = false;
        var showVersion = false;
        string? profileId = null;
        string? protocolUri = null;
        var unknown = new List<string>();

        var list = args as IList<string> ?? args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var arg = list[i];
            if (arg.Equals("--background", StringComparison.OrdinalIgnoreCase))
            {
                background = true;
            }
            else if (arg.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase))
            {
                smokeTest = true;
            }
            else if (arg.Equals("--settings", StringComparison.OrdinalIgnoreCase))
            {
                openSettings = true;
            }
            else if (arg.Equals("--safe-mode", StringComparison.OrdinalIgnoreCase))
            {
                safeMode = true;
            }
            else if (arg.Equals("--home", StringComparison.OrdinalIgnoreCase))
            {
                openHome = true;
            }
            else if (arg.Equals("--downloads", StringComparison.OrdinalIgnoreCase))
            {
                openDownloads = true;
            }
            else if (arg.Equals("--dictate", StringComparison.OrdinalIgnoreCase)
                     || arg.Equals("--ask", StringComparison.OrdinalIgnoreCase))
            {
                quickDictate = true;
            }
            else if (arg.Equals("--battle", StringComparison.OrdinalIgnoreCase))
            {
                arenaModeId = ArenaModes.BattleId;
            }
            else if (arg.Equals("--direct", StringComparison.OrdinalIgnoreCase))
            {
                arenaModeId = ArenaModes.DirectId;
            }
            else if (arg.Equals("--side-by-side", StringComparison.OrdinalIgnoreCase))
            {
                arenaModeId = ArenaModes.SideBySideId;
            }
            else if (arg.Equals("--leaderboard", StringComparison.OrdinalIgnoreCase))
            {
                arenaModeId = ArenaModes.LeaderboardId;
            }
            else if (arg.Equals("--agent", StringComparison.OrdinalIgnoreCase))
            {
                arenaModeId = ArenaModes.AgentId;
            }
            else if (arg.Equals("--image", StringComparison.OrdinalIgnoreCase))
            {
                arenaModeId = ArenaModes.ImageId;
            }
            else if (arg.Equals("--code", StringComparison.OrdinalIgnoreCase))
            {
                arenaModeId = ArenaModes.CodeId;
            }
            else if (arg.Equals("--video", StringComparison.OrdinalIgnoreCase))
            {
                arenaModeId = ArenaModes.VideoId;
            }
            else if (arg.Equals("--search", StringComparison.OrdinalIgnoreCase))
            {
                arenaModeId = ArenaModes.SearchId;
            }
            else if (arg.Equals("--mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < list.Count && ArenaModes.TryGet(list[i + 1], out var namedMode))
                {
                    arenaModeId = namedMode.Id;
                    i++;
                }
                else
                {
                    unknown.Add(arg);
                }
            }
            else if (arg.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase))
            {
                if (ArenaModes.TryGet(arg["--mode=".Length..], out var namedMode))
                    arenaModeId = namedMode.Id;
                else
                    unknown.Add(arg);
            }
            else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
            }
            else if (arg.Equals("--version", StringComparison.OrdinalIgnoreCase))
            {
                showVersion = true;
            }
            else if (arg.StartsWith(ProtocolLinks.Scheme + ":", StringComparison.OrdinalIgnoreCase))
            {
                // A arenadesktop:// link (S11 #99). The first one wins;
                // a second link in the same launch is ignored, not an
                // error. Validation happens in ParseProtocolUri.
                protocolUri ??= arg.Trim();
            }
            else if (arg.Equals("--profile", StringComparison.OrdinalIgnoreCase))
            {
                // The value is the next argument — unless it's another
                // flag or there is none, in which case the launch just
                // uses the saved active profile.
                if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    profileId = list[++i].Trim();
                }
            }
            else if (arg.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg["--profile=".Length..].Trim();
                profileId = value.Length > 0 ? value : null;
            }
            else
            {
                unknown.Add(arg);
            }
        }

        return new CommandLineOptions
        {
            Background = background,
            SmokeTest = smokeTest,
            OpenSettings = openSettings,
            SafeMode = safeMode,
            ProfileId = profileId,
            OpenHome = openHome,
            OpenDownloads = openDownloads,
            QuickDictate = quickDictate,
            ArenaModeId = arenaModeId,
            ShowHelp = showHelp,
            ShowVersion = showVersion,
            ProtocolUri = protocolUri,
            Unknown = unknown,
        };
    }
}

/// <summary>
/// The documented command-line surface (S11 #100), shown by
/// <c>--help</c> and mirrored in the README's "Command line &amp;
/// links" section. Kept in Core so a test pins that every supported
/// flag and link route is actually documented here.
/// </summary>
public static class CommandLineUsage
{
    public static string Text(string version) =>
        $"ArenaDesktop {version} — command line\n" +
        "\n" +
        "  ArenaDesktop.exe [options] [arenadesktop:// link]\n" +
        "\n" +
        "Options:\n" +
        "  --profile <id>   Start in (or switch to) the named profile.\n" +
        "                   On a running instance the switch asks first, like the menu.\n" +
        "  --background     Start quietly in the tray (used by Start with Windows).\n" +
        "  --home           Open at the Arena home page.\n" +
        "  --settings       Open the Settings window.\n" +
        "  --downloads      Open the Downloads window.\n" +
        "  --ask, --dictate Open ArenaDesktop with Arena focused, ready to type (Quick ask).\n" +
        "  --battle         Go to Battle (Arena's home experience).\n" +
        "  --direct         Go to Direct Chat (its own page; pick the model there).\n" +
        "  --side-by-side   Go to Side by Side (its own page; pick both models there).\n" +
        "  --leaderboard    Go to the Leaderboard.\n" +
        "  --agent          Go to Agent Mode.\n" +
        "  --image          Go to Image.\n" +
        "  --code           Go to Code.\n" +
        "  --video          Go to Video.\n" +
        "  --search         Go to Web Search.\n" +
        "  --mode <id>      Go to a mode by id: battle, direct, side-by-side, leaderboard, agent, image, code, video, search.\n" +
        "  --safe-mode      Start at the home page and skip the update check.\n" +
        "                   The app lock still applies. Launch-time only: it has\n" +
        "                   no effect on an instance that is already running.\n" +
        "  --help           Show this text and exit.\n" +
        "  --version        Show the ArenaDesktop version and exit.\n" +
        "\n" +
        "Links (registered for the current Windows user):\n" +
        "  arenadesktop://open            Show the window\n" +
        "  arenadesktop://home            Go to the Arena home page\n" +
        "  arenadesktop://settings        Open Settings\n" +
        "  arenadesktop://downloads       Open Downloads\n" +
        "  arenadesktop://help            Open Help & troubleshooting\n" +
        "  arenadesktop://profile/<id>    Start in (or switch to) a profile\n" +
        "  arenadesktop://battle          Go to Battle\n" +
        "  arenadesktop://direct          Go to Direct Chat\n" +
        "  arenadesktop://side-by-side    Go to Side by Side\n" +
        "  arenadesktop://leaderboard     Go to the Leaderboard\n" +
        "  arenadesktop://agent           Go to Agent Mode\n" +
        "  arenadesktop://image           Go to Image\n" +
        "  arenadesktop://code            Go to Code\n" +
        "  arenadesktop://video           Go to Video\n" +
        "  arenadesktop://search          Go to Web Search\n" +
        "\n" +
        "Links can only name these actions — a arenadesktop:// link can never\n" +
        "carry a web address or open an arbitrary page. When ArenaDesktop is\n" +
        "already running, options and links are handed to the running instance;\n" +
        "while it is locked, they wait and apply after you unlock.";
}
