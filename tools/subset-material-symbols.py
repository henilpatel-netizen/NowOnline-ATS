#!/usr/bin/env python3
"""Subset the self-hosted Material Symbols Outlined font to only the icons the ATS uses.

Reproducible: scans the source for icon ligature names (both `<span class="ms">name</span>`
literals and C# icon-name string literals), intersects them with the ligatures the full font
actually defines, subsets to that set (keeping the variable axes and the `liga` feature), and
verifies every used icon survives. Re-run after adding a new icon.

Usage:  py tools/subset-material-symbols.py
"""
import re, sys, pathlib, tempfile
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

# The .ms CSS class renders at these fixed axis values, so we flatten the variable font to a static
# instance here (drops fvar/gvar/HVAR/avar/STAT) before glyph-subsetting. Keep in sync with
# `font-variation-settings` in wwwroot/css/ats-base.css.
AXES = {"opsz": 24, "wght": 400, "FILL": 0, "GRAD": 0}

ROOT = pathlib.Path(__file__).resolve().parent.parent
WEB = ROOT / "src" / "Ats.Web"
FULL = WEB / "wwwroot" / "lib" / "material-symbols" / "material-symbols-outlined.woff2"
OUT = WEB / "wwwroot" / "lib" / "material-symbols" / "material-symbols-subset.woff2"
LIST = ROOT / "tools" / "material-symbols.icons.txt"

# Candidate tokens: any lowercase_underscore word that appears as .ms span text or a quoted string
# in a view or an icon-producing C# file. Over-collection is safe (non-ligature words add nothing).
# Icon text inside any element carrying the .ms class (allow other attributes between class and >).
MS_SPAN = re.compile(r'class="ms[ a-z-]*"[^>]*>\s*([a-z][a-z_]+?)\s*<')
QUOTED = re.compile(r'"([a-z][a-z_]{2,})"')

def collect():
    tokens = set()
    for path in list(WEB.rglob("*.cshtml")) + list((WEB / "ViewComponents").rglob("*.cs")) \
              + list((ROOT / "src" / "Ats.Infrastructure" / "Dashboard").rglob("*.cs")):
        text = path.read_text(encoding="utf-8", errors="ignore")
        tokens.update(MS_SPAN.findall(text))
        tokens.update(QUOTED.findall(text))
    return tokens

def font_ligatures(font):
    """All ligature input strings the font defines (letter-sequence -> icon glyph)."""
    ligs = set()
    gsub = font.get("GSUB")
    if not gsub:
        return ligs
    cmap = font.getBestCmap()
    rev = {g: chr(c) for c, g in cmap.items()}
    for lookup in gsub.table.LookupList.Lookup:
        for st in lookup.SubTable:
            # Material Symbols wraps ligature subtables in Extension Substitution (LookupType 7).
            st = getattr(st, "ExtSubTable", st)
            ligsets = getattr(st, "ligatures", None)
            if not ligsets:
                continue
            for first_glyph, entries in ligsets.items():
                fc = rev.get(first_glyph)
                if fc is None:
                    continue
                for lig in entries:
                    comp = [rev.get(g) for g in lig.Component]
                    if all(comp):
                        ligs.add(fc + "".join(comp))
    return ligs

def main():
    if not FULL.exists():
        sys.exit(f"Full font not found: {FULL} (run `libman restore` first)")
    candidates = collect()
    defined = font_ligatures(TTFont(str(FULL)))
    used = sorted(candidates & defined)
    missing = sorted(t for t in candidates if "_" in t and t not in defined)  # underscore = likely icon
    if not used:
        sys.exit("No icons resolved; aborting.")

    LIST.write_text("\n".join(used) + "\n", encoding="utf-8")

    # 1) Flatten the variable font to the fixed axes the CSS uses (drops the big variation tables).
    inst = instancer.instantiateVariableFont(TTFont(str(FULL)), AXES, inplace=False)
    with tempfile.NamedTemporaryFile(suffix=".ttf", delete=False) as tmp:
        static_path = tmp.name
    inst.save(static_path)

    # 2) Subset the static font to the used icon ligatures (keep liga so name -> glyph resolves).
    text = " ".join(used)
    args = [static_path, f"--text={text}", "--layout-features+=liga,dlig,calt,ccmp,rlig",
            "--flavor=woff2", f"--output-file={OUT}"]
    subset.main(args)
    pathlib.Path(static_path).unlink(missing_ok=True)

    # Verify every used icon still resolves in the subset.
    sub_ligs = font_ligatures(TTFont(str(OUT)))
    lost = [t for t in used if t not in sub_ligs]
    before = FULL.stat().st_size
    after = OUT.stat().st_size
    print(f"icons used: {len(used)}")
    print(f"candidates not real ligatures (ignored): {len(candidates) - len(used)}")
    if missing:
        print(f"NOTE underscore-tokens not in font (check for typos): {missing}")
    print(f"size: {before:,} -> {after:,} bytes ({after*100//before}%)")
    if lost:
        sys.exit(f"REGRESSION: these used icons are missing from the subset: {lost}")
    print("verified: all used icons present in subset.")

if __name__ == "__main__":
    main()
