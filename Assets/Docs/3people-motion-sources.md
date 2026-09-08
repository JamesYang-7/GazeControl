# Three-party mocap → SMPL-X: source directories

Reference for the tool that converts the **3People-2022** three-party conversation
capture into SMPL-X skeleton animation. This document records *where things are* and
*what format they are in*; the converter's own design lives elsewhere.

Surveyed 2026-09-07. All four roots are **outside this Unity repository** and are not
versioned by it.

## The four roots

| Symbol | Path | What it is |
| --- | --- | --- |
| `{TPC_CODE}` | `F:\Code\Multi-TPC` | Data-processing repo for the dataset (Python/Jupyter/MATLAB/Praat). The dataset's own documentation. |
| `{TPC_DATA}` | `D:\3People-2022` | The **processed** data — mocap exports, gaze, audio, captions, annotations. ~13 GB plus zips. |
| `{TPC_VICON}` | `D:\3People-2024\3Party-Conversation-2022` | The **original Vicon project** — C3D markers, per-participant VSK skeleton templates, raw camera data. Upstream of everything in `{TPC_DATA}/mocap`. |
| `{TPC_VIEWER}` | `F:\Code\Conversation_Demo` | C++/OpenGL visualizer for the original (non-SMPL-X) skeleton. The reference implementation of how the motion files are interpreted. |

Write `{TPC_DATA}/mocap/...` rather than a bare path when referring to something over
there — the same convention `{PAPER_ROOT}` uses in `CLAUDE.md`.

`{TPC_VICON}` sits under `D:\3People-2024`, which also holds the *2024* capture
(`3Party-Conversation-2024`) and ~330 GB of D-Lab experiment files. Only the
`3Party-Conversation-2022` subtree is upstream of this dataset.

Both code roots are git repositories with public upstreams:

- Multi-TPC — dataset DOI [10.5281/zenodo.17935560](https://doi.org/10.5281/zenodo.17935560); authors Meng-Chen Lee (UH) and Zhigang Deng. MIT.
- Conversation_Demo — `github.com/MCMartinLee/Conversation_Demo`. MIT.

---

## `{TPC_CODE}` — `F:\Code\Multi-TPC`

*A Multimodal Dataset for Three-Party Conversations with Speech, Motion, and Gaze.*

```
Multi-TPC/
├── README.md                  # capture rig, tool chain, pipeline overview, citation
├── requirements.txt           # pandas, numpy, matplotlib, scipy  (py >= 3.8)
├── LICENSE                    # MIT
├── Analysis.ipynb             # worked example over the released dataset (~545 KB)
├── Figures/
│   ├── layout.png             # capture-room layout of the three participants
│   ├── clapboard.png          # the marker-instrumented sync clapboard
│   ├── skeleton.png           # joint-index diagram for the motion export
│   ├── joints_table.png       # joint index → txt column mapping
│   └── conversion.png         # pixel gaze target → pitch/yaw derivation
├── Processing/
│   ├── Motion/                # README.md, csvtotxt-3p.py, Mocap_Separate.ipynb
│   ├── Gaze/                  # README.md, clean-eye-tracker-data.py,
│   │                          #   GazeAngleConvert.ipynb, GazeProcessing.mlapp
│   ├── AudioAndText/          # README.md, Captions.ipynb, PitchAndIntensity.praat
│   └── Annotation/            # README.md, Annotation.ipynb, GesturalBackchannel.ipynb
└── Utils/
    └── data.py                # motion_load(), align_data(), outlier/gap handling, chunking
```

**Read first for the conversion tool:** `Processing/Motion/README.md`,
`Processing/Motion/csvtotxt-3p.py`, and `Utils/data.py::motion_load` — together they
define the per-participant motion file that the SMPL-X converter consumes.

Capture and pre-processing chain, per the root README: Vicon IQ (mocap, gap
interpolation, smoothing) → D-Lab (gaze + audio) → Audacity (trim, mute other
speakers per track) → Praat (pitch/intensity). All modalities are synchronised on a
**physical clapboard carrying mocap markers**.

## `{TPC_DATA}` — `D:\3People-2022`

```
3People-2022/
├── 3people.zip                     # 3.4 GB   archived copy of the processed set
├── 3people_audio.zip               # 9.1 GB
├── 3People2022_raw_audio_wma.zip   # 735 MB
├── raw_data/<date>/pc<N>-<Name>/audio/     # 4 dates, 3 participants each
├── audio_text/<date>/
│   ├── Session_<S>_PC_<N>_audio.wav
│   └── Caption_SentenceBased/
└── mocap/                          # ← the working tree; everything below lives here
    ├── <date>/                     # 7 dates (see table)
    │   ├── Mocap/
    │   │   ├── Session_<S>_mocap_data.csv        # all 3 participants stacked
    │   │   └── Separate/Session_<S>_PC_<N>_mocap_data.txt   # ← converter input
    │   ├── Gaze/
    │   │   ├── Session_<S>_PC_<N>_EyeTracker_data.csv       # timestamp,x_px,y_px
    │   │   ├── Session_<S>_PC_<N>_EyeTracker_data_gapfilled.txt
    │   │   ├── Seperate/  (sic)                             # per-participant txt
    │   │   └── Final.mlapp
    │   ├── Caption/ , Caption_SentenceBased/     # *_sentence.csv, *_words.csv
    │   ├── subtitles/                            # YouTube .srt / .vtt / .json
    │   ├── Session_<S>_gazeBehavior.txt          # discrete gaze target, 3 columns
    │   ├── Notes.xlsx
    │   └── Train/            (12-15-2021 only)
    ├── Analysis/             # all_frame.csv, keeping_frames.csv, taking_frames.csv
    └── Training/             # AudioAndGaze{,-Old,-Oldest}/, Intervals/
```

### `Notes.xlsx` — the participant register and the sync sheet

One per date under `mocap/<date>/`, and it is the **authority on who is who**. Session 1
names each machine — `PC 1 (Crystal)`, `PC 2 (Maryum)`, `PC 3 (Justice)` — and every
session then records, per PC:

- `Audio start/stop` and `Visual start/stop`, as both `mm:ss.fff` and **UTC epoch ms**
- a combined `Start (UTC)` / `Stop (UTC)`, `Total frames`, and an `Offset (frames)`
  against the mocap (1-4 frames on the dates checked, at 60 fps)
- a `Mocap` row with `Start` / `Stop` / `Total frames` — for 12-15-2021 Session 1 that is
  951 / 37249 / 36298, exactly the range measured in the export

So the per-stream alignment does not have to be estimated: the sheet carries the offsets.

Sessions per date:

| Date | Sessions | `Separate/` txt files |
| --- | --- | --- |
| 12-15-2021 | 4 | 12 |
| 01-28-2022 | 6 | 18 |
| 02-11-2022 | 2 | 6 |
| 03-04-2022 | 3 | 9 |
| 03-05-2022 | 2 | 6 |
| 03-11-2022 | 3 | 9 |
| 03-12-2022 | 3 | 9 |

**69 participant-sessions in total** (23 sessions × 3 participants) with motion.
`raw_data/` covers only 4 dates and includes `12-10-2021`, which has no `mocap/` entry.

## `{TPC_VICON}` — `D:\3People-2024\3Party-Conversation-2022`

The original Vicon Nexus/IQ project. **This is the ground truth**: everything in
`{TPC_DATA}/mocap` is derived from it.

```
3Party-Conversation-2022/
├── *.enf                       # Vicon project files
└── <date>/                     # 9 dates, unpadded (1-28-2022, 3-4-2022 …)
    ├── <date>.Capture day 1.enf
    └── Session 1/
        ├── <Name>.vsk          # ← per-participant scaled skeleton template
        ├── <Name>-ROM.trial/.x2d   # range-of-motion calibration take
        ├── Conversation*.trial/.x2d  # raw 2D camera data per take
        ├── Session 1.cp        # camera calibration
        ├── ExtraParametersTarsus30.txt
        └── c3d/
            ├── convo<S>_pc<N>.c3d      # ← reconstructed markers, one participant each
            └── convo<S>_pc<N>.Take*.enf
```

Date coverage — note two dates have templates and raw camera data but **no reconstructed
C3D**, and `4-1-2022` has no counterpart in `{TPC_DATA}` at all:

| date | C3D | VSK subjects |
| --- | --- | --- |
| 12-10-2021 | **0** | Alexander, Christian, Feyza |
| 12-15-2021 | 12 | Crystal, Justice, Maryum |
| 1-28-2022 | 18 | Benjamin, Clayton, Karthik |
| 2-11-2022 | 6 | Kevin, Saifur, Sana |
| 3-4-2022 | 9 + `Convo002.c3d` | Fatima, Joseph, May |
| 3-5-2022 | 6 | Aditi, Ahmed, William |
| 3-11-2022 | 9 | An, Eric, Robert |
| 3-12-2022 | 9 | Alahnna, Elian, Jericka |
| 4-1-2022 | **0** | Aditi, Christian, Karthik, Maryum |

### The VSK is the skeleton definition that was missing

Each `<Name>.vsk` is XML: 19 `<Segment>`s, 41 `<Marker>`s, 105 `<Parameter>`s. It answers
by construction what had to be inferred from the viewer:

- **The hierarchy**, and its segment order is exactly the export's column order.
- **The joint types and their degrees of freedom** — 3 `JointFree` (pelvis, thorax, head),
  4 `JointBall` (femurs, humeri), 4 `JointHinge` (tibiae, toes), 8 `JointHardySpicer`
  (feet, clavicles, radii, hands). **This explains all fourteen always-zero export
  columns**, exactly and without exception: `ltibia` is a hinge about `AXIS=0 1 0`, so
  only its Y column is written; `lclavicle` is a Hardy-Spicer about `(1,0,0)`/`(0,0,1)`,
  so its Y is zero; `lradius` about `(0,1,0)`/`(0,0,1)` zeroes X; `lhand` about
  `(-1,0,0)`/`(0,1,0)` zeroes Z. The Euler triplets are each joint's own DOF padded to
  three, written into the slot matching the axis — no permutation.
- **The rest pose, numerically.** `EDIT_POSE_MEAN` is `1.5708 0 0` on `lhumerus` and
  `-1.5708 0 0` on `rhumerus`, zero everywhere else except a −25° elbow (`lradius`
  `-0.436332`). That is the ∓90° arm rotation `{TPC_VIEWER}` hardcodes, confirmed from
  the model rather than inferred from a screenshot.
- **Per-participant anthropometrics** — `InterHip, Thigh, Shin, Foot, Shoulder,
  ClavHeight, ShoulderHeight, UpperArm, LowerArm, Back, Neck` in mm, for all 28
  subject-days. Crystal, for instance: Thigh 462.0, Shin 334.9, UpperArm 253.2,
  LowerArm 219.0, Back 124.5, Neck 323.2. The remaining ~94 parameters are per-marker
  placement offsets.
  - **`12-10-2021/Feyza.vsk` is an unfitted default** — every value a round number
    (Thigh 400, Shin 400, UpperArm 250, Neck 400). That subject was never scaled. It is
    on a date with no C3D anyway.

Note the native joint parameterisation is **axis-angle** for ball/free joints and a signed
angle about a named axis for hinge/Hardy-Spicer — *not* Euler. The `-local-euler.csv`
export is a conversion Vicon applied on the way out, which is why its order is a property
of the exporter rather than of the model, and why the VSK does not settle it either.

### The C3D markers — 70 files, and yes, some are short

Standard 41-marker Vicon Plug-in Gait set, **60 Hz**, one participant per file, subject
name prefixing every label (`Crystal:LFHD`, …), float storage, millimetres, Z-up — the
**same room frame as the processed export** (first-frame `LFHD` z ≈ 1604 mm against
pelvis z ≈ 919 mm, matching x/y).

```
LFHD RFHD LBHD RBHD | C7 T10 CLAV STRN RBAC | LSHO LUPA LELB LFRM LWRA LWRB LFIN
RSHO RUPA RELB RFRM RWRA RWRB RFIN | LFWT RFWT LBWT RBWT
LTHI LKNE LSHN LANK LHEE LTOE LMT5 | RTHI RKNE RSHN RANK RHEE RTOE RMT5
```

**62 of the 70 files carry the full 41. Eight are short by one or two**, consistently per
subject rather than randomly:

| file(s) | subject | missing |
| --- | --- | --- |
| 12-15-2021 convo1, convo2 pc2 | Maryum | `T10`, `RHEE` |
| 12-15-2021 convo3 pc2 | Maryum | `RHEE` |
| 3-4-2022 convo2, convo3 pc2 | May | `T10` |
| 1-28-2022 convo5 pc1 | Clayton | `LANK` |
| 3-12-2022 convo1 pc1 | Elian | `RSHO` |
| 3-5-2022 convo2 pc1 | Ahmed | `RTHI` |

The subject's own VSK still declares all 41, so these are labelling/reconstruction
failures for that take, not a different marker set. **Within a file there *are* gaps,
and they are stored as all-zero points rather than flagged invalid** (corrected
2026-09-07; the earlier "no gaps" came from sampling too few frames): on Benjamin,
01-28-2022 convo1, `C7` is zero in 52% of frames, `LBWT` 40%, `RFWT` 20% — the trunk
and hip markers that arms and clothing occlude. A marker consumer must drop zero points,
key off the label list per file, and tolerate a missing trunk or limb marker rather than
assume 41.

Two more traps:

- **`3-4-2022/Session 1/Convo002.c3d` (194 MB, 246 points) is the un-split original** —
  all three participants plus model outputs in one file. Every other C3D is per-participant.
- **`2-11-2022/convo3_pc{1,2,3}.c3d` report 65535 frames, which is the 16-bit header field
  saturating.** The true length is **69,507** frames (1158.5 s), recoverable from the file
  size or `POINT:FRAMES`. Trusting the header truncates that take by 4,000 frames.

### Frame alignment with the processed export is direct

Each `Session_<S>` in `{TPC_DATA}` corresponds to `convo<S>` here, and the export's
`Frame` column is the C3D frame index — every session's range sits inside its take with a
margin at both ends:

| session | export `Frame` range | frames | C3D frames |
| --- | --- | --- | --- |
| 1 | 951 – 37248 | 36,298 | 37,561 |
| 2 | 424 – 38210 | 37,787 | 38,383 |
| 3 | 300 – 40000 | 39,701 | 40,375 |
| 4 | 632 – 50133 | 49,502 | 50,352 |

(12-15-2021; the same holds on the other dates.) So markers and joint angles can be put
on one clock without any search or correlation.

## `{TPC_VIEWER}` — `F:\Code\Conversation_Demo`

OpenGL/Assimp/ImGui viewer: loads three participants' motion txt onto a Mixamo-rigged
`male3.dae`, free camera, optional eye-gaze rays and head spheres.

```
Conversation_Demo/
├── README.md, LICENSE, Demo.sln, .gitignore
├── All bones.txt                # the 52 mixamorig_* bones of the viewer's rig
├── Demo/                        # the viewer      — main.cpp (1061 lines), GLSL, ImGui, glad
│   ├── main.cpp                 #   txt → bone rotations → skinned playback
│   ├── Head_rotate_<N>.txt , head_eye_angle<N>.txt
│   └── *.glsl / *.vs / *.fs     # anim_model, cubemaps, eye, materials, model_loading, normal
├── Data/                        # the gaze-target extractor — main.cpp (254 lines)
├── Libraries/                   # Includes/ (assimp, glm, glad, GLFW, animation.h, bone.h …),
│                                # Libs/ (*.lib), dlls/ (assimp, glfw3, irrKlang, ikpMP3)
├── Release/                     # downloaded release payload (git-ignored upstream)
│   ├── Release/                 #   Demo.exe, Data.exe, shaders, dlls
│   ├── Resources/               #   male3/male3.dae + textures, room/, big-room/,
│   │                            #   sphere/, skybox/, textures/
│   └── Conversation/            #   sample Session_<S>_PC_<N>_{mocap,EyeTracker}_data.txt, 4People/
└── Snapshot/                    # README screenshots
```

`Demo/main.cpp` is the **authoritative reader** of the motion txt; `Data/main.cpp` is the
same reader plus the ray/sphere test that produces `Session_<S>_gazeBehavior.txt`
(the discrete "looking left / right / neither" annotation).

Build: Windows 10/11, Visual Studio 2015–2022, `Demo.sln`. Run:
`Release/Release/Demo.exe`.

---

## The motion format the converter has to read

### Vicon export → per-participant txt

`Mocap/Session_<S>_mocap_data.csv` is the raw Vicon IQ local-euler export with the three
participants **stacked vertically**: a name line, a header line, the frames, then three
blank lines before the next participant. `csvtotxt-3p.py` (and `Mocap_Separate.ipynb`)
slices those blocks, drops the `Frame` column, moves the nine position columns to the end,
and writes space-separated `Separate/Session_<S>_PC_<N>_mocap_data.txt`.

Example, 12-15-2021 Session 1: CSV 108,906 lines → three txt files of 36,298 frames each;
`Frame` runs 951 → 37,248 (capture starts partway in — the clapboard sync offset).

### Column layout — 66 space-separated floats per line

19 joints × 3 Euler angles (**degrees**) = 57, then 3 joints × 3 positions
(**millimetres**) = 9.

Rotation order (`Utils/data.py::motion_load`, index `j` as used by both `main.cpp`s):

| j | joint | j | joint | j | joint |
| --- | --- | --- | --- | --- | --- |
| 0 | pelvis | 7 | rfoot | 14 | lhand |
| 1 | lfemur | 8 | rtoes | 15 | rclavicle |
| 2 | ltibia | 9 | thorax | 16 | rhumerus |
| 3 | lfoot | 10 | head | 17 | rradius |
| 4 | ltoes | 11 | lclavicle | 18 | rhand |
| 5 | rfemur | 12 | lhumerus | | |
| 6 | rtibia | 13 | lradius | | |

Then positions: `j=19` pelvis (x,y,z), `j=20` thorax, `j=21` head.

Fourteen columns are **identically zero** in every file and `motion_load` drops them:
`ltibia_x, ltibia_z, ltoes_x, ltoes_z, rtibia_x, rtibia_z, rtoes_x, rtoes_z,`
`lclavicle_y, lradius_x, lhand_z, rclavicle_y, rradius_x, rhand_z`.

### Hierarchy

From `Figures/skeleton.png` and the Mixamo correspondence in the table. Three chains off
the pelvis, and **that is all** — no lumbar subdivision, no neck, no fingers:

```
pelvis ── thorax ── head
   │         ├───── lclavicle ── lhumerus ── lradius ── lhand
   │         └───── rclavicle ── rhumerus ── rradius ── rhand
   ├───── lfemur ── ltibia ── lfoot ── ltoes
   └───── rfemur ── rtibia ── rfoot ── rtoes
```

### Rest pose: arms straight down at the sides

`Figures/skeleton.png` shows the reference pose with both arms hanging vertically, and
the viewer confirms it independently: `Demo/main.cpp` pre-rotates the Mixamo upper arms
by ∓90° about Z before applying the animated rotation (`j=12` left −90°, `j=16` right
+90°), which is exactly the correction that folds a Mixamo T-pose arm down to the side.
So the exported angles are relative to an **I-pose**, not a T-pose. A SMPL-X retarget
needs its own equivalent, measured against SMPL-X's own (near-T) rest pose.

### Conventions established by the viewer

- **Frame rate 60 fps** (`Utils/data.py::align_data` divides the audio sample rate by 60;
  `Demo/main.cpp` carries a commented-out `k % 2` decimation to 30 Hz).
- **Millimetres, Z-up, right-handed** — the native Vicon frame. `position_scale = 1000.0f`
  converts to metres and the file's `(x, y, z)` is read into the viewer's Y-up GL frame as
  `(y, z, x)`, i.e. `GL_x ← file_y, GL_y ← file_z, GL_z ← file_x`.
- **The three translation triplets are not the same kind of quantity** (measured over
  12-15-2021 S1 PC1, every 12th frame):

  | | mean (mm) | s.d. (mm) | what it is |
  | --- | --- | --- | --- |
  | `pelvis<t-*>` | (478.9, −178.6, 917.5) | (13.6, 7.9, 1.1) | **global room position** of the root; z ≈ 917 mm is standing hip height |
  | `thorax<t-*>` | (−74.1, 3.7, 94.5) | (4.6, 2.1, 3.4) | **constant local bone offset** from pelvis, ‖·‖ ≈ 120 mm |
  | `head<t-*>` | (2.8, 5.8, 335.2) | (5.0, 2.2, 1.4) | **constant local bone offset** from thorax, ‖·‖ ≈ 335 mm |

  This is how both `main.cpp`s use them — `globalHips · [R_pelvis · T(thorax_off)] ·
  [R_thorax · T(head_off)] · R_head`. Limb lengths are absent *from the export*, but they
  are in the VSK (see `{TPC_VICON}`), so full per-participant proportions are available.
  Participants barely translate (pelvis s.d. ≈ 14 mm over a whole session), so root
  motion is close to negligible for a seated triad.
- **Rotations are local to the parent**, in degrees, in the file's own axes.

### ⚠ The two reference readers use different Euler orders

Neither is documented, and they are **not equivalent**. Expressed in the file's own axes
(after undoing the GL remap above):

| | composition | source |
| --- | --- | --- |
| `Data/main.cpp` | `Rx(aX) · Ry(aY) · Rz(aZ)` — **XYZ** | explicit `glm::rotate` sequence about GL Z, X, Y |
| `Demo/main.cpp` | `Rx(aX) · Rz(aZ) · Ry(aY)` — **XZY** | `glm::quat(vec3(y, z, x))`, whose constructor (verified in the vendored `glm/detail/type_quat.inl:204-213`) composes `Rz(.z)·Ry(.y)·Rx(.x)` |

They agree only to second order in the two smaller angles. On a real pelvis row the
quaternions differ by ≈ 5°; the head carries the largest secondary angles in the data
(aY up to ±21°, aZ up to ±38°), so this is exactly the joint where the discrepancy is
worst — and head orientation is what this project is for.

**The export alone cannot settle it**, and the VSK does not either — the model stores
ball/free joints as axis-angle, so the Euler order belongs to the exporter, not the model.
Within the export there is no redundancy to exploit: only three joints have translations
and two of those are constant bone offsets.

**Resolved 2026-09-07: it is XYZ, and the C3D markers are what settle it.** Pose the
source skeleton itself — the VSK's segment lengths, the export's angles and its thorax
and head offsets — and fit one constant local offset per marker to the segment it rides
on. A rigidly attached marker leaves millimetres under the right order and tens of
centimetres under a wrong one. `Tools/convert_3people_smplx.py --check-markers` does
this under all six orders; on ten subjects over three dates XYZ leaves **2-6 mm RMS**
(one subject 20 mm, from a slipped left-shoulder marker) against **40-300 mm** for each
of the other five. The two readers' disagreement is therefore a bug in `Demo/main.cpp`,
the renderer, not an open convention.

Over all 69 participant-sessions the XYZ residual has median 8.7 mm; 18 exceed 15 mm,
and in every one of those the excess sits on one or two segments — a finger marker at
100-250 mm (`LFIN`/`RFIN` on Benjamin, Karthik, An, Jericka), a head marker at 50-110 mm
(Clayton, Elian, Ahmed), or a trunk marker (Eric, 03-11-2022 S3, thorax 322 mm) — while
the rest of the body stays at millimetres. Those are markers that slipped or were
mislabelled in that take, and the list is the first place to look if the marker-fitting
route is ever built.

The former justification for XYZ — that it produced the published
`Session_<S>_gazeBehavior.txt` — turned out to be no justification: a faithful re-run
of `Data/main.cpp` agrees with that file on 25% of rows (01-28-2022 Session 1), under no
Euler order or eye-angle convention, and the file codes P1 as looking at nobody 97% of
the time there and 100% in Session 3. It was evidently produced from other inputs.

Marker-based fitting of SMPL-X (shape and pose) remains the higher-fidelity route; the
markers are already aligned with the export (same room frame, `Frame` is the C3D index).

### Other viewer details

- **Only 19 of the viewer's 52 bones are driven.** `Demo/main.cpp` binds
  Hips, LeftUpLeg/Leg/Foot/ToeBase, RightUpLeg/Leg/Foot/ToeBase, Spine1, Head,
  Left/RightShoulder, Left/RightArm, Left/RightForeArm, Left/RightHand.
  **No finger bones and no neck** — hands are rigid. Note the viewer's bone order is not
  the file's joint order (its index 9 is Spine1 ← file `thorax`, index 10 Head ← `head`).
- **Head/eye geometry used by the gaze extractor**: `eye_height = 1.667 m`,
  `eye_front = 0.198 m` (eye offset forward of head centre), `head_front = 0.1 m`,
  head sphere radius `0.4` (scaled units). The viewer rescales each participant to the
  model by `eye_height / (mean of the three initial torso heights)`.

### Gaze, for completeness

`Gaze/*_EyeTracker_data.csv` is `timestamp_ms, x_px, y_px` from D-Lab. Anomalies are
removed by rolling z-score and gaps filled by cubic spline (`GazeProcessing.mlapp`,
`Utils/data.py`), then pixels are converted to **pitch and yaw degrees**
(`GazeAngleConvert.ipynb`) giving the two-column `*_gapfilled.txt` the viewer reads.
`Data/main.cpp` composes head pose × eye angle and ray-casts against the other two
participants' head spheres to write `Session_<S>_gazeBehavior.txt` (0 neither / 1 left /
2 right).

Final 12-column annotation (`Processing/Annotation/README.md`):
`P1-3 audio | P1-3 gaze | P1-3 headshake/nod` — audio 0 silent / 1 speaking /
2 backchannel, gaze 0 neither / 1 left / 2 right, gestures binary.

---

## Is this enough to build the SMPL-X converter?

**Yes for the body — the conversion is fully determined except for two choices and one
convention that has to be decided rather than discovered.** The gaps are listed below;
none of them blocks starting.

### What the target side needs, already pinned by this repository

`SmplxMotionClip` reads a `.npz` holding `poses.npy` `(frames, 165)` — 55 joints × 3
**axis-angle** — plus `trans.npy` `(frames, 3)` in metres and `mocap_frame_rate.npy`.
Joint order is `SmplxAnimUtils.JointNames`. The source frame is **right-handed, Y-up**;
`SmplxAnimUtils.AxisAngleToUnityRotation` already applies the mirror into Unity, so the
converter writes right-handed Y-up data and touches none of that.

### Source → SMPL-X joint map (19 → 21 of 55)

| source | SMPL-X | source | SMPL-X |
| --- | --- | --- | --- |
| pelvis | `pelvis` | thorax | `spine1`/`spine2`/`spine3` † |
| lfemur / rfemur | `left_hip` / `right_hip` | head | `neck` + `head` † |
| ltibia / rtibia | `left_knee` / `right_knee` | lclavicle / rclavicle | `left_collar` / `right_collar` |
| lfoot / rfoot | `left_ankle` / `right_ankle` | lhumerus / rhumerus | `left_shoulder` / `right_shoulder` |
| ltoes / rtoes | `left_foot` / `right_foot` ‡ | lradius / rradius | `left_elbow` / `right_elbow` |
| | | lhand / rhand | `left_wrist` / `right_wrist` |

† one source joint spanning several SMPL-X joints — see the decisions below.
‡ SMPL-X's `left_foot` **is** the toe joint; `left_ankle` is the ankle.

The remaining 34 SMPL-X joints have no source: 30 finger joints, `jaw`,
`left_eye_smplhf`, `right_eye_smplhf`. For this project that is convenient rather than a
loss — jaw and eyes are driven at runtime by Oculus LipSync and by `IGazePolicy`, and
must stay zero in the clip so they do not fight it. Fingers get a fixed relaxed pose.

### Decisions the converter has to make (not missing data)

1. **How to split `thorax` across `spine1`/`spine2`/`spine3`, and `head` across
   `neck`/`head`.** Any split that composes to the right total preserves the *global*
   orientation of the head and chest, which is what the gaze work needs; the split only
   changes the silhouette of the spine and neck. An even three-way slerp of the thorax
   rotation and a ~40/60 neck/head split are reasonable starting points.
2. **Rest-pose alignment per joint.** Source rest is arms-down; SMPL-X's is near-T. For
   each joint the constant alignment rotation can be *computed*, not guessed: SMPL-X's
   own rest bone directions are measurable from the bind pose of
   `Assets/SMPLX/smplx-visemes-unity.fbx`, and the source rest directions follow from the
   hierarchy plus the arms-down reference pose. Bone-direction matching fixes two of the
   three DOF; the twist about the bone axis needs a convention and visual checking.
3. **Root translation.** Source pelvis is an absolute room position in mm and Z-up;
   SMPL-X `trans` is metres, Y-up. Whether to keep the room position, or anchor on the
   segment's first frame the way `SmplxMotionPlayer.RelativeToFirstFrame` does for
   TalkingWithHands, is the same call already made for the existing corpus.

### The converter — `Tools/convert_3people_smplx.py` (built 2026-09-07)

Run from the repo's uv environment (`uv run python Tools/convert_3people_smplx.py`,
optionally `--date`/`--session`, `--check-markers`, `--dry-run`). It reads the **stacked
CSV**, not the `Separate/` txt — the CSV carries the subject name heading each block and
the `Frame` column, both of which the txt loses — resolves the participant number through
that date's `Notes.xlsx`, and writes `F:\Data\3People-2022-SMPLX\<date>\Session_<S>_pc<N>_<Name>.npz`
with `poses`/`trans` (float32), `mocap_frame_rate`, `frame_index` and provenance fields.
All 69 participant-sessions are converted. The module docstring is the design record:
the joint map and splits, the rest alignment (trunk, head and legs carry orientation onto
SMPL-X's own rest; arms and feet are bone-direction-matched), the room-frame-plus-grounding
root convention, and the two validations — an FK self-check on every clip and the marker
fit above.

### Two routes — **angle retargeting is the chosen one** (user, 2026-09-07)

- **Angle retargeting** from the processed export — **built, see above.** Cheaper, and
  everything it needs is described in this document. Lands on a default SMPL-X body; the
  Euler-order risk is closed by the marker check.
- **Marker-based fitting** from the C3D — a MoSh-style fit of SMPL-X (shape *and* pose) to
  the 41 markers, with the VSK anthropometrics as an initialisation or a check. More work,
  but it removes the Euler ambiguity outright, recovers **per-participant body shape**
  instead of posing a default body, and is the standard way this is done. The eight
  short files and the two header traps above are the things to handle. **Deferred, not
  dropped** — it stays the better answer if fidelity ever matters more than turnaround.

The two share the joint map, the rest-pose alignment problem and the validation oracle, so
the second is an upgrade of the first rather than a rewrite. Two consequences of taking
the angle route that are worth holding onto while building it:

- **Adopt `Data/main.cpp`'s XYZ** (the standing decision) and keep the Euler order behind
  a single named constant, so switching it is one edit rather than an archaeology
  exercise. The gaze-behaviour oracle below is what tests it.
- **The VSK is still useful on this route** — its `EDIT_POSE_MEAN` gives the arms-down
  rest pose numerically, and its per-participant segment lengths give a real scale for the
  root translation instead of a guessed one.

### Real limitations, either way

- **Rigid hands and no neck articulation** are properties of the capture, and will be
  visible. The existing TalkingWithHands agents have full finger motion; these will not.
  (The marker set has one finger marker per hand, `LFIN`/`RFIN` — enough for wrist
  orientation, not for articulated fingers.)
- **Two dates have no reconstructed markers at all** (12-10-2021, 4-1-2022), so the
  marker route covers 7 dates rather than 9 — and 12-10-2021 has no processed mocap either.

### Validation available for free

- **Side-by-side against `{TPC_VIEWER}`**, which renders the same take from the same file.
- **A quantitative oracle:** `Session_<S>_gazeBehavior.txt` is a pure function of the
  three participants' head poses (`Data/main.cpp` ray-casts eye rays at head spheres).
  Re-running that same test against the converted SMPL-X heads should reproduce the
  committed annotation frame for frame. That is a strong regression test on exactly the
  quantity this project cares about, and it exists already.

---

## Caveats and open questions

- **RESOLVED, and it is a real defect: `Separate/..._PC_<N>_...` is mislabelled.** Its
  `N` is a position in an **alphabetical list of first names**, not a participant number.
  The processed CSV stacks its three blocks alphabetically and `csvtotxt-3p.py` slices
  them positionally, calling block *k* `PC_k`. **Only 5 of the 21 participant-slots across
  the seven dates are correctly labelled.**

  | date | `Separate/_PC_1,2,3_` is really | true `pc1, pc2, pc3` (Notes.xlsx) | correct |
  | --- | --- | --- | --- |
  | 12-15-2021 | Crystal, Justice, Maryum | Crystal, Maryum, Justice | 1/3 |
  | 01-28-2022 | Benjamin, Clayton, Karthik | Clayton, Karthik, Benjamin | 0/3 |
  | 02-11-2022 | Kevin, Saifur, Sana | Kevin, Sana, Saifur | 1/3 |
  | 03-04-2022 | Fatima, Joseph, May | Joseph, May, Fatima | 0/3 |
  | 03-05-2022 | Aditi, Ahmed, William | Ahmed, Aditi, William | 1/3 |
  | 03-11-2022 | An, Eric, Robert | Eric, An, Robert | 1/3 |
  | 03-12-2022 | Alahnna, Elian, Jericka | Elian, Alahnna, Jericka | 1/3 |

  **Four independent sources agree on the true numbering** and disagree with the file
  names: `Notes.xlsx`'s `PC n (Name)` headers; the C3D subject labels (`Crystal:LFHD`) in
  files named `convo<S>_pc<N>`; the `raw_data/pc<N>-<Name>/audio/` folders; and the gaze
  exports' own timestamps — `Session_1_PC_{1,2,3}_EyeTracker_data.csv` begin at
  1639591167232 / 1639591414719 / 1639594989186, matching `Notes.xlsx`'s per-PC
  `Start (UTC)` for Crystal / Maryum / Justice to within 4-12 ms. Those three machines
  started ~64 minutes apart, so the match is not coincidence.

  **So gaze, audio and the raw folders carry true PC numbers; only the processed motion
  does not.** To pair a converted clip with a voice, read the subject name from the CSV
  block header (or the C3D label) and look that participant up in `Notes.xlsx`. Never join
  on `PC_<N>` across trees. **And do not use a per-date table**: the CSV block order is
  *usually* alphabetical but not always — five sessions differ (12-15-2021 S3 is
  Justice, Crystal, Maryum; 01-28-2022 S4; 03-11-2022 S3; 03-12-2022 S2 and S3), so
  `PC_k` is block *k* of *that session's* CSV and nothing more. Verified 2026-09-07 by
  matching every `Separate/` txt's first row against its CSV block: the correspondence
  is exact in all 69 cases, and the name is only ever in the CSV. The converter resolves
  it per session; the remap that used to be printed here was wrong for those five.

  - **This reaches past our converter.** `Data/main.cpp` reads
    `Mocap/Separate/..._PC_N_...` alongside `Gaze/Convert/..._PC_N_...` to produce the
    published `Session_<S>_gazeBehavior.txt` — so on the evidence above those annotations
    pair each participant's motion with a **different** participant's gaze in 16 of the 21
    slots. The mechanism is verified from the file contents; what has *not* been done is
    re-running the pipeline to show the annotations change. **It is a strong claim about
    someone else's published data — put it to the corpus author before relying on it.**
    It also undercuts the Euler decision above, whose whole justification was agreeing
    with those annotations.
  - Spelling varies between registers (`Notes.xlsx` "Kathik"/"Ben" vs the C3D
    "Karthik"/"Benjamin"); match on the person, not the string.
- Motion and gaze lengths differ slightly (36,298 motion vs 36,300 gaze frames on the
  sample session); `align_data` trims everything to the shortest stream.
- `Gaze/Seperate/` is spelled that way on disk.
- The pelvis translation in the txt is absolute room position; the viewer subtracts only
  the first frame's Y. A SMPL-X converter has to decide the root convention explicitly
  (the corpus this project already uses, TalkingWithHands, is *Centered* — see `CLAUDE.md`).
- Nothing in the source data has SMPL-X betas or a body shape; the skeleton is a Vicon
  joint hierarchy retargeted to Mixamo by the viewer only.
