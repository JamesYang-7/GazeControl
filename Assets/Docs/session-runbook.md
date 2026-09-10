# Running a participant session

The operator's procedure, start to finish. Everything the software can check for
itself is checked by **GazeControl → Study 1 → Start Session** (or **Study 2 →**); what is left here is
what only a person in the room can do.

## Study 2 (3People clips)

The procedure below is the same; only the scene and the clip list differ.

- Open **`Assets/Scenes/Study2Scene.unity`**, not TriadScene. Its agents stand where
  the recording puts them, moved onto the participant's starting viewpoint per clip.
- On the `GazeCondition` object, `StudySessionRunner.Conversations` lists the seven
  exported clips `study2_c1`…`c7` and `Seeds` one seed per clip. **Choose the clips
  and the seeds before starting**: delete the conversations you are not using
  (keep `Seeds` the same length and order), and set each seed to one whose three
  tracks exist. Tracks are baked for seeds 1-12 on every clip
  (`GazeControl → Study 2 → Bake Seeds 1-12`); `uv run python
  Tools/summarize_gaze_tracks.py` tabulates what exists and what each agent did.
  Save the scene.
- The questionnaire's group count follows the clip list; its items are study 1's.
- To check the bakes by eye, **GazeControl → Study 2 → Preview Session**: the clip order
  with the tracks replayed on the desktop, no questionnaire, no headset, Space skips to the next clip.

## Before the participant arrives

1. Open **GazeControl → Study 1 → Start Session** or **Study 2 → Start Session**; it opens the right scene.
2. Read the **Preflight** list. Rows marked `!!` block the session; fix them now.
   Rows marked `→` are set for you when the session starts and need nothing.
   - *Segments exported* / *Tracks baked* are the two that take real time to fix —
     baking is one play session per condition, from the Clip Browser. Check them
     the day before, not with someone waiting.
3. Check the participant label at the top. It is taken from the folders under
   the study's folder under `Recordings/`, so it is one past the last person who ran in this study. The step buttons are
   for a correction only.
4. Start Varjo Base and confirm the headset is connected. Eye tracking needs
   permission granted to this application in Varjo Base — once per machine, not
   once per participant.

## With the participant

5. Consent and instructions, then fit the headset. If they will answer with a
   controller, hand them one now and say what it does: point it at the buttons
   under the question and pull the trigger.
6. Press **Start session for Pnn**. The editor enters play; the scene applies
   study mode, replayed tracks, logging, the headset and the developer overlay
   itself. The window switches to a live view: XR status, eye-tracking status and
   the clip position.
7. Run the headset's eye-tracking calibration for this wearer
   (`XrParticipantRig.CalibrateEyeTracking()`, or from Varjo Base). The window's
   **Headset** section says when the tracker is available and calibrated —
   wait for it to say so.
8. With the participant standing on the mark and looking straight ahead, confirm
   the view is centred between the two agents at eye level. Recentring places
   their head on the vertex at the agents' eye height, facing between them, and
   the head tracks freely from there. It happens on the first tracked frame;
   repeat it from `XrParticipantRig`'s context menu if they were not on the mark
   facing forward then.
9. Press **Space** with the Game view focused to begin (the key does not reach the
   scene while the Start Session window is focused). From here Space is the only key: it advances past
   the framing screen, and after each clip it commits the questionnaire screen
   and releases the next clip.

   **The participant answers either way.** They may press the buttons under the
   question with the controller, or say the answer for you to type into the
   operator panel on the desktop window — the two write the same record, and you
   can take over mid-screen. The panel's header says whether a controller is
   being tracked and which button they are pointing at; if it says none is, the
   session simply runs on spoken answers.

   With a controller they can pass **every screen but the last**, framing and
   both previews included — read those aloud and ask them to wait for you rather
   than relying on the screen to hold them. The free-text probe has no keyboard
   in the headset, so it is always spoken: their **Next** on that screen records
   whatever you have typed, empty included. The closing passage is yours alone;
   press Enter when the headset is off.
10. Stay in the room. The head tracks, so a participant who walks sees the agents
    move; ask them to stay on the mark, and recentre if they have drifted between
    clips.

## Afterwards

Everything lands in the study's own folder under `Recordings/` (`Recordings/Study_01` for TriadScene, `Recordings/Study_02` for Study2Scene — the gaze runner's `OutputDirectory`):

- `<study>/Pnn/session.json` — the running order, the counterbalancing slot,
  start and end times, how many clips were shown, and the commit it ran at.
  **No `completedUtc` means the session did not finish.**
- `<study>/Pnn/responses.csv` and `comments.jsonl` — the questionnaire.
- `<study>/<conversation>/Pnn_<condition>_<timestamp>.csv` and `_user.csv` —
  the per-frame agent log and the participant's own gaze, per take.

Close the play session. Nothing needs putting back: the study settings were
applied to the play session only, and the scene asset was never modified.

## Things that go wrong

- **The agents move and nothing is heard, or a clip never starts.** The editor's audio
  engine is still bound to a device that has gone — usually the headset after it was
  unplugged or the headset's virtual device after XR started — even though Windows shows Unity playing on the monitor. The rig now resets the engine itself when XR starts and stops; if it still happens, run
  **GazeControl → Reset Audio** (or restart the editor), then start the clip again. A
  headset plugged in or out mid-day is the trigger; the bake does this reset itself.

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
