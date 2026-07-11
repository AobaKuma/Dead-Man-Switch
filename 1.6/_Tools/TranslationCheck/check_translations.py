#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
RimWorld translation-key missing checker for the Fortified Feature Framework.

Scans EVERY Languages/ folder under the mod root (the main module plus any
conditionally-loaded sub-modules such as CE/, Royalty/, Ideology/, Mod/*), treats
English as the base language of each module, and for every other language reports:
  * MISSING  keys  - present in English but absent from the translation
  * OBSOLETE keys  - present in the translation but no longer in English
  * DUPLICATE keys - the same key defined more than once inside one language

It also audits the BASE (English) language itself for:
  * exact duplicate keys
  * case-variant collisions (e.g. LV1_Initial vs LV1_initial) - a common source
    of phantom "missing" entries.

Keys whose immediately-preceding XML comment contains the word "UNUSED" are
treated as intentionally-dead and excluded from the base comparison, so a modder
can park stale keys without polluting the report.

Output: a plain-text report (MissingTranslations.txt) next to this script.

The script is written defensively: it never aborts on a single bad file. Any
file it cannot parse is recorded in a PARSE ERRORS section instead of crashing
the whole run.
"""

import os
import sys
import io
import datetime
import collections
import xml.etree.ElementTree as ET

BASE_LANGUAGE = "English"          # reference language (folder name)
REPORT_NAME = "MissingTranslations.txt"
# Categories of translatable content; each is a top-level folder inside a language.
CATEGORIES = ("Keyed", "DefInjected")
UNUSED_MARKER = "unused"           # comment keyword that marks keys as dead


# --------------------------------------------------------------------------- #
# Filesystem helpers
# --------------------------------------------------------------------------- #
def find_mod_root(start):
    """
    Walk upward from `start` to the real mod root.

    Preference order:
      1. the nearest ancestor that contains About/About.xml  (true mod root)
      2. the nearest ancestor that contains a Languages/ folder (fallback)
    Returning the About-based root lets us discover sub-module Languages folders
    (CE/, Royalty/, Mod/*, ...) instead of stopping at the first one.
    """
    cur = os.path.abspath(start)
    languages_fallback = None
    while True:
        if os.path.isfile(os.path.join(cur, "About", "About.xml")):
            return cur
        if languages_fallback is None and os.path.isdir(os.path.join(cur, "Languages")):
            languages_fallback = cur
        parent = os.path.dirname(cur)
        if parent == cur:          # reached filesystem root
            return languages_fallback
        cur = parent


def read_mod_name(mod_root):
    """Best-effort read of the mod name from About/About.xml (never raises)."""
    about = os.path.join(mod_root, "About", "About.xml")
    if not os.path.isfile(about):
        return os.path.basename(mod_root)
    try:
        tree = ET.parse(about)
        node = tree.getroot().find("name")
        if node is not None and node.text:
            return node.text.strip()
    except Exception:
        pass
    return os.path.basename(mod_root)


def find_all_language_dirs(mod_root):
    """
    Return every 'Languages' directory under mod_root, sorted by relative path.

    Skips version-control / tooling noise directories.
    """
    out = []
    skip = {".git", ".idea", ".vs", "obj", "bin", "_Tools", "_Source", "_Sources"}
    for dirpath, dirs, _files in os.walk(mod_root):
        dirs[:] = [d for d in dirs if d not in skip]
        if os.path.basename(dirpath) == "Languages":
            out.append(dirpath)
    out.sort()
    return out


def list_language_dirs(languages_dir):
    """Return sorted list of language folder names actually present on disk."""
    out = []
    for name in sorted(os.listdir(languages_dir)):
        full = os.path.join(languages_dir, name)
        if os.path.isdir(full):
            out.append(name)
    return out


# --------------------------------------------------------------------------- #
# XML parsing
# --------------------------------------------------------------------------- #
def parse_keys_from_file(path):
    """
    Return (keys_dict, duplicates_list, error_or_None) for one LanguageData xml.

    keys_dict:  {key_name: text_value}  (last write wins, matching RimWorld)
    duplicates_list: [key_name, ...] keys that appeared more than once
    error_or_None: a string describing a parse failure, or None on success.

    Keys preceded by a comment containing "UNUSED" are skipped: the modder has
    explicitly parked them, so they must not count as base keys.
    """
    keys = {}
    duplicates = []
    seen = set()
    try:
        # insert_comments=True keeps <!-- ... --> nodes so we can honor UNUSED.
        parser = ET.XMLParser(target=ET.TreeBuilder(insert_comments=True))
        tree = ET.parse(path, parser=parser)
        root = tree.getroot()
    except ET.ParseError as e:
        return keys, duplicates, "XML parse error: {}".format(e)
    except Exception as e:                       # unreadable file, permissions, etc.
        return keys, duplicates, "read error: {}".format(e)

    note = None
    if root is None or root.tag != "LanguageData":
        # Not fatal, but worth surfacing - RimWorld expects <LanguageData> root.
        note = "root element is <{}>, expected <LanguageData>".format(
            root.tag if root is not None else "none")
        # Still try to read direct children so we don't lose data.

    unused = False
    for child in list(root) if root is not None else []:
        tag = child.tag
        # Comment / processing-instruction nodes: tag is a callable, not a str.
        if not isinstance(tag, str):
            if tag is ET.Comment:
                text = child.text or ""
                # An UNUSED comment marks every following key until the next comment.
                unused = UNUSED_MARKER in text.lower()
            continue
        if unused:
            continue                              # intentionally-dead key: skip
        key = tag
        value = child.text if child.text is not None else ""
        if key in seen:
            duplicates.append(key)
        seen.add(key)
        keys[key] = value
    return keys, duplicates, note


def collect_language(lang_dir):
    """
    Collect every translation key for one language.

    Returns:
      data:  {category: {key: {"value": str, "file": relpath}}}
      dups:  {category: {key: [relpath, ...]}}
      errors: [(relpath, message), ...]
    """
    data = {c: {} for c in CATEGORIES}
    dups = {c: {} for c in CATEGORIES}
    errors = []

    for category in CATEGORIES:
        cat_root = os.path.join(lang_dir, category)
        if not os.path.isdir(cat_root):
            continue
        for dirpath, _dirs, files in os.walk(cat_root):
            for fname in files:
                if not fname.lower().endswith(".xml"):
                    continue
                fpath = os.path.join(dirpath, fname)
                rel = os.path.relpath(fpath, lang_dir).replace("\\", "/")
                keys, duplicates, err = parse_keys_from_file(fpath)
                if err:
                    errors.append((rel, err))
                for k, v in keys.items():
                    # First file that defines a key "owns" it for reporting.
                    if k not in data[category]:
                        data[category][k] = {"value": v, "file": rel}
                for dk in duplicates:
                    dups[category].setdefault(dk, []).append(rel)
    return data, dups, errors


def find_case_variants(keys):
    """Group keys that are equal when lower-cased but differ in actual case."""
    groups = collections.defaultdict(set)
    for k in keys:
        groups[k.lower()].add(k)
    return {low: sorted(v) for low, v in groups.items() if len(v) > 1}


# --------------------------------------------------------------------------- #
# Reporting
# --------------------------------------------------------------------------- #
def truncate(text, limit=80):
    text = " ".join((text or "").split())      # collapse whitespace/newlines
    if len(text) > limit:
        return text[:limit - 1] + "…"
    return text


def build_module_section(w, module_label, base_dir, languages_dir):
    """
    Render one module (one Languages folder) and return (missing, obsolete).

    languages_dir: path to the module's Languages folder.
    base_dir:      path to the module's English folder, or None if absent.
    """
    module_missing = 0
    module_obsolete = 0

    w("#" * 70 + "\n")
    w("MODULE: {}\n".format(module_label))
    w("#" * 70 + "\n\n")

    if base_dir is None:
        w("  (no '{}' base language in this module - skipped)\n\n"
          .format(BASE_LANGUAGE))
        return module_missing, module_obsolete

    base_data, base_dups, base_errors = collect_language(base_dir)
    base_counts = ", ".join(
        "{} {}".format(len(base_data[c]), c) for c in CATEGORIES)
    w("Base keys: {}\n".format(base_counts))
    if base_errors:
        w("WARNING: base language has {} unreadable file(s).\n"
          .format(len(base_errors)))

    # ---- Base-language self-audit: duplicates + case-variant collisions ----
    base_issue_lines = []
    for category in CATEGORIES:
        cvars = find_case_variants(base_data[category].keys())
        for low in sorted(cvars):
            base_issue_lines.append(
                "    [{}] case-variant keys: {}".format(
                    category, ", ".join(cvars[low])))
        for k in sorted(base_dups.get(category, {})):
            base_issue_lines.append(
                "    [{}] duplicate key: {}   (in {})".format(
                    category, k, ", ".join(base_dups[category][k])))
    if base_issue_lines:
        w("\n  [BASE ISSUES] problems inside {} itself "
          "(fix these first - they cause phantom 'missing'):\n".format(BASE_LANGUAGE))
        for line in base_issue_lines:
            w(line + "\n")
    w("\n")

    # ---- Per-language comparison ----
    lang_names = list_language_dirs(languages_dir)
    for name in lang_names:
        if name == BASE_LANGUAGE:
            continue
        data, dups, errors = collect_language(os.path.join(languages_dir, name))

        w("--- LANGUAGE: {} ---\n\n".format(name))
        lang_missing = 0
        lang_obsolete = 0

        for category in CATEGORIES:
            base_keys = base_data[category]
            trans_keys = data[category]

            missing = sorted(k for k in base_keys if k not in trans_keys)

            # If the base has no keys at all in this category we CANNOT judge
            # obsolescence - flagging everything would be a false positive
            # (e.g. a mod that ships Keyed translations but no English/Keyed).
            base_empty = len(base_keys) == 0
            if base_empty:
                obsolete = []
            else:
                obsolete = sorted(k for k in trans_keys if k not in base_keys)

            lang_missing += len(missing)
            lang_obsolete += len(obsolete)

            w("  {}:  Missing: {}   Obsolete: {}\n".format(
                category, len(missing), len(obsolete)))

            if base_empty and trans_keys:
                w("    NOTE: no '{}' base keys for this category - "
                  "cannot verify; {} translated key(s) left untouched.\n"
                  .format(BASE_LANGUAGE, len(trans_keys)))

            if missing:
                w("    [MISSING] present in {} but not translated:\n"
                  .format(BASE_LANGUAGE))
                for k in missing:
                    info = base_keys[k]
                    w("      {}\n".format(k))
                    w("          EN: \"{}\"\n".format(truncate(info["value"])))
                    w("          src: {}\n".format(info["file"]))

            if obsolete:
                w("    [OBSOLETE] in translation but no longer in {}:\n"
                  .format(BASE_LANGUAGE))
                for k in obsolete:
                    info = trans_keys[k]
                    w("      {}   (in {})\n".format(k, info["file"]))

            cat_dups = dups.get(category, {})
            if cat_dups:
                w("    [DUPLICATE] defined more than once in this language:\n")
                for k in sorted(cat_dups):
                    w("      {}   (in {})\n".format(k, ", ".join(cat_dups[k])))
            w("\n")

        if errors:
            w("    [PARSE ERRORS] files that could not be read:\n")
            for rel, msg in errors:
                w("      {}  ->  {}\n".format(rel, msg))
            w("\n")

        w("  SUBTOTAL {}: {} missing, {} obsolete\n\n".format(
            name, lang_missing, lang_obsolete))
        module_missing += lang_missing
        module_obsolete += lang_obsolete

    if base_errors:
        w("  BASE LANGUAGE PARSE ERRORS:\n")
        for rel, msg in base_errors:
            w("    {}  ->  {}\n".format(rel, msg))
        w("\n")

    return module_missing, module_obsolete


def build_report(mod_name, mod_root, language_dirs):
    out = io.StringIO()
    w = out.write

    w("=" * 70 + "\n")
    w("RimWorld Translation Key Check Report\n")
    w("=" * 70 + "\n")
    w("Mod:            {}\n".format(mod_name))
    w("Base language:  {}\n".format(BASE_LANGUAGE))
    w("Generated:      {}\n".format(
        datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")))
    w("Modules found:  {}\n".format(len(language_dirs)))
    w("\n")

    total_missing = 0
    total_obsolete = 0

    for languages_dir in language_dirs:
        module_label = os.path.relpath(languages_dir, mod_root).replace("\\", "/")
        base_dir = os.path.join(languages_dir, BASE_LANGUAGE)
        if not os.path.isdir(base_dir):
            base_dir = None
        m, o = build_module_section(w, module_label, base_dir, languages_dir)
        total_missing += m
        total_obsolete += o

    w("=" * 70 + "\n")
    w("TOTAL across all modules & languages: {} missing, {} obsolete\n".format(
        total_missing, total_obsolete))
    w("=" * 70 + "\n")
    return out.getvalue(), total_missing, total_obsolete


# --------------------------------------------------------------------------- #
# Main
# --------------------------------------------------------------------------- #
def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    mod_root = find_mod_root(script_dir)
    if mod_root is None:
        print("ERROR: could not locate a mod root (About/About.xml or Languages/) "
              "above:\n  {}".format(script_dir))
        return 2

    language_dirs = find_all_language_dirs(mod_root)
    if not language_dirs:
        print("ERROR: no 'Languages' folders found under {}".format(mod_root))
        return 2

    mod_name = read_mod_name(mod_root)
    print("Mod root : {}".format(mod_root))
    print("Base lang: {}".format(BASE_LANGUAGE))
    print("Modules  : {}".format(len(language_dirs)))
    for d in language_dirs:
        print("   - {}".format(os.path.relpath(d, mod_root).replace("\\", "/")))

    report, total_missing, total_obsolete = build_report(
        mod_name, mod_root, language_dirs)

    report_path = os.path.join(script_dir, REPORT_NAME)
    try:
        with io.open(report_path, "w", encoding="utf-8") as f:
            f.write(report)
    except Exception as e:
        print("ERROR: could not write report: {}".format(e))
        print(report)                            # fall back to stdout
        return 1

    print("-" * 50)
    print("Report written: {}".format(report_path))
    print("Totals: {} missing, {} obsolete".format(total_missing, total_obsolete))
    return 0


if __name__ == "__main__":
    # Ensure UTF-8 stdout even on legacy Windows consoles.
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    sys.exit(main())
