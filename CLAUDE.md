# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A gaze control demo for virtual agents in a triad conversation: two virtual agents (A and B) plus the user. Gaze behavior patterns collected from real triad conversations (gaze shifts preceding turn-taking) drive the agents' gaze control. Motion control of the agents is also data-driven.

Target demo scenes:
1. **Agent-to-agent turn-taking** — turn-taking happens between agent A and agent B while the user just watches.
2. **Turn-yielding to the user** — agent A wants the user to take the turn and signals this with gaze.

## Environment

- **Unity 6000.5.3f1** (Unity 6) with the **Universal Render Pipeline (URP)**.
- Input handled via the **Input System** package (`Assets/InputSystem_Actions.inputactions`).
- **Timeline** and **Unity Test Framework** packages are installed.
- The project is currently a fresh URP template: `Assets/Scenes/SampleScene.unity`, URP settings under `Assets/Settings/`, and removable template files under `Assets/TutorialInfo/`.

## SMPL-X

The agents use the SMPL-X body model. Two code layers drive it:
- `Assets/GazeControl/Scripts/Runtime/Motion/` (ours) — .npz clip loading and playback (`SmplxMotionPlayer` writes joint local rotations in `Update`).
- `Assets/SMPLX/Scripts/SMPLX.cs` (official MPI package, imported verbatim — do not restyle) — pose correctives (486 blendshapes recomputed in `LateUpdate` from current joint rotations, composing cleanly with our player), betas/expressions, hand/body pose presets. Depends on `Assets/SMPLX/ThirdParty/` and the regressor JSONs in `Assets/SMPLX/Resources/`.

License: SMPL-X model license (research use; cite the SMPL-X paper in publications). The Meshcapade sample textures are CC BY-NC.

## Speech and the recorded conversation

**The agents speak the corpus's own audio, not synthesised speech.** `RecordedConversation` replays a 20-30 s segment of a real TalkingWithHands dyadic conversation: each agent gets that recording's wav on its `AudioSource` and its 60 fps grounded SMPL-X motion, and the corpus's annotated end-of-turn events become the turn schedule the gaze policies read. Corpus speaker 1 (`main-agent`) is agent A, speaker 2 (`interloctr`) is agent B; the human user is the silent listener.

Everything runs on one clock, `RecordedConversation.Elapsed` — the frame clock, because Unity Recorder writes one output frame per rendered frame, so a take's timeline *is* the frame count. Both voices are scheduled on a single DSP instant so the two sides cannot start a frame apart. An audio-clock version of `Elapsed` was tried and produced a take 0.7 s shorter than the segment with the last utterance clipped; see the property's comment.

The Kokoro-82M TTS integration (`Assets/TTS/`, `KokoroTts`, `TtsSpeaker`) and the scripted `TriadConversation` were **deleted on 2026-08-10** when this replaced them. They are in the git history.

## Demo segments

The corpus is at `F:\Data\TalkingWithHandsCentered` (see its own README): `talkingwithHands-Audio/{main-agent,interloctr}/{wav,tsv}`, `SMPLX-60fps-grounded/` (the motion variant we use), and `EoT_TWH/` — 186 deduped conversations, 6,953 end-of-turn events typed 1 interruption / 2 overlapping / 3 turn-taking, times in ms.

`Tools/find_demo_segments.py` picks the stretch that gets played and exports it to `Assets/DemoSegments/<name>/` (git-ignored): two trimmed wavs plus `segment.json` (schema `gazecontrol.demo-segment/2`) with the turn schedule, the events and the motion frame offset. Motion is read from the corpus in place. Scene-1 selection requires 20-30 s, ≥2 events fully inside with lead, boundaries on utterance boundaries with nothing straddling them, and ≥2 s between consecutive turn instants; candidates are then ranked for demo readability. `--inspect <rank>` prints a segment as a transcript with its events marked.

**`--scene2` searches for turn-yields to the user instead**: a 20-30 s window that *ends* at a turn-taking event closing a monologue (no other annotated event inside by default; the other speaker limited to backchannels; the yielder talking ≥10 s), ranked with question-final closers in a hard first tier (the transcripts are punctuated — `?` is a direct test). The exported schedule names the user (**speaker code 0**) as the final turn's taker with the yield event's real index — no `-1` final turn — plus `yieldsToUser` and a `tailSeconds` hold the recorder reads (`DemoRecorder` holds that long after the voices stop; 0 = its 1.5 s default). A scene-2 export runs a self-check on the written JSON and fails loudly on any contract violation. `RecordedConversation.User` must be wired to the human participant for a yield segment to load, and `ProposedGazePolicy` plays the prototype's **listener track** for a participant in neither boundary role (in scene 2 that is the other agent; unreachable in scene-1 schedules). `--allow-internal-events` admits interruptions/overlaps (never internal turn-takings — whether a bystander could join after real turn-taking is a semantic judgment made by reading `--inspect`, not by rule).

Voices are **measured, not annotated** — see Agent appearance. `--voice male` walks the ranking and measures pitch lazily, so a filtered search only reads the audio it needs.

**The exporter peak-normalises the pair** (to 0.70, one gain for both sides from the louder track). The corpus records around **−42 dBFS**: at raw levels no frame reaches `VoiceRmsThreshold` (0.01), so baseline B never detects anyone speaking and both agents stare at the human user for the whole take. The gain is shared because the balance between the two speakers is recorded rather than incidental; it is written into `segment.json`. The base `wav/` tracks *are* properly speaker-separated (8-16× energy ratio during their own speaker's words) — only the level was wrong.

## Lip sync

Oculus LipSync (`Assets/Oculus/LipSync/`, third-party, verbatim) drives the mesh's 14 viseme blendshapes (indices 506–519, Oculus order minus `sil`) from each agent's `AudioSource`. Per agent: `AudioSource` + `OVRLipSyncContext` (`audioLoopback` **on**, or the voice is muted) + `OVRLipSyncContextMorphTarget` (`laughterBlendTarget` must be **−1**; the default 15 would drive an expression blendshape). The scene needs one `LipSync` GameObject with the `OVRLipSync` component. Nothing extra is wired for the demo segment: the context analyses whatever its `AudioSource` plays, which is the corpus wav.

The mesh carries a **mouth bag** (added in Blender) so the open mouth is no longer a hole. The bag is unmapped — all 161 of its vertices sit on UV (0, 0) — so it would otherwise take the colour of the albedo's bottom-left texel, which differs per agent texture. `SmplxMouthInteriorSubmesh` (an `AssetPostprocessor`, editor-only) therefore moves every triangle whose three vertices are all on UV (0, 0) onto submesh 1 at import and assigns `Assets/SMPLX/Materials/SMPLX-MouthInterior.mat`. Adjust that material to change how dark the cavity reads; no reimport needed. `GazeControl → Visemes → Open Jaw` drives the `aa` viseme in Edit Mode so you can see inside.

Caveat: with the Unity editor unfocused, the player loop and edit-mode skinning stall (`Time.time` freezes in Play Mode; `Camera.Render()` captures show stale meshes). Set `Application.runInBackground = true` **each Play Mode session** when driving the editor unfocused (the `PlayerSettings` flag does not govern editor play, and the runtime flag resets on exit). Skinning/blendshape changes made in the same editor command they're rendered in won't show — render in a later command.

## Gaze control

Gaze patterns come from the ICMI paper in `Assets/Docs/` (Figures 6–8: top gaze subsequences in the 1 s before turn events, 60 Hz, encoded over conversational roles — red = current speaker, blue = next speaker, green = listener; y-axis = gaze target). **All fifteen printed prototypes** are transcribed from the raw prototype archives (`raw_prototypes/*.npz` under `F:\aF\My_Papers\ICMI_2026___Explainable_Gaze_Patterns_for_Turn_Taking\`) by `Tools/build_gaze_patterns.py` into the generated `GazePatterns.g.cs` — never edit that file by hand. They are named by figure (`Fig6a`…`Fig8e`) and pooled by class: 5 interruption, 4 overlapping, 6 turn-taking. The generator asserts that Fig7e and Fig8d still decode to the two transcriptions the earlier demos were signed off on.

The **full gaze corpus** behind those prototypes — the complete per-frame sequences, not just the pre-turn second — is `Research/corpora/gaze_events/gaze_events_md6.csv`: 120,765 role-coded fixations over 38 sessions at 60 fps that tile each participant's timeline losslessly (its own README documents the schema and caveats; read §6 before trusting the `ad` role). `Tools/build_baseline_a_params.py` fits baseline A from it. Upstream sources, outside the repo: per-frame gaze targets in `F:\Data\GazePattern\Gaze_mocap_{2022,2024}_r40\<session>_gaze_target.csv` and EoT annotations in `F:\Data\GazePattern\EoT_{2022,2024}\<session>_eot.csv`, rebuilt into the CSV by the `gazeturn` package at `F:\Code\scnpro\prototype\prototype` (`python -m gazeturn.labeling.gaze_events`). Note `Research/corpora` is a plain local copy, not a junction — unversioned and not a backup.

**All gaze runs through `IGazePolicy`.** `GazeConditionRunner` builds one policy per agent, ticks it at a fixed 30 Hz decision rate decoupled from the frame rate, and applies the result; `RecordedConversation` drives only playback and the turn schedule and never touches a gaze target. The three experimental conditions are the `Condition` dropdown on the `GazeCondition` object:

- `SpeakerFollowing` — baseline B, voice-activity driven, never averts. §3's rules apply symmetrically to the agent itself: it will not switch on its *own* sub-600 ms utterance (a backchannel is a backchannel whoever produces it), and it never takes itself as addressee. `ConversationState.CurrentAddressee` belongs to whoever holds the floor, so with real audio it names *this* agent whenever it talks over someone — which is what a backchannel is. In that case the agent it is addressing is the **floor-holder it is answering** (`CurrentSpeaker`); the human fallback applies only when neither field names anyone else. Falling back to the human directly was tried and left an agent staring at the user for 66% of a take.
- `RoleConditioned` — baseline A, the Shintani et al. 2024 model re-fitted to our corpus (`Assets/GazeControl/Resources/BaselineAParameters.json`, regenerated by `Tools/build_baseline_a_params.py`).
- `Proposed` — a prototype played over a **holding-replay substrate**, overriding it only in the window before each annotated end-of-turn event. Outside the windows the agent replays measured holding-period fixation stretches from the full gaze corpus, conditioned on its current role (`HoldingReplayGazePolicy` + the committed `Assets/GazeControl/Resources/HoldingSequences.json`, regenerated by `Tools/build_holding_sequences.py`); replay aversion renders as recentred eyes, exactly like the prototypes' `None`, and the stretch draws are **per-agent** seeded streams — holding gaze is uncoordinated in the corpus. `ProposedSettings.Substrate = SpeakerFollowing` is the prototype-only ablation: bit-for-bit baseline B outside the windows, isolating the pattern exactly (the pre-2026-08-14 arrangement). Each event draws its own prototype from the pool matching **its own class**, so an interruption is preceded by an interruption prototype. That draw is `FNV-1a(BaseSeed, eventIndex)`, deliberately *not* per-agent — both agents must play tracks of the same prototype — and the whole mapping is written into the log's metadata sidecar before the first boundary. `ProposedSettings.Selection = Fixed` pins one prototype instead; `PatternTimeScale` 1 is data-faithful.

`ConversationDirector` publishes the segment's turn schedule (who holds the floor, who takes it next, when the boundary is, and which event it is) so a policy can act *before* a boundary rather than react after it; baselines A and Proposed require it. **It is driven, not self-advancing**: `GazeConditionRunner` calls `SyncTo(k · step)` once per decision tick, so every time the director publishes — time to the boundary, time since the last one, turn phase — is measured on the decision grid rather than at the sub-frame instant the frame clock happened to read. It used to pull a clock delegate on every read (to dodge the `Update` ordering between it and the runner); that made two bakes of one seed disagree on single-frame prototype segments, and being driven removes the ordering hazard outright. The segment's final turn carries `eventIndex = -1` and no prototype fires there.

`GazeController` is the shared animation layer — identical in every condition, or the study measures animation instead of gaze policy. It drives the SMPL-X eye bones only: `HeadContribution` is **0**, so the head stays on pure mocap. Yaw and pitch are clamped separately (±35° / ±25°). Aversion is a direction (`SetAversion(yaw, pitch)`, eye-in-head); a zero offset means "eyes recentred in the head", which is how the proposed condition renders the prototypes' `None`. No blink model (SMPL-X has no eyelid shapes) and no idle micro-motion — body motion comes from the mocap clips.

`ListenerCamera` remains in the codebase but is wired to nothing since gaze moved behind the policy interface.

## XR — the study rig

The study runs live in a **Varjo** headset on **tethered PC VR**. The backend is the **Varjo Unity XR plugin** (`com.varjo.xr`), which is the active Standalone loader; `com.unity.xr.openxr` stays installed but inactive. Settings live in `Assets/XR/` and must be committed.

**It was OpenXR first, and the switch is deliberate** (2026-08-27). The port was built and verified against OpenXR, and on real hardware that path works: the runtime reports itself as `Varjo OpenXR Runtime 4.14.0`, renders stereo, gives a Floor origin and holds 90 fps with both agents. What it would not give is gaze — `XR_EXT_eye_gaze_interaction` was both available *and* enabled on the instance, yet no gaze device ever appeared (`GetDevicesWithCharacteristics(EyeTracking)` returned 0, nothing exposed `eyesData`). Varjo's own plugin is the answer to that, and it gives more than the extension does: per-eye rays, pupil size and focus distance rather than one combined ray. **The eye-gaze measures of §7 are built on `VarjoEyeTracking`, not on OpenXR input.** The OpenXR feature set (Eye Gaze Interaction plus the Index / Vive / KHR controller profiles) is still configured, so switching the loader back is a one-line change if the vendor path ever disappoints.

**XR does not start by itself, on purpose.** "Initialize XR on Startup" is unchecked, and `XrParticipantRig.StartXrOnPlay` (off in the committed scene) is the only switch. Baking gaze tracks, browsing clips and recording demo videos all press Play on a machine that may have no headset, and auto-initialisation would break every one of them.

The rig is `User` (the triad vertex, unmoved) → `Camera Offset` (carries the eye-height calibration) → `Main Camera` (`TrackedPoseDriver` on the centre eye). With XR off the camera is parked at **1.60 m**, exactly where it has always sat, so desktop takes render the same frames as before the port. `GazeControl → Set Up XR Rig` builds or repairs the rig; nothing else in the scene changed, because the agents already aim at the user's `LookAtAnchor` and that is this camera — in a headset they aim at the participant's real head.

**Eye-height calibration normalises every participant to 1.60 m** (`EyeHeightCalibration`, pure and unit-tested; `XrParticipantRig.Calibrate()`). The agents are standing mocap with fixed head height, so without it a short participant looks up at them and a tall one looks down — height would be an uncontrolled between-participant variable in exactly what the study measures. Samples outside 1.20–2.10 m are **rejected, not clamped** (the headset on the desk), leaving the rig where it was. Re-calibrating measures the participant again rather than stacking offsets.

**Verified on a real Varjo, 2026-08-27, through the OpenXR path**: subsystems start, tracking origin is Floor, stereo renders, the head pose drives the camera, and the scene holds **90 fps** with two SMPL-X agents, 486 correctives and LipSync — the frame-rate risk is closed. Before that it was checked under the **OpenXR Mock Runtime** (disabled in the committed settings; re-enable temporarily to repeat), which also showed calibration moving a simulated 1.85 m participant's eyes to exactly 1.600 m.

**Participant gaze is logged, in its own file on the decision grid.** `ParticipantGazeLogger` (on the `User` vertex, driven by `GazeConditionRunner.Tick`, built and wired by `GazeControl → Set Up XR Rig`) writes `<stem>_user.csv` into the take's folder beside the twenty-column agent log, one row per decision tick: tracker and per-eye status, the combined and per-eye rays in **world space**, head pose, focus distance, pupil diameters and eye openness, plus what the ray resolved to. Two files rather than more columns on one, because the agent log records what was *rendered*, once per frame, and this records what was *decided against*, once per tick — merging them would take one of the two off its own clock.

Gaze comes through `IParticipantGazeSource`; `VarjoGazeSource` is the only implementation and it drains `VarjoEyeTracking.GetGazeList` (one native fetch returning aligned gaze/measurement pairs — `GetGaze` + `GetEyeMeasurements` are two fetches and can straddle a tracker frame). The tracker runs at 100–200 Hz against the 60 Hz grid, so a tick with no new frame repeats the last sample and the logged `device_frame` is what says so.

**Which agent is being looked at is a head *sphere*, not a cone** (`GazeSphereTargeting`, pure and unit-tested): an angle is a different amount of head at every distance and the participant can step. That makes the two halves of mutual gaze use different criteria — the participant→agent half a 0.15 m sphere, the agent→participant half the runner's 15° cone — so `target_angle_deg` is logged and the sidecar records both thresholds, leaving the threshold an analysis choice rather than a baked-in one. `agents_looking_at_user` is a bit set over agent ids, so gaze-return latency (§7.4) needs no join against the agent log. **Nothing is written on a desktop Play** (baking, clip browsing and Record Demo all run headless), and a `StudySession` now also refuses to start with no participant gaze logger wired.

**Still unverified, and the first thing to do next**: nothing has been run through the *Varjo loader* yet — the measurements above are the OpenXR path, and the loader switch came after them. Also open: eye-height calibration has never been run on a real worn headset, **no gaze has yet been read from real hardware** (the pipeline above is built and tested but has only ever seen "no headset"), §7's four derived measures are not computed anywhere, there is no in-VR questionnaire, and per-participant calibration is not persisted. Note for whoever debugs gaze: Varjo gates eye tracking behind a permission in **Varjo Base** and behind its own per-wearer ET calibration (`XrParticipantRig.CalibrateEyeTracking()` requests it), and some runtimes suppress gaze entirely while the headset is off the head — `VarjoGazeSource.UnavailableReason` names whichever of the three failed.

## Agent appearance

**Match the textures to the segment's voices.** The corpus records nothing about who is speaking, so `Tools/find_demo_segments.py` measures it: median F0 over each speaker's own voiced frames, written into `segment.json` as `voicePitchHz`/`voice`, and filterable with `--voice male|female|mixed`. The scene wires agent A to the **stock** female albedo `smplx_texture_f_alb.png` and agent B to `smplitex_f00021_alb.png`, both on `SMPLX-Female-URP-AgentA/B.mat` with the stock female normal map. The male pair (`SMPLX-Male-URP`, `SMPLX-Male-URP-AgentB`) is kept for when a male segment is used.

A SMPLitex output needs two passes before it is usable, both editor menu items under **GazeControl → Textures**:

1. **Composite SMPLitex Eye Disc** — SMPLitex never generates the eyeball UV island and leaves it black, and both eyes share that one island, so an untreated texture gives an agent black eyes. The disc is copied from a stock SMPL-X albedo; it sits at (296, 221) r≈19 in 512²-scale, **top-left origin**.
2. **Pad SMPLitex UV Islands** — fills the gaps between islands from the island edges, using the mesh's own UVs as the coverage mask so nothing a triangle covers is ever written. It also repairs the generator spilling a flat background colour *into* an island, but only when the background is measurably flat (the mode holds ≥50% of outside texels); every female SMPLitex texture tried fails that test and is padded only.

Only the *body* is female. `SMPLX.modelType` stays `Male` on purpose: it selects the betas-to-joints regressor and so moves the **skeleton**, not the mesh surface, and the FBX carries one template mesh. A genuinely female body would need a female template export or non-zero betas.

**Known artifact: SMPLitex textures do not register with the mesh's UVs on the face.** The nostril shading lands below the actual nostrils, on the upper lip and nasolabial fold, and the same offset is visible on every SMPLitex sample tried (`f00016`, `f00017`, `f00021`). The **mesh is not at fault** — this was tested on 2026-08-16 by putting the stock Meshcapade albedo on agent A: its nostrils land exactly right on the same mesh, same material, same normal map, while agent B's SMPLitex texture beside it stays displaced. So the misregistration is in what SMPLitex generates, not in the FBX's UVs or the Blender mouth-bag re-export. Nothing here corrects it: a fix would have to warp the generated face island onto the SMPL-X template, which is a real piece of work and has not been attempted.

Because of that, **agent A wears the stock female albedo** (`smplx_texture_f_alb.png`) as of 2026-08-16 — it needs neither of the two passes below (it is the eye-disc donor and its islands are properly laid out). Agent B is still on SMPLitex and still shows the offset. There is only one stock female albedo, so B cannot take the same fix without the two agents becoming the same person.

Agent A's earlier sample `smplitex_f00016_alb.png` was deleted on the same day (it fit the body badly and carried yellow-green painted onto the fingers and toes *inside* the UV islands, an artifact of the sample that no colour statistic could separate from legitimate content). It lives only in git history.

## Motion data conventions

TalkingWithHands clips come in pairs from the same recorded take: one `interloctr` file and one `main-agent` file whose names differ only in that token. **The two agents must always play a matched pair**: one agent uses the `interloctr` clip and the other the `main-agent` clip of the same take. Mixing takes would put two unrelated conversations in one room, and the corpus is *Centered* — each file's `trans` and global orientation are expressed in a frame anchored on that file's main agent, so only the two sides of one stem are mutually consistent.

`SmplxMotionPlayer` plays a clip whole or as a segment (`StartFrame`/`FrameCount`), and root translation is anchored on the segment's **first frame**, not the clip's — several minutes into a recording the root has wandered far from where it started. Clear `Autoplay` when an external timeline calls `Seek`, which is what `RecordedConversation` does.

Only two example clips are committed (via LFS); other `.npz` files under `Assets/MotionData/` are git-ignored, and the demo reads its motion straight from the corpus.

## Progress board

`PROGRESS.md` at the repo root is the project's working memory: nested checkbox task list, decisions log, and open questions. Keep it current via the `progress-board` skill — update it whenever a task starts/finishes, a decision is made, or before committing. `progress.html` is auto-generated from it by the pre-commit hook in `.githooks/` (enabled per clone with `git config core.hooksPath .githooks`) — never hand-edit the HTML.

## Working in this repo

- This is a Unity project: every asset file has a paired `.meta` file. When adding, moving, or deleting assets outside the Unity Editor, keep `.meta` files consistent (let the Editor generate them where possible, and always commit them together with their asset).
- Scenes, prefabs, and most `.asset` files are Unity YAML (see `.gitattributes`); binary media (models, textures, audio) go through **Git LFS**.
- Tests use the Unity Test Framework and run inside the Unity Editor (Test Runner window, or via the JetBrains Unity test tooling when available).
- **`Research/` holds local-only material** — raw gaze corpora (`corpora/`), reference PDFs (`papers/`), working notes (`notes/`). Everything there is git-ignored except its `README.md`. It sits outside `Assets/` deliberately: Unity imports every file under `Assets/` regardless of `.gitignore`, generating `.meta` files and Library entries for data that is never a runtime asset. Put reference material there, not under `Assets/Docs/`.
