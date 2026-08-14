#!/usr/bin/env python3
"""Build EQBuddy's bundled text+icon font from OFL-licensed Noto sources.

Why this exists: under Wine/CrossOver, WPF's DirectWrite text stack never
performs font fallback for codepoints above U+FFFF, and Wine ships no Segoe
fonts at all — so every symbol/emoji glyph EQBuddy renders (arrows, gear
icons, warning triangles, the 🦴/🐛/🔍 status glyphs, etc.) shows up as a
☐ box under CrossOver even though it renders fine on native Windows.

First attempt (kept here so nobody retries it): bundle an icon-*only* font
and put it first in the WPF FontFamily list ("./Fonts/#EQBuddy Icons, Segoe
UI Variable Text, Segoe UI"), letting Latin text still fall through to
Segoe for the rest of the string. Field-tested under Wine and it does not
work: Wine's DirectWrite resolves a run's font by consulting only the
*primary* family's cmap — it never walks the rest of an explicit WPF family
list character-by-character, and there is no system fallback to catch what
the primary font is missing either. The result was correct icon glyphs and
every Latin letter boxed. The only font Wine will actually consult per
character is the one WPF resolved as primary for that run, so that font has
to carry the icons *and* the text.

Fix: bundle one font, family "EQBuddy Sans", built on a Noto Sans Regular
base (full Latin/Latin-Extended/punctuation/currency/letterlike coverage,
with its OpenType layout kept — tabular figures matter, since the app sets
Typography.NumeralAlignment=Tabular globally) with the same icon subsets
grafted in as extra glyphs. Listed first, it is the *only* font Wine's
DirectWrite consults, so both text and icons resolve without ever needing
fallback.

Pipeline:
  1. Scan src/EQBuddy, src/EQBuddy.UI.Shared, src/EQBuddy.Core (.cs/.xaml,
     skipping obj/bin) for every codepoint >= U+2190 the app can emit —
     these are the icon glyphs.
  2. Download five pinned, SHA-256-verified Noto source fonts (cached in a
     temp dir): Noto Sans Regular as the text base, plus the four icon
     sources, each needed icon codepoint assigned to the first source that
     covers it, in priority order.
  3. Subset the text base to five fixed Latin/punctuation/currency/
     letterlike ranges (intersected with its actual cmap) keeping the
     OpenType layout features the app depends on (tnum/kern/liga); subset
     each icon source to its assigned codepoints with layout closure
     disabled (single standalone glyphs, not shaped prose). Scale every
     subset to 1000 units/em and merge them into one font, text base first.
  4. Graft in empty, zero-width glyphs for the variation selectors U+FE0E
     / U+FE0F (the app's strings carry them; they must never box).
  5. Normalize vertical metrics to Noto Sans Regular's own values — it's
     the text base now, so its line-height contract is the one WPF should
     see, not one of the symbol sources'.
  6. Rewrite the name table (family "EQBuddy Sans", OFL license/credit
     records — the OFL forbids reusing the reserved name "Noto" in a
     derivative, which "EQBuddy Sans" satisfies) and verify the result
     covers everything requested before writing it out.

Run with the fonttools venv active:
    python3 scripts/build-icon-font.py

Outputs (checked into the repo):
    src/EQBuddy/Fonts/EQBuddySans.ttf
    src/EQBuddy/Fonts/EQBuddySans.codepoints.txt
"""

import hashlib
import re
import sys
import tempfile
import urllib.request
from pathlib import Path

from fontTools import subset
from fontTools.merge import Merger
from fontTools.ttLib import TTFont
from fontTools.ttLib.scaleUpem import scale_upem
from fontTools.ttLib.tables._g_l_y_f import Glyph
from fontTools.varLib.instancer import instantiateVariableFont

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
FONTS_DIR = REPO / "src" / "EQBuddy" / "Fonts"
CACHE_DIR = Path(tempfile.gettempdir()) / "eqbuddy-icon-font-src"

SCAN_ROOTS = ["src/EQBuddy", "src/EQBuddy.UI.Shared", "src/EQBuddy.Core"]
SCAN_EXTS = {".cs", ".xaml"}
SKIP_DIRS = {"obj", "bin"}

MIN_CODEPOINT = 0x2190
EXCLUDED_CODEPOINTS = {0xFE0E, 0xFE0F, 0xFEFF, 0xFFFF}
VARIATION_SELECTORS = (0xFE0E, 0xFE0F)

FAMILY_NAME = "EQBuddy Sans"
PS_NAME = "EQBuddySans-Regular"

# Pinned Noto sources. URLs point at each project's upstream repo; the
# SHA-256 (computed once, by hand, against the exact bytes fetched) is the
# actual pin — a changed upstream file fails the hash check loudly rather
# than silently baking in different glyphs.
#
# "text" is the base font the rest merge into (see module docstring for why
# a bare icon font isn't enough under Wine). NotoEmoji ships only as a
# variable font (wght 300-700); we instance it to Regular (wght=400)
# ourselves rather than depend on a prebuilt static file.
SOURCES = {
    "text": dict(
        url="https://raw.githubusercontent.com/notofonts/notofonts.github.io/main/fonts/NotoSans/hinted/ttf/NotoSans-Regular.ttf",
        sha256="478c558ea716033cd60c03438f628dfa75694dcf6b5f6d505a2f05fd2b4f3823",
        variable=False,
    ),
    "symbols2": dict(
        url="https://raw.githubusercontent.com/notofonts/notofonts.github.io/main/fonts/NotoSansSymbols2/hinted/ttf/NotoSansSymbols2-Regular.ttf",
        sha256="c4a0a80f0041ce4be81e2478faad22776d23edb98ae3f0d19bd37044820ecf9d",
        variable=False,
    ),
    "math": dict(
        url="https://raw.githubusercontent.com/notofonts/notofonts.github.io/main/fonts/NotoSansMath/hinted/ttf/NotoSansMath-Regular.ttf",
        sha256="d51afd5739c7ba6c44fcab35a88160e25dfb69a2d4ad0bd99533f8d894af1f96",
        variable=False,
    ),
    "symbols": dict(
        url="https://raw.githubusercontent.com/notofonts/notofonts.github.io/main/fonts/NotoSansSymbols/hinted/ttf/NotoSansSymbols-Regular.ttf",
        sha256="d0e98e9a2c046594c5021437273943be7e79e0fd980fde125279e22302212595",
        variable=False,
    ),
    "emoji": dict(
        url="https://raw.githubusercontent.com/google/fonts/main/ofl/notoemoji/NotoEmoji%5Bwght%5D.ttf",
        sha256="de6c18832938afc99caf132b39d6a30a19bac7f2e812e28db2535b4608d27551",
        variable=True,
    ),
}

# For a BMP icon codepoint, try sources in this order and take the first
# cmap hit. Supplementary-plane codepoints (pictographs) only ever live in
# the monochrome emoji font, so they skip straight to "emoji". The text
# base is handled separately (fixed ranges, not priority-assigned).
BMP_ORDER = ["symbols2", "math", "symbols", "emoji"]

# Fixed text coverage: Basic Latin, Latin-1 Supplement + Latin Extended-A/B,
# General Punctuation, Currency Symbols, Letterlike Symbols. Intersected
# with Noto Sans's actual cmap at build time — this is a coverage request,
# not a guarantee every codepoint in range exists.
TEXT_RANGES = [
    (0x0020, 0x007E),
    (0x00A0, 0x024F),
    (0x2000, 0x206F),
    (0x20A0, 0x20CF),
    (0x2100, 0x214F),
]

# The OpenType layout features EQBuddy actually depends on: tabular figures
# (the app sets Typography.NumeralAlignment=Tabular globally, so digits
# must have fixed-width variants), kerning, and standard ligatures. Every
# other default feature (small caps, fractions, stylistic sets, ...) goes
# unused and is dropped along with its glyphs.
TEXT_LAYOUT_FEATURES = ["tnum", "kern", "liga"]

# Per-source OFL copyright/attribution lines, taken verbatim from each
# font's own name table (nameID 0) — Symbols and Symbols2 share one line.
OFL_ATTRIBUTIONS = [
    "Noto Sans (text base): Copyright 2022 The Noto Project Authors "
    "(https://github.com/notofonts/latin-greek-cyrillic)",
    "Noto Sans Symbols / Noto Sans Symbols 2: Copyright 2022 The Noto "
    "Project Authors (https://github.com/notofonts/symbols)",
    "Noto Sans Math: Copyright 2022 Google LLC. All Rights Reserved.",
    "Noto Emoji: Copyright 2013 Google LLC",
]
OFL_COPYRIGHT = "; ".join(OFL_ATTRIBUTIONS)


# ---------------------------------------------------------------------------
# 1. Codepoint scan
# ---------------------------------------------------------------------------

_RE_U8 = re.compile(r"\\U([0-9A-Fa-f]{8})")
_RE_U4 = re.compile(r"\\u([0-9A-Fa-f]{4})")
_RE_ENT_HEX = re.compile(r"&#x([0-9A-Fa-f]+);")
_RE_ENT_DEC = re.compile(r"&#(\d+);")


def _is_high_surrogate(v):
    return 0xD800 <= v <= 0xDBFF


def _is_low_surrogate(v):
    return 0xDC00 <= v <= 0xDFFF


def _keep(cp):
    if cp < MIN_CODEPOINT:
        return False
    if cp in EXCLUDED_CODEPOINTS:
        return False
    if 0xD800 <= cp <= 0xDFFF:
        return False
    return True


def _scan_text(text, cps):
    # (a) \Uxxxxxxxx eight-hex-digit escapes.
    for m in _RE_U8.finditer(text):
        cps.add(int(m.group(1), 16))

    # (b) adjacent \uXXXX\uXXXX high+low surrogate escape pairs, combined;
    # (c) remaining lone \uXXXX escapes that aren't surrogates.
    matches = list(_RE_U4.finditer(text))
    i = 0
    while i < len(matches):
        v = int(matches[i].group(1), 16)
        if _is_high_surrogate(v) and i + 1 < len(matches) and matches[i + 1].start() == matches[i].end():
            v2 = int(matches[i + 1].group(1), 16)
            if _is_low_surrogate(v2):
                cps.add(0x10000 + (v - 0xD800) * 0x400 + (v2 - 0xDC00))
                i += 2
                continue
        if not _is_high_surrogate(v) and not _is_low_surrogate(v):
            cps.add(v)
        i += 1

    # (d) XML numeric entities.
    for m in _RE_ENT_HEX.finditer(text):
        cps.add(int(m.group(1), 16))
    for m in _RE_ENT_DEC.finditer(text):
        cps.add(int(m.group(1)))

    # (e) literal characters in the text, combining surrogate pairs.
    j, n = 0, len(text)
    while j < n:
        o = ord(text[j])
        if _is_high_surrogate(o) and j + 1 < n:
            o2 = ord(text[j + 1])
            if _is_low_surrogate(o2):
                cps.add(0x10000 + (o - 0xD800) * 0x400 + (o2 - 0xDC00))
                j += 2
                continue
        if not _is_high_surrogate(o) and not _is_low_surrogate(o):
            cps.add(o)
        j += 1


def scan_codepoints():
    cps = set()
    for root in SCAN_ROOTS:
        base = REPO / root
        for path in base.rglob("*"):
            if path.is_dir() or path.suffix not in SCAN_EXTS:
                continue
            if SKIP_DIRS & set(path.relative_to(REPO).parts):
                continue
            _scan_text(path.read_text(encoding="utf-8-sig"), cps)
    return sorted(cp for cp in cps if _keep(cp))


# ---------------------------------------------------------------------------
# 2. Download + verify pinned sources
# ---------------------------------------------------------------------------


def fetch_source(name, spec):
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    dest = CACHE_DIR / f"{name}.ttf"
    if not dest.exists() or hashlib.sha256(dest.read_bytes()).hexdigest() != spec["sha256"]:
        print(f"  downloading {name} <- {spec['url']}")
        with urllib.request.urlopen(spec["url"]) as resp:
            data = resp.read()
        digest = hashlib.sha256(data).hexdigest()
        if digest != spec["sha256"]:
            sys.exit(f"SHA-256 mismatch for {name}: expected {spec['sha256']}, got {digest}")
        dest.write_bytes(data)
    else:
        print(f"  cached {name} (sha256 verified)")
    return dest


def load_source(name, spec):
    path = fetch_source(name, spec)
    font = TTFont(path)
    if spec["variable"]:
        font = instantiateVariableFont(font, {"wght": 400.0})
    return font


# ---------------------------------------------------------------------------
# 3. Assign, subset, scale, merge
# ---------------------------------------------------------------------------


def text_codepoints(font):
    cmap = font.getBestCmap()
    wanted = {cp for lo, hi in TEXT_RANGES for cp in range(lo, hi + 1)}
    return {cp for cp in wanted if cp in cmap}


def assign_icon_codepoints(needed, fonts):
    icon_fonts = {name: fonts[name] for name in BMP_ORDER}
    cmaps = {name: font.getBestCmap() for name, font in icon_fonts.items()}
    assigned = {name: set() for name in icon_fonts}
    unresolved = []
    for cp in needed:
        order = BMP_ORDER if cp <= 0xFFFF else ["emoji"]
        for name in order:
            if cp in cmaps[name]:
                assigned[name].add(cp)
                break
        else:
            unresolved.append(cp)
    if unresolved:
        sys.exit(
            "No source font covers: " + ", ".join(f"U+{cp:04X}" for cp in unresolved)
        )
    return assigned


def subset_font(font, codepoints, keep_layout):
    opt = subset.Options()
    opt.glyph_names = False
    opt.notdef_outline = True
    opt.hinting = False
    opt.drop_tables = list(set(opt.drop_tables) | {"gasp", "vhea", "vmtx"})
    if keep_layout:
        # Real Latin text gets shaped: keep GSUB/GPOS closure so tabular-
        # figure variants, kerning pairs, and ligature components all come
        # along, but restrict the feature set to what the app actually
        # turns on (see TEXT_LAYOUT_FEATURES) so unused stylistic-set /
        # fraction / small-caps glyphs don't ride along for free.
        opt.layout_features = TEXT_LAYOUT_FEATURES
        opt.bidi_closure = False
    else:
        # Icons are referenced individually, never shaped as bidi/RTL prose
        # or run through OpenType layout, so skip the closures that would
        # pull in unrequested glyphs (e.g. bidi-mirror counterparts) and
        # drop layout entirely.
        opt.layout_closure = False
        opt.bidi_closure = False
        opt.layout_features = []
        opt.drop_tables += ["GSUB", "GPOS", "GDEF", "STAT", "gvar", "HVAR", "VVAR", "fvar", "MATH"]
    subsetter = subset.Subsetter(options=opt)
    subsetter.populate(unicodes=sorted(codepoints))
    subsetter.subset(font)
    scale_upem(font, 1000)
    return font


def build_merged_font(text_cps, icon_assigned, fonts, tmp_dir):
    # Text base goes first: it's what supplies the merged font's GSUB/GPOS
    # and is the metrics reference (see normalize_metrics).
    paths = []
    subset_font(fonts["text"], text_cps, keep_layout=True)
    text_path = tmp_dir / "subset-text.ttf"
    fonts["text"].save(text_path)
    paths.append(text_path)

    for name in BMP_ORDER:
        codepoints = icon_assigned[name]
        if not codepoints:
            continue
        subset_font(fonts[name], codepoints, keep_layout=False)
        path = tmp_dir / f"subset-{name}.ttf"
        fonts[name].save(path)
        paths.append(path)

    return Merger().merge(paths)


# ---------------------------------------------------------------------------
# 4. Variation-selector glyphs
# ---------------------------------------------------------------------------


def add_blank_glyphs(font, codepoints):
    glyf = font["glyf"]
    hmtx = font["hmtx"]
    cmap_tables = [t for t in font["cmap"].tables if t.isUnicode()]
    order = font.getGlyphOrder()
    for cp in codepoints:
        glyph_name = f"vs{cp:04X}"
        order.append(glyph_name)
        glyf.glyphs[glyph_name] = Glyph()  # numberOfContours=0: empty outline.
        hmtx.metrics[glyph_name] = (0, 0)  # zero advance width, zero lsb.
        for table in cmap_tables:
            table.cmap[cp] = glyph_name
    font.setGlyphOrder(order)
    glyf.glyphOrder = order


# ---------------------------------------------------------------------------
# 5. Vertical metrics
# ---------------------------------------------------------------------------


def normalize_metrics(font, reference):
    hhea, ref_hhea = font["hhea"], reference["hhea"]
    hhea.ascender, hhea.descender, hhea.lineGap = (
        ref_hhea.ascender,
        ref_hhea.descender,
        ref_hhea.lineGap,
    )
    os2, ref_os2 = font["OS/2"], reference["OS/2"]
    os2.usWinAscent, os2.usWinDescent = ref_os2.usWinAscent, ref_os2.usWinDescent
    os2.sTypoAscender, os2.sTypoDescender, os2.sTypoLineGap = (
        ref_os2.sTypoAscender,
        ref_os2.sTypoDescender,
        ref_os2.sTypoLineGap,
    )


# ---------------------------------------------------------------------------
# 6. Naming
# ---------------------------------------------------------------------------


def set_names(font):
    name = font["name"]
    name.names = []
    license_text = (
        "This Font Software is licensed under the SIL Open Font License, "
        "Version 1.1. This license is available with a FAQ at: "
        "https://scripts.sil.org/OFL. " + OFL_COPYRIGHT
    )
    records = {
        0: OFL_COPYRIGHT,
        1: FAMILY_NAME,
        2: "Regular",
        3: f"{PS_NAME}:2026",
        4: FAMILY_NAME,
        5: "Version 1.000",
        6: PS_NAME,
        13: license_text,
        14: "https://scripts.sil.org/OFL",
    }
    for name_id, value in records.items():
        name.setName(value, name_id, 3, 1, 0x409)  # Windows, Unicode BMP, en-US
        name.setName(value, name_id, 1, 0, 0)  # Mac, Roman, English


# ---------------------------------------------------------------------------
# 7. Verify + write
# ---------------------------------------------------------------------------


def verify(font, needed):
    cmap = font.getBestCmap()
    required = set(needed) | set(VARIATION_SELECTORS)
    missing = sorted(cp for cp in required if cp not in cmap)
    if missing:
        sys.exit(
            "Final cmap is missing codepoints: "
            + ", ".join(f"U+{cp:04X}" for cp in missing)
        )
    return cmap


def write_manifest(cmap, path):
    entries = sorted(cp for cp in cmap if cp >= MIN_CODEPOINT)
    path.write_text("\n".join(f"{cp:04X}" for cp in entries) + "\n", encoding="utf-8")
    return entries


def write_ofl(path):
    attributions = "\n".join(f"  - {line}" for line in OFL_ATTRIBUTIONS)
    path.write_text(
        f"""Copyright 2022-2023, The Noto Project Authors.

Built from the following Noto sources (per-font copyright below):
{attributions}

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
https://scripts.sil.org/OFL

This derivative work, "EQBuddy Sans", is a Noto Sans text base with
monochrome icon glyph subsets grafted in and the whole re-metriced using
fontTools; per section 5 of the license below it does not use the reserved
font name "Noto".

-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical writer
or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining a
copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components, in
Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any Modified
Version, except to acknowledge the contribution(s) of the Copyright
Holder(s) and the Author(s) or with their explicit written permission.

5) The Font Software, modified or unmodified, in part or in whole, must
be distributed entirely under this license, and must not be distributed
under any other license. The requirement for fonts to remain under this
license does not apply to any document created using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
""",
        encoding="utf-8",
    )


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------


def main():
    print("Scanning for icon codepoints...")
    icon_cps = scan_codepoints()
    print(f"  {len(icon_cps)} icon codepoints found (>= U+2190)")

    print("Fetching pinned source fonts...")
    fonts = {name: load_source(name, spec) for name, spec in SOURCES.items()}

    text_cps = text_codepoints(fonts["text"])
    print(f"  {len(text_cps)} text codepoints found across {len(TEXT_RANGES)} ranges")

    icon_assigned = assign_icon_codepoints(icon_cps, fonts)
    for name in BMP_ORDER:
        print(f"  {name}: {len(icon_assigned[name])} icon codepoints")

    print("Subsetting, scaling, and merging...")
    with tempfile.TemporaryDirectory() as tmp:
        merged = build_merged_font(text_cps, icon_assigned, fonts, Path(tmp))

    print("Adding variation-selector glyphs (FE0E/FE0F)...")
    add_blank_glyphs(merged, VARIATION_SELECTORS)

    print("Normalizing vertical metrics to Noto Sans Regular...")
    normalize_metrics(merged, fonts["text"])

    print("Setting name table...")
    set_names(merged)

    print("Verifying coverage...")
    needed = set(icon_cps) | text_cps
    cmap = verify(merged, needed)

    FONTS_DIR.mkdir(parents=True, exist_ok=True)
    ttf_path = FONTS_DIR / "EQBuddySans.ttf"
    manifest_path = FONTS_DIR / "EQBuddySans.codepoints.txt"
    ofl_path = FONTS_DIR / "OFL.txt"

    for stale in ("EQBuddyIcons.ttf", "EQBuddyIcons.codepoints.txt"):
        stale_path = FONTS_DIR / stale
        if stale_path.exists():
            stale_path.unlink()

    merged.save(ttf_path)
    entries = write_manifest(cmap, manifest_path)
    write_ofl(ofl_path)

    size = ttf_path.stat().st_size
    print()
    print("Done.")
    print(f"  glyphs:     {merged['maxp'].numGlyphs}")
    print(f"  codepoints: {len(entries)}")
    print(f"  file size:  {size:,} bytes ({size / 1024:.1f} KiB)")
    print(f"  wrote:      {ttf_path}")
    print(f"  wrote:      {manifest_path}")
    print(f"  wrote:      {ofl_path}")


if __name__ == "__main__":
    main()
