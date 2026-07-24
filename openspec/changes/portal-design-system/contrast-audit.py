"""WCAG 2.1 contrast audit of the Atlas token palette.

OKLCH -> OKLab -> linear sRGB -> relative luminance -> contrast ratio.
Task 4.2 of the portal-design-system change. Design D8 explicitly declines to claim these
ratios are verified, so this script is the verification, not a restatement.
"""

import math
import re
import sys

# ---------------------------------------------------------------- colour maths


def oklch_to_linear_srgb(L, C, H_deg):
    h = math.radians(H_deg)
    a = C * math.cos(h)
    b = C * math.sin(h)

    l_ = L + 0.3963377774 * a + 0.2158037573 * b
    m_ = L - 0.1055613458 * a - 0.0638541728 * b
    s_ = L - 0.0894841775 * a - 1.2914855480 * b

    l, m, s = l_**3, m_**3, s_**3

    r = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s
    g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s
    bb = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s

    # Clamp for out-of-gamut results; the browser gamut-maps, and clamping is close enough
    # for a luminance comparison.
    return [min(1.0, max(0.0, v)) for v in (r, g, bb)]


def relative_luminance(linear_rgb):
    r, g, b = linear_rgb
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(lum_a, lum_b):
    hi, lo = max(lum_a, lum_b), min(lum_a, lum_b)
    return (hi + 0.05) / (lo + 0.05)


# ---------------------------------------------------------------- token parsing

OKLCH = re.compile(
    r"oklch\(\s*([\d.]+)%\s+([\d.]+)\s+([\d.]+)\s*(?:/\s*([\d.]+)\s*)?\)", re.I
)


def parse_colour(value):
    value = value.strip()
    if value.startswith("#"):
        hexv = value[1:]
        if len(hexv) == 3:
            hexv = "".join(c * 2 for c in hexv)
        srgb = [int(hexv[i : i + 2], 16) / 255 for i in (0, 2, 4)]
        # sRGB -> linear
        return [
            (c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4) for c in srgb
        ]
    m = OKLCH.match(value)
    if not m:
        return None
    if m.group(4) is not None:  # has alpha; not comparable without compositing
        return None
    return oklch_to_linear_srgb(float(m.group(1)) / 100, float(m.group(2)), float(m.group(3)))


def token_block(css, selector):
    i = css.index(selector)
    j = css.index("{", i)
    depth = 0
    for n in range(j, len(css)):
        if css[n] == "{":
            depth += 1
        elif css[n] == "}":
            depth -= 1
            if depth == 0:
                body = css[j + 1 : n]
                break
    out = {}
    for decl in body.split(";"):
        if ":" not in decl:
            continue
        k, _, v = decl.partition(":")
        k = k.strip()
        if k.startswith("--"):
            lum = parse_colour(v)
            if lum is not None:
                out[k] = relative_luminance(lum)
    return out


# ---------------------------------------------------------------- the pairs

# (foreground, background, context, px, bold) -- px/bold decide the AA threshold.
PAIRS = [
    ("--fg", "--bg", "body text on page", 14, False),
    ("--fg", "--surface", "body text on card", 14, False),
    ("--fg", "--surface-2", "text on card head", 13, False),
    ("--fg", "--surface-3", "text on subtle fill", 13, False),
    ("--fg-2", "--surface", "secondary text on card", 13, False),
    ("--fg-2", "--bg", "secondary text on page", 13, False),
    ("--fg-2", "--surface-2", "nav/btn label", 13, False),
    ("--fg-2", "--surface-3", "tag / count-pill / neutral badge", 12, False),
    ("--fg-muted", "--surface", "muted text on card", 13, False),
    ("--fg-muted", "--bg", "muted text on page", 12, False),
    ("--fg-muted", "--surface-2", "table header (th, 11px)", 11, False),
    ("--fg-muted", "--surface-3", "muted on subtle fill", 12, False),
    # --fg-subtle is decorative-only by decision (separators, ellipses). WCAG exempts pure
    # decoration, so these are reported for the record and not counted as failures. Raising it to
    # AA would collide with --fg-muted and collapse the four-step ramp to three.
    ("--fg-subtle", "--surface", "DECORATIVE separators on card", 12, False, True),
    ("--fg-subtle", "--bg", "DECORATIVE separators on page", 12, False, True),
    ("--fg-subtle", "--surface-2", "DECORATIVE chip/crumb separators", 11, False, True),
    ("--brand", "--surface", "link on card", 14, False),
    ("--brand", "--bg", "link on page", 14, False),
    ("--brand", "--brand-soft", "active nav item / filter chip", 13, False),
    ("--brand-on", "--brand", "primary button label", 13, False),
    ("--ok", "--ok-soft", "badge: succeeded", 12, False),
    ("--warn-text", "--warn-soft", "badge: stale gate", 12, False),
    ("--danger", "--danger-soft", "badge: failed", 12, False),
    ("--info", "--info-soft", "badge: running", 12, False),
    ("--violet", "--violet-soft", "badge: poison", 12, False),
    ("--orange-text", "--orange-soft", "badge: unknown skew", 12, False),
    ("--yellow-text", "--yellow-soft", "badge: yellow", 12, False),
    ("--fg", "--surface", "stat value (26px)", 26, False),
    ("--fg", "--bg", "page title h1 (20px/600)", 20, True),
]


def threshold(px, bold):
    # WCAG 2.1: large text is >=24px, or >=18.66px when bold.
    large = px >= 24 or (bold and px >= 18.66)
    return 3.0 if large else 4.5


def audit(name, tokens):
    print(f"\n{'=' * 78}\n{name}\n{'=' * 78}")
    print(f"{'ratio':>7}  {'need':>5}  {'':3} {'pair':<34} context")
    print("-" * 78)
    failures = []
    for pair in PAIRS:
        fg, bg, context, px, bold = pair[:5]
        decorative = len(pair) > 5 and pair[5]
        if fg not in tokens or bg not in tokens:
            print(f"{'--':>7}  {'--':>5}   ?  {fg} on {bg:<18} (unparsed)")
            continue
        ratio = contrast(tokens[fg], tokens[bg])
        need = threshold(px, bold)
        if decorative:
            mark = "n/a"
        else:
            ok = ratio >= need
            mark = "PASS" if ok else "FAIL"
            if not ok:
                failures.append((fg, bg, context, ratio, need))
        print(
            f"{ratio:>7.2f}  {'--' if decorative else f'{need:>5.1f}'}  {mark:<4} "
            f"{fg + ' on ' + bg:<34} {context}"
        )
    return failures


def main():
    css = open(sys.argv[1]).read()
    # Strip comments first: prose containing ';' (and there is plenty) otherwise shreds the
    # declaration split and silently drops the tokens that follow it.
    css = re.sub(r"/\*.*?\*/", "", css, flags=re.S)

    light = token_block(css, ":root {")
    # Dark only overrides some tokens; the rest cascade from :root. Auditing the raw block would
    # miss inherited pairs (e.g. --brand-on) and report them as unparsed.
    dark = {**light, **token_block(css, '[data-theme="dark"] {')}

    fails_light = audit("LIGHT THEME", light)
    fails_dark = audit("DARK THEME", dark)

    print(f"\n{'=' * 78}\nSUMMARY\n{'=' * 78}")
    for label, fails in (("light", fails_light), ("dark", fails_dark)):
        if not fails:
            print(f"{label}: all pairs meet WCAG AA for their rendered size.")
        else:
            print(f"{label}: {len(fails)} pair(s) BELOW AA:")
            for fg, bg, context, ratio, need in fails:
                print(f"   {fg} on {bg} = {ratio:.2f} (need {need:.1f}) -- {context}")


if __name__ == "__main__":
    main()
