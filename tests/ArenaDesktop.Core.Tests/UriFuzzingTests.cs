using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// Fuzzing harness for URI handling. Thousands of generated inputs —
/// random assemblies of scheme fragments, hosts, routes, ports,
/// user-info, queries, fragments, control characters and mutations of
/// valid links — are fed to the two parsers untrusted input reaches:
/// <see cref="LaunchIntent.ParseProtocolUri"/> (arenadesktop:// links)
/// and <see cref="UrlPolicy"/> (web navigation).
///
/// No fuzzing library is used on purpose: the generator is a seeded
/// <see cref="Random"/>, so every run exercises exactly the same
/// corpus, a failure reproduces from its seed alone, and the test
/// project gains no dependency for what is a loop and a word list.
/// The invariants under test are the security ones:
///
/// - neither parser ever throws, whatever it is handed;
/// - a protocol link is either refused, or yields an intent whose
///   kind is defined, whose profile id (if any) is one of the valid
///   ids, and whose mode id (if any) names a real Arena mode — a
///   generated link can never smuggle an arbitrary action, profile
///   or address through;
/// - a web URL classifies to a defined disposition, and
///   <see cref="UrlPolicy.RedactForLog"/> never echoes a query or
///   fragment back.
/// </summary>
public class UriFuzzingTests
{
    private const int Iterations = 5_000;

    private static readonly string[] ProfileIds = { "default", "work", "play-2" };

    private static readonly string[] Fragments =
    {
        "arenadesktop://", "arenadesktop:", "arenadesktop", "ARENA DESKTOP",
        "https://", "http://", "file:///", "javascript:", "data:,", "blob:",
        "open", "home", "settings", "downloads", "battle-lab", "field-pass",
        "prompt-pad", "palette", "help", "battle", "direct", "side-by-side",
        "leaderboard", "agent", "image", "code", "video", "search",
        "profile", "profile/", "default", "work", "play-2", "unknown",
        "arena.ai", "arena.ai.evil.com", "evil.com", "localhost",
        "/", "//", "///", "/../", "%2e%2e", "%00", "%", "..", ".",
        ":", ":443", ":0", ":99999", "@", "user@", "user:pass@",
        "?", "?x=1", "?url=https://evil.example", "#", "#frag",
        " ", "\t", "\0", "\n", "\"", "'", "<script>", "&", "=", "+",
        "☃", "🎉", "ＡＲＥＮＡ", "a", "Z", "0", "-", "_", "~",
    };

    private static string Generate(Random rng)
    {
        var parts = rng.Next(1, 9);
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < parts; i++)
            text.Append(Fragments[rng.Next(Fragments.Length)]);
        return text.ToString();
    }

    private static string Mutate(Random rng, string seed)
    {
        var chars = seed.ToCharArray();
        var edits = rng.Next(1, 5);
        for (var i = 0; i < edits && chars.Length > 0; i++)
        {
            var at = rng.Next(chars.Length);
            switch (rng.Next(3))
            {
                case 0: chars[at] = (char)rng.Next(0, 0x300); break;
                case 1: chars[at] = Fragments[rng.Next(Fragments.Length)][0]; break;
                default:
                    return new string(chars, 0, at)
                        + Fragments[rng.Next(Fragments.Length)]
                        + new string(chars, at, chars.Length - at);
            }
        }
        return new string(chars);
    }

    private static readonly string[] ValidSeeds =
    {
        "arenadesktop://open",
        "arenadesktop://settings",
        "arenadesktop://battle-lab",
        "arenadesktop://prompt-pad",
        "arenadesktop://palette",
        "arenadesktop://battle",
        "arenadesktop://profile/default",
        "arenadesktop://profile/work",
        "https://arena.ai/chat/1",
        "https://arena.ai.evil.com/",
    };

    [Fact]
    public void ProtocolUriParser_NeverThrows_AndOnlyYieldsAllowlistedIntents()
    {
        var rng = new Random(0xA11CE);
        for (var i = 0; i < Iterations; i++)
        {
            var candidate = i % 2 == 0
                ? Generate(rng)
                : Mutate(rng, ValidSeeds[rng.Next(ValidSeeds.Length)]);

            ProtocolParseResult result;
            try
            {
                result = LaunchIntent.ParseProtocolUri(candidate, ProfileIds);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"ParseProtocolUri threw {ex.GetType().Name} on iteration {i} " +
                    $"for input [{candidate}]: {ex.Message}");
            }

            if (result.IsRefusal)
            {
                Assert.Null(result.Intent);
                Assert.False(string.IsNullOrWhiteSpace(result.Refusal));
                continue;
            }

            Assert.NotNull(result.Intent);
            var intent = result.Intent!;
            Assert.True(Enum.IsDefined(intent.Kind), $"undefined intent kind for [{candidate}]");
            if (intent.Kind == LaunchIntentKind.SwitchProfile)
                Assert.Contains(intent.ProfileId, ProfileIds);
            if (intent.Kind == LaunchIntentKind.GoArenaMode)
                Assert.True(ArenaModes.TryGet(intent.ModeId, out _),
                    $"unknown mode '{intent.ModeId}' accepted from [{candidate}]");
            // A link can never carry a web address through in any field.
            Assert.DoesNotContain("http", intent.ProfileId ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http", intent.ModeId ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void UrlPolicy_NeverThrows_AndRedactionNeverLeaksQueryOrFragment()
    {
        var rng = new Random(0xB0B);
        for (var i = 0; i < Iterations; i++)
        {
            var candidate = i % 2 == 0
                ? Generate(rng)
                : Mutate(rng, ValidSeeds[rng.Next(ValidSeeds.Length)]);

            LinkDisposition navigation, newWindow;
            string redacted;
            try
            {
                navigation = UrlPolicy.ClassifyNavigation(candidate);
                newWindow = UrlPolicy.ClassifyNewWindow(candidate);
                redacted = UrlPolicy.RedactForLog(candidate);
                _ = UrlPolicy.IsInAppHost(candidate);
                _ = UrlPolicy.IsAutoGrantedPermission(candidate, "notifications");
                _ = LaunchIntent.DescribeForLog(candidate);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"UrlPolicy threw {ex.GetType().Name} on iteration {i} " +
                    $"for input [{candidate}]: {ex.Message}");
            }

            Assert.True(Enum.IsDefined(navigation));
            Assert.True(Enum.IsDefined(newWindow));
            // The log form carries scheme/host/path only.
            Assert.DoesNotContain("?", redacted);
            Assert.DoesNotContain("#", redacted);
            Assert.True(redacted.Length <= 300, $"redaction unexpectedly long for [{candidate}]");
        }
    }

    [Fact]
    public void ProtocolUriParser_ExtremeLengths_DoNotThrow()
    {
        var rng = new Random(42);
        foreach (var length in new[] { 1_024, 16_384, 65_536 })
        {
            var chars = new char[length];
            for (var i = 0; i < length; i++)
                chars[i] = Fragments[rng.Next(Fragments.Length)][0];
            var candidate = "arenadesktop://" + new string(chars);
            var result = LaunchIntent.ParseProtocolUri(candidate, ProfileIds);
            Assert.True(result.IsRefusal, "an over-long generated route must be refused");
        }
    }
}
