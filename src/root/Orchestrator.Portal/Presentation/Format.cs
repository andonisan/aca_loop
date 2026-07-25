using System.Globalization;

namespace Orchestrator.Portal.Presentation;

/// <summary>
/// Display formatting for the read-model views. Centralized so the same quantity reads the same on every
/// page, and so the judgements below are made once.
/// </summary>
static class Format
{
    /// <summary>
    /// Invariant culture and an explicit UTC suffix throughout. Operators of these stacks are in
    /// different timezones and read each other's screenshots in incident channels, so a timestamp that
    /// silently renders in the viewer's locale is a timestamp two people can disagree about.
    /// </summary>
    internal static string Absolute(DateTimeOffset? at) =>
        at is null
            ? "—"
            : at.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + "Z";

    /// <summary>
    /// Durations are read to compare runs, so the unit steps up only when the smaller one stops being
    /// informative.
    /// <para>
    /// Invariant, for the same reason <see cref="Absolute"/> is: on a Spanish-locale host this rendered
    /// "2,5 s" while an English one rendered "2.5 s", which is the same cross-operator ambiguity a
    /// locale-rendered timestamp creates. Every interpolation here is pinned rather than only the one with
    /// a decimal separator today, so adding a fractional format later cannot silently reintroduce it.
    /// </para>
    /// </summary>
    internal static string Duration(TimeSpan? duration) =>
        duration switch
        {
            null => "—",
            { TotalSeconds: < 1 } d => Invariant($"{d.TotalMilliseconds:F0} ms"),
            { TotalMinutes: < 1 } d => Invariant($"{d.TotalSeconds:F1} s"),
            { TotalHours: < 1 } d => Invariant($"{(int)d.TotalMinutes}m {d.Seconds:D2}s"),
            { } d => Invariant($"{(int)d.TotalHours}h {d.Minutes:D2}m"),
        };

    /// <summary>Cache age. Coarse on purpose: the useful distinction is "current" versus "stale enough to
    /// question", not the exact second.</summary>
    internal static string Age(TimeSpan age) =>
        age switch
        {
            { TotalSeconds: < 5 } => "just now",
            { TotalMinutes: < 1 } d => Invariant($"{(int)d.TotalSeconds}s ago"),
            { TotalHours: < 1 } d => Invariant($"{(int)d.TotalMinutes}m ago"),
            { } d => Invariant($"{(int)d.TotalHours}h ago"),
        };

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
