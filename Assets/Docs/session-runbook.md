# Running a participant session

The operator's procedure, start to finish. Everything the software can check for
itself is checked by **GazeControl → Study → Start Session**; what is left here is
what only a person in the room can do.

## Study 2 (3People clips)

The procedure below is the same; only the scene and the clip list differ.

- Open **`Assets/Scenes/Study2Scene.unity`**, not TriadScene. Its agents stand where
  the recording puts them, moved onto the participant's fixed viewpoint per clip.
- On the `GazeCondition` object, `StudySessionRunner.Conversations` lists the seven
  exported clips `study2_c1`…`c7` and `Seeds` one seed per clip. **Choose the clips
  and the seeds before starting**: delete the conversations you are not using
  (keep `Seeds` the same length and order), and set each seed to one whose three
  tracks exist. Tracks are baked for seeds 1-12 on every clip
  (`GazeControl → Study 2 → Bake Seeds 1-12`); `uv run python
  Tools/summarize_gaze_tracks.py` tabulates what exists and what each agent did.
  Save the scene.
- The questionnaire's group count follows the clip list; its items are study 1's.

## Before the participant arrives

1. Open the study scene and open **GazeControl → Study → Start Session**.
2. Read the **Preflight** list. Rows marked `!!` block the session; fix them now.
   Rows marked `→` are set for you when the session starts and need nothing.
   - *Segments exported* / *Tracks baked* are the two that take real time to fix —
     baking is one play session per condition, from the Clip Browser. Check them
     the day before, not with someone waiting.
3. Check the participant label at the top. It is taken from the folders under
   `Recordings/`, so it is one past the last person who ran. The step buttons are
   for a correction only.
4. Start Varjo Base and confirm the headset is connected. Eye tracking needs
   permission granted to this application in Varjo Base — once per machine, not
   once per participant.

## With the participant

5. Consent and instructions, then fit the headset.
6. Press **Start session for Pnn**. The editor enters play; the scene applies
   study mode, replayed tracks, logging, the headset and the developer overlay
   itself. The window switches to a live view: XR status, eye-tracking status and
   the clip position.
7. Run the headset's eye-tracking calibration for this wearer
   (`XrParticipantRig.CalibrateEyeTracking()`, or from Varjo Base). The window's
   **Headset** section says when the tracker is available and calibrated —
   wait for it to say so.
8. With the participant looking straight ahead, confirm the view is centred
   between the two agents. It recentres itself on the first tracked frame; repeat
   it from `XrParticipantRig`'s context menu if they were not facing forward then.
9. Press **Space** to begin. From here Space is the only key: it advances past the
   framing screen, and after each clip it commits the questionnaire screen and
   releases the next clip. The participant answers aloud and you type what they
   say into the operator panel on the desktop window.
10. Stay in the room. The viewpoint is fixed, so a participant who walks gets no
    feedback that they have.

## Afterwards

Everything lands in `Recordings/`:

- `Recordings/Pnn/session.json` — the running order, the counterbalancing slot,
  start and end times, how many clips were shown, and the commit it ran at.
  **No `completedUtc` means the session did not finish.**
- `Recordings/Pnn/responses.csv` and `comments.jsonl` — the questionnaire.
- `Recordings/<conversation>/Pnn_<condition>_<timestamp>.csv` and `_user.csv` —
  the per-frame agent log and the participant's own gaze, per take.

Close the play session. Nothing needs putting back: the study settings were
applied to the play session only, and the scene asset was never modified.

## Things that go wrong

**A session was interrupted and the participant has to be re-run.** Move their
folder aside first (`Recordings/P07` → `Recordings/P07_aborted`). A session
refuses to reopen a participant's folder, because appending would mix two runs in
files nothing downstream can separate. The Start Session window flags an
unfinished previous session so this is visible before the next one starts.

**The window says a label has already run and you disagree.** Look in
`Recordings/` — a folder with that name exists. Its `session.json` says how far
that session got.

**Nothing appears in the headset after a clip.** The questionnaire canvas is on
the participant's own vertex; the operator panel is IMGUI and is drawn only into
the desktop window. If neither appears, there is no `QuestionnaireSession` in the
scene — but preflight refuses to launch in that case, so this should not be
reachable from the window.

**A clip was cut short.** Its questionnaire screen comes up with recording
disabled. A rating of half a clip is worse than a missing one, because nothing
downstream can tell them apart.

**P00 files appear.** That is the debugging namespace: a development run, not a
participant. Its folder is timestamped, and the roster ignores it.
