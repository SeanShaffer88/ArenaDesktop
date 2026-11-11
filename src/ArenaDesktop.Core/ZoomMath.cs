namespace ArenaDesktop.Core;

/// <summary>
/// Discrete zoom steps offered by the toolbar and shortcuts (v0.10.0,
/// S6 #57). The standard table uses fine 5% steps between 75% and 125%
/// — the range people actually nudge — and wider steps outside it. The
/// optional extended range (a Settings opt-in for low-vision users)
/// widens the bounds to 25%–500% with a few extra stops; the step table
/// inside the standard range is identical either way, so turning the
/// setting on never changes what Ctrl+± does at everyday zooms.
/// </summary>
public static class ZoomMath
{
    /// <summary>The standard-range step table (50%–200%).</summary>
    public static readonly IReadOnlyList<double> Steps = new[]
    {
        0.50, 0.67, 0.75, 0.80, 0.85, 0.90, 0.95, 1.00,
        1.05, 1.10, 1.15, 1.20, 1.25, 1.50, 1.75, 2.00,
    };

    /// <summary>The extended-range step table (25%–500%): the standard steps plus wider stops at both ends.</summary>
    public static readonly IReadOnlyList<double> ExtendedSteps = new[]
    {
        0.25, 0.33, 0.50, 0.67, 0.75, 0.80, 0.85, 0.90, 0.95, 1.00,
        1.05, 1.10, 1.15, 1.20, 1.25, 1.50, 1.75, 2.00, 2.50, 3.00, 4.00, 5.00,
    };

    public const double Min = 0.50;
    public const double Max = 2.00;
    public const double ExtendedMin = 0.25;
    public const double ExtendedMax = 5.00;

    public static IReadOnlyList<double> StepsFor(bool extendedRange) =>
        extendedRange ? ExtendedSteps : Steps;

    public static double MinFor(bool extendedRange) => extendedRange ? ExtendedMin : Min;
    public static double MaxFor(bool extendedRange) => extendedRange ? ExtendedMax : Max;

    /// <summary>Clamp an arbitrary factor (e.g. restored from settings) to the standard range.</summary>
    public static double Clamp(double factor) => Clamp(factor, extendedRange: false);

    /// <summary>Clamp an arbitrary factor to the standard or extended range; NaN/non-positive means 100%.</summary>
    public static double Clamp(double factor, bool extendedRange)
    {
        if (double.IsNaN(factor) || factor <= 0)
            return 1.0;
        return Math.Min(MaxFor(extendedRange), Math.Max(MinFor(extendedRange), factor));
    }

    /// <summary>The next step strictly above <paramref name="current"/> in the standard table, or the max when already there.</summary>
    public static double StepUp(double current) => StepUp(current, extendedRange: false);

    /// <summary>The next step strictly above <paramref name="current"/>, or the range max when already there.</summary>
    public static double StepUp(double current, bool extendedRange)
    {
        foreach (var step in StepsFor(extendedRange))
        {
            if (step > current + 0.001)
                return step;
        }
        return MaxFor(extendedRange);
    }

    /// <summary>The next step strictly below <paramref name="current"/> in the standard table, or the min when already there.</summary>
    public static double StepDown(double current) => StepDown(current, extendedRange: false);

    /// <summary>The next step strictly below <paramref name="current"/>, or the range min when already there.</summary>
    public static double StepDown(double current, bool extendedRange)
    {
        var steps = StepsFor(extendedRange);
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            if (steps[i] < current - 0.001)
                return steps[i];
        }
        return MinFor(extendedRange);
    }

    /// <summary>
    /// "100%", "67%", … for the toolbar label. Formatted against the
    /// widest range so an extended-range zoom never displays clamped.
    /// </summary>
    public static string FormatPercent(double factor) =>
        $"{(int)Math.Round(Clamp(factor, extendedRange: true) * 100)}%";

    /// <summary>
    /// The one-line report for a zoom change (enhancement pack 10).
    ///
    /// Zoom used to be completely silent: Ctrl+= and Ctrl+- changed the
    /// page with no status line and no announcement, so a keyboard user
    /// could not tell whether the key had done anything or how many steps
    /// they were from where they started. The preset menu already reported
    /// ("isn't in the current zoom range…"), which made the silence on the
    /// step keys worse rather than better.
    ///
    /// Wording is deliberately flat so a screen reader says it cleanly:
    /// "Zoom 125%" and, at a limit, why the press did nothing.
    /// </summary>
    /// <param name="factor">The zoom factor now in effect.</param>
    /// <param name="atLimit">
    /// True when the request could not move further (already at the
    /// smallest or largest step). The caller detects this by comparing the
    /// factor before and after; saying "Zoom 50%" when nothing changed
    /// reads as though it worked.
    /// </param>
    /// <param name="extendedRange">Whether the extended range is on, which is what decides where the limits are.</param>
    public static string Report(double factor, bool atLimit, bool extendedRange) =>
        atLimit
            ? $"Zoom {FormatPercent(factor)} — that's the {(factor <= MinFor(extendedRange) ? "smallest" : "largest")} this zoom range goes"
            : $"Zoom {FormatPercent(factor)}";
}
