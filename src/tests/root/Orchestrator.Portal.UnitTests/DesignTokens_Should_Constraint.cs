using System.Text.RegularExpressions;
using Shouldly;

namespace Orchestrator.Portal.UnitTests;

/// <summary>
/// Guards on the design system's stylesheet. Both of these exist because the property they protect is
/// invisible in review: a theme block that has drifted looks fine until someone switches themes, and a
/// hard-coded colour looks fine until the other theme renders it.
/// </summary>
public class DesignTokens_Should_Constraint
{
    static string Css() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "atlas.css"));

    /// <summary>Strip comments before parsing: the file's prose contains both ';' and ':', which shred a
    /// declaration split.</summary>
    static string WithoutComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    static string BlockAfter(string css, string marker)
    {
        var start = css.IndexOf(marker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"'{marker}' not found in the stylesheet");

        var open = css.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return css[(open + 1)..i];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after '{marker}'.");
    }

    static Dictionary<string, string> TokensIn(string block)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(block, @"(--[a-z0-9-]+)\s*:\s*([^;}]+)"))
        {
            tokens[m.Groups[1].Value] = Regex.Replace(m.Groups[2].Value.Trim(), @"\s+", " ");
        }

        return tokens;
    }

    /// <summary>
    /// The dark palette is written twice — once for the explicit <c>[data-theme="dark"]</c> override and
    /// once inside the <c>prefers-color-scheme</c> block — because plain CSS cannot alias one selector's
    /// declarations to another. That duplication is a documented trade, and this test is the thing that
    /// makes it survivable: it has already caught a real drift, where a contrast fix was applied to the
    /// attribute block only and the OS-preference path silently kept the failing values.
    /// </summary>
    [Fact]
    public void BothDarkThemeBlocks_Should_DefineIdenticalTokens()
    {
        var css = WithoutComments(Css());

        var attributeBlock = TokensIn(BlockAfter(css, "[data-theme=\"dark\"]"));
        var mediaBlock = TokensIn(BlockAfter(css, "@media (prefers-color-scheme: dark)"));

        attributeBlock.ShouldNotBeEmpty();

        // Compare as sorted "name: value" lines so a failure names the offending token instead of
        // dumping two dictionaries.
        static string[] Lines(Dictionary<string, string> t) =>
            [.. t.Select(kv => $"{kv.Key}: {kv.Value}").OrderBy(s => s, StringComparer.Ordinal)];

        Lines(mediaBlock)
            .ShouldBe(
                Lines(attributeBlock),
                "the two dark-theme blocks have drifted — a token was changed in one and not the other"
            );
    }

    /// <summary>Every light token needs a dark counterpart, or a view renders one theme's foreground on
    /// the other's background.</summary>
    [Fact]
    public void EveryThemedLightToken_Should_HaveADarkCounterpart()
    {
        var css = WithoutComments(Css());

        var light = TokensIn(BlockAfter(css, ":root {"));
        var dark = TokensIn(BlockAfter(css, "[data-theme=\"dark\"]"));

        // Tokens that are intentionally theme-independent: type, geometry, motion, and shell metrics
        // carry no colour, so a dark override would be noise.
        var themeIndependent = new[]
        {
            "--font-",
            "--fs-",
            "--r-",
            "--sp-",
            "--t-",
            "--sidebar-",
            "--header-",
        };

        var colourish = light.Keys.Where(k =>
            !themeIndependent.Any(prefix => k.StartsWith(prefix, StringComparison.Ordinal))
            && !k.StartsWith("--lightningcss", StringComparison.Ordinal)
        );

        foreach (var token in colourish)
        {
            dark.ShouldContainKey(token, $"{token} has no dark-theme value");
        }
    }

    /// <summary>
    /// Colour literals belong to the token layer. This is the mechanical half of the spec's "all visual
    /// values come from tokens" requirement — the half a reviewer would have to notice by eye.
    /// </summary>
    [Fact]
    public void NoColourLiteral_Should_AppearOutsideTheTokenLayer()
    {
        var css = WithoutComments(Css());

        // Everything from the first component section onward: the token blocks legitimately hold literals.
        var componentLayer = css[css.IndexOf("* {", StringComparison.Ordinal)..];

        // The checkbox tick is an inline SVG data URI, and a data URI cannot reference a CSS variable —
        // so its stroke colour has to be literal. It is the single allowed exception; anything else that
        // wants one should change this test deliberately.
        componentLayer = Regex.Replace(
            componentLayer,
            @"url\(""data:image/svg\+xml[^""]*""\)",
            "url(DATA-URI)"
        );

        var literals = Regex
            .Matches(
                componentLayer,
                @"#[0-9a-f]{3,8}\b|\boklch\(|\brgba?\(|\bhsla?\(",
                RegexOptions.IgnoreCase
            )
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        literals.ShouldBeEmpty(
            $"colour literals outside the token layer: {string.Join(", ", literals)} — use a token"
        );
    }

    /// <summary>
    /// A rule that removes the focus outline must supply a replacement, or the control becomes invisible
    /// to keyboard users. Checked structurally: every declaration block containing <c>outline: none</c>
    /// must also set a border or shadow, or have a sibling <c>:focus</c>/<c>:focus-visible</c> rule.
    /// </summary>
    [Fact]
    public void EveryOutlineRemoval_Should_HaveAReplacement()
    {
        var css = WithoutComments(Css());

        var offenders = new List<string>();
        foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
        {
            var selector = Regex.Replace(rule.Groups[1].Value.Trim(), @"\s+", " ");
            var body = rule.Groups[2].Value;

            if (!Regex.IsMatch(body, @"outline\s*:\s*none"))
            {
                continue;
            }

            // Acceptable in the same block: a shadow or border change that reads as focus.
            if (Regex.IsMatch(body, @"box-shadow\s*:|border-color\s*:"))
            {
                continue;
            }

            // Otherwise a focus rule must exist for this selector's base.
            var baseSelector = selector.Split(':')[0].Split(',')[0].Trim();
            if (
                baseSelector.Length > 0
                && Regex.IsMatch(
                    css,
                    Regex.Escape(baseSelector)
                        + @"[^{}]*:focus(-visible|-within)?[^{}]*\{[^{}]*(outline|box-shadow)"
                )
            )
            {
                continue;
            }

            // Or a :focus-within on an ancestor wrapper, which is how the search box does it.
            if (Regex.IsMatch(css, @":focus-within[^{}]*\{[^{}]*(box-shadow|border-color)"))
            {
                continue;
            }

            offenders.Add(selector);
        }

        offenders.ShouldBeEmpty(
            $"outline removed with no visible replacement: {string.Join(" | ", offenders)}"
        );
    }
}
