# Research (local only)

Scratch space for raw gaze corpora, papers, and analysis exports.

**Everything here except this README is git-ignored.** The README is committed so
the convention survives a fresh clone — recreate the subfolders as needed.

## Why here and not under `Assets/`

Unity imports every file under `Assets/`, whether or not git ignores it: it
generates a `.meta`, hashes the file into the Library, and re-imports on change.
For a multi-GB `.npz` corpus that is pure cost, since none of this is a runtime
asset. Anything outside `Assets/` is invisible to the Editor.

It still lives inside the repo root so that relative paths and repo-wide search
work without absolute paths.

## Suggested layout

```
Research/
  corpora/     raw gaze data (.npz, .csv) and derived exports
  papers/      reference PDFs
  notes/       working notes, figure transcriptions
```

## This is not a backup

Git-ignored means unversioned and unbacked-up. Keep the canonical copy of any
irreplaceable corpus elsewhere and mirror it here.

On Windows, a directory junction avoids duplicating gigabytes and keeps one
source of truth (no admin rights needed, unlike a symlink):

```
mklink /J "E:\Unity_Projects\GazeControl\Research\corpora" "F:\path\to\canonical\data"
```
