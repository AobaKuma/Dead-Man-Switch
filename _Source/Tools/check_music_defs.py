"""Check the situational-music defs (Plans/DMS_Music_Transitions_實作計畫.md 10.1).

Usage: python -I _Source/Tools/check_music_defs.py <mod root>
Exit code 0 when every check passes; each problem is printed on its own line.

Checks:
- unique defNames per def type;
- every DMS SongDef has playOnMap=false and a clipPath whose file exists under Sounds/ with the exact casing;
- every sequence uses DMS.MusicSequenceWorker_DMS, names a DMS state, sets fadeoutLastSongDuration and
  overrideFadeout, references only existing SongDefs and has at least one song in its base pool;
- every transition uses DMS.MusicTransition_DMS, references an existing sequence and sets no dangerRequirement;
- each of the eight DMS states has exactly one sequence and one transition.
"""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

STATES = [
    "InstallationExplore", "FacilityExplore", "Aftermath", "SecurityAlert",
    "FacilityTrapped", "InstallationCombat", "BossCombat", "LockdownCountdown",
]
AUDIO_EXTENSIONS = [".wav", ".mp3", ".ogg"]
MUSIC_DEFS = "1.7/NewContent/Defs/Misc/Music"


def load(root):
    defs = {"SongDef": {}, "MusicSequenceDef": {}, "MusicTransitionDef": {}}
    problems = []
    for path in sorted((root / MUSIC_DEFS).glob("*.xml")):
        for node in ET.parse(path).getroot():
            if node.tag not in defs:
                continue
            name = node.findtext("defName")
            if name in defs[node.tag]:
                problems.append(f"{node.tag} {name}: defined twice")
            defs[node.tag][name] = node
    return defs, problems


def clip_exists(root, clip):
    # Match each path segment with exact casing, as ContentFinder keys are case-sensitive on some platforms.
    folder = root / "Sounds"
    parts = clip.split("/")
    for part in parts[:-1]:
        names = {p.name for p in folder.iterdir()} if folder.is_dir() else set()
        if part not in names:
            return False
        folder = folder / part
    names = {p.name for p in folder.iterdir()} if folder.is_dir() else set()
    return any(parts[-1] + ext in names for ext in AUDIO_EXTENSIONS)


def items(node, tag):
    holder = node.find(tag)
    return [li.text.strip() for li in holder.findall("li")] if holder is not None else []


def main():
    root = Path(sys.argv[1])
    defs, problems = load(root)
    songs, sequences, transitions = defs["SongDef"], defs["MusicSequenceDef"], defs["MusicTransitionDef"]

    for name, node in songs.items():
        if (node.findtext("playOnMap") or "").strip().lower() != "false":
            problems.append(f"SongDef {name}: playOnMap must be false")
        clip = (node.findtext("clipPath") or "").strip()
        if not clip or not clip_exists(root, clip):
            problems.append(f"SongDef {name}: no audio file for clipPath '{clip}' (casing must match)")

    seq_states = {}
    for name, node in sequences.items():
        if node.findtext("workerType") != "DMS.MusicSequenceWorker_DMS":
            problems.append(f"MusicSequenceDef {name}: workerType must be DMS.MusicSequenceWorker_DMS")
        for field in ("fadeoutLastSongDuration", "overrideFadeout"):
            if node.find(field) is None:
                problems.append(f"MusicSequenceDef {name}: {field} must be set (vanilla defaults to 10 s)")
        ext = next((li for li in node.iter("li") if li.get("Class") == "DMS.ModExtension_DMSMusicSequence"), None)
        state = ext.findtext("state") if ext is not None else None
        if state not in STATES:
            problems.append(f"MusicSequenceDef {name}: ModExtension_DMSMusicSequence.state '{state}' is not a DMS state")
        else:
            seq_states.setdefault(state, []).append(name)
        base_pool = items(node, "songs") + ([node.findtext("song")] if node.findtext("song") else [])
        if not base_pool:
            problems.append(f"MusicSequenceDef {name}: the base pool (songs) is empty")
        referenced = list(base_pool)
        if ext is not None:
            referenced += items(ext, "facilitySongs")
            variants = ext.find("variants")
            if variants is not None:
                referenced += [v.findtext("song") for v in variants.findall("li")]
        for song in referenced:
            if song not in songs:
                problems.append(f"MusicSequenceDef {name}: unknown SongDef '{song}'")

    trans_states = {}
    for name, node in transitions.items():
        if node.findtext("workerType") != "DMS.MusicTransition_DMS":
            problems.append(f"MusicTransitionDef {name}: workerType must be DMS.MusicTransition_DMS")
        if node.find("dangerRequirement") is not None:
            problems.append(f"MusicTransitionDef {name}: must not set dangerRequirement")
        sequence = node.findtext("sequence")
        if sequence not in sequences:
            problems.append(f"MusicTransitionDef {name}: unknown sequence '{sequence}'")
            continue
        ext = next((li for li in sequences[sequence].iter("li") if li.get("Class") == "DMS.ModExtension_DMSMusicSequence"), None)
        state = ext.findtext("state") if ext is not None else None
        trans_states.setdefault(state, []).append(name)

    for state in STATES:
        if len(seq_states.get(state, [])) != 1:
            problems.append(f"state {state}: expected one sequence, found {seq_states.get(state, [])}")
        if len(trans_states.get(state, [])) != 1:
            problems.append(f"state {state}: expected one transition, found {trans_states.get(state, [])}")

    for problem in problems:
        print(problem)
    print(f"{len(songs)} songs, {len(sequences)} sequences, {len(transitions)} transitions; {len(problems)} problem(s).")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
