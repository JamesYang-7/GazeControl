"""Convert the 3People-2022 three-party capture into SMPL-X ``.npz`` clips.

The source is the Vicon IQ local-Euler export under ``{TPC_DATA}/mocap`` (see
``Assets/Docs/3people-motion-sources.md`` for the roots, the file formats and
the traps); the target is the clip format ``SmplxMotionClip`` plays: ``poses``
(frames x 165, axis-angle, radians, right-handed Y-up), ``trans`` (frames x 3,
metres) and ``mocap_frame_rate``.

This is the **angle-retargeting route** (user's call, 2026-09-07): the export's
joint angles are decoded and re-expressed on the SMPL-X skeleton, posed on the
default body. The marker-based fit of ``{TPC_VICON}``'s C3D data is the better
answer if fidelity ever matters more than turnaround; the joint map and the
rest-pose alignment below are what the two routes share.

Input is the **stacked CSV** ``Mocap/Session_<S>_mocap_data.csv``, not the
``Separate/`` txt it was sliced into. The CSV carries what the txt loses: the
subject's name heading each block, and the ``Frame`` column, which is the C3D
frame index. The block order is *usually* alphabetical but not always (five of
the 23 sessions differ), so the name is read from the block it heads and the
participant number is looked up in that date's ``Notes.xlsx`` -- the
``Separate/..._PC_<N>_...`` number is a position, not a participant.

How the retarget works
----------------------
Both skeletons have identity rest orientations: every source segment frame is
axis-aligned with the room at the zero pose (X forward, Y left, Z up -- the VSK
places each child by a pure translation), and every SMPL-X joint frame is
axis-aligned with the model frame (X left, Y up, Z forward). What differs is
the *rest bone directions*: the source rests in an I-pose with the arms along
-Z, SMPL-X rests near a T-pose with the arms along +/-X.

So for every mapped joint ``j`` the source's global orientation ``G_src[j]`` is
carried into the SMPL-X frame by the fixed axis permutation ``C`` and followed
by a constant alignment ``A[j]`` chosen so that at the source's rest pose the
SMPL-X bone points where the source bone points::

    G_tgt[j] = C · G_src[j] · Cᵀ · A[j]        A[j] · b_tgt[j] = C · b_src[j]

Then ``G_tgt[j] · b_tgt[j] = C · G_src[j] · b_src[j]`` for every pose: the
SMPL-X bone tracks the source bone's world direction exactly. ``A`` is the
minimal rotation between the two rest directions, which fixes two of the three
degrees of freedom; the twist about the bone is the convention that has to be
checked by eye (``TWIST_DEG``; a rendered clip showed the palms on the thighs
with zero twist). Trunk, head and legs use ``A = I`` -- the source rotation is
applied on top of SMPL-X's own rest, because both skeletons rest upright facing
forward in a natural stance, and it is the head's *orientation*, not a bone
direction, that the gaze work needs preserved (see ``SOURCE_REST_BONE`` for why
the legs are not direction-matched). SMPL-X local rotations follow as
``G_tgt[parent]ᵀ · G_tgt[j]``.

The source's 19 joints map onto 21 of SMPL-X's 55: ``thorax`` is spread over
``spine1/2/3`` as three equal fractional rotations and ``head`` over
``neck``/``head`` as 40/60, both of which compose to the source total exactly
(fractional powers of one rotation commute). Jaw and eyes stay zero -- LipSync
and ``IGazePolicy`` own them at runtime -- and the fingers get the SMPL-X
package's relaxed hand preset (``--hands flat`` for zero).

Root translation keeps the **room frame** horizontally (metres, Y-up), so the
three clips of a session stay mutually consistent the way a TalkingWithHands
pair does, and is **grounded vertically** per clip: the default body's legs are
not the participant's, so the source pelvis height would sink or float the
feet. ``trans`` follows SMPL-X's convention (pelvis joint = template pelvis +
``trans``).

Despiking
---------
The Vicon fits carry glitches of three kinds, and three passes run before the
retarget, in this order (``--no-despike`` skips all of them; the marker check
always tests the export, not the repaired signal). **All three repair global
orientations, not the exported local angles**: Vicon fits each segment to its
own markers, so a glitch in one fit shows in that segment's global alone while
its children's globals stay right (their exported locals compensate). Bridging
a parent's *local* would push the error onto every descendant -- a first
version did, and gave a clean head 25 new jumps. Locals are re-derived from the
repaired globals at the end.

1. **A pelvis that keeps flipping** between two fit solutions (Maryum, 12-15-2021,
   whose T10 marker is missing: the pelvis snaps up to 136° and dwells 20-100
   frames in the wrong state, 40% of the time). The wrong state is not a
   minority, so no filter can pick it out -- but the thorax global stays smooth
   (31 jumps against the pelvis's 257). When the pelvis global jumps over
   ``JUMP_DEG`` at least ``PELVIS_REBUILD_MIN_JUMPS`` times and
   ``PELVIS_REBUILD_RATIO`` times as often as the thorax global, the pelvis is
   rebuilt for the whole take as the thorax carrying the subject's median
   pelvis-to-thorax offset. Gating on the thorax being the smoother one is what
   keeps a jumpy thorax off a good pelvis (An, 03-11-2022 S1). The position is
   rebuilt the same way only when that comes out with fewer jumps than the
   exported one. Maryum's pelvis: 257 jumps over 5° (max 136°) → 28 (max 13°).
   Triggers on 32 of the 69 takes, and never adds jumps to any joint.
2. **Excursions** on the slow joints (``EXCURSION_DEG``: pelvis, thorax, femurs,
   tibiae): frames further than that from a 2 s running median, in runs up to
   ``EXCURSION_MAX_FRAMES``, are bridged by interpolating across them. Arms and
   hands are excluded because an arm can legitimately be elsewhere for a
   second, and the head because a held glance is the very behaviour studied.
3. **Spikes** on every joint and on the pelvis translation: frames further than
   ``SPIKE_DEG`` / ``SPIKE_MM`` from a ``SPIKE_WINDOW``-frame median, in runs of
   at most ``SPIKE_MAX_FRAMES``, bridged the same way.

Runs too long to bridge are left alone but counted; the printout and the clip's
``spike_runs_left`` say how much is still in there. What remains after all
three is mostly forearm and hand marker swaps (a wrist flipping 30-60° for a
stretch), a few takes whose head markers failed outright (Clayton 01-28 S1/S5,
Elian, Eric 03-11 S2/S3: 150-180° head snaps that no bridge can fix), and on
Maryum the right leg, whose heel marker is missing too.

Legs
----
The legs are played as retargeted (``--legs real``, the default). They are
where the fits fail longest -- Maryum's right leg, heel marker missing, flips
at the femur itself for stretches at a time, with no clean child to rebuild it
from -- and ``--legs loop`` was tried on 2026-09-08 as a way round that: the
eight SMPL-X leg joints replaced by a ping-pong loop of the clip's own cleanest
leg stretch (longest run with no leg joint stepping over ``LEG_LOOP_STEP_DEG``,
capped at ``LEG_LOOP_MAX_SECONDS``), the pelvis kept real. **It was dropped the
same day: the feet drift.** The looped joints are locals under a real pelvis,
so every pelvis turn or sway pivots and slides the planted feet, and with the
legs no longer following the body the drift is exactly what the eye lands on.
The option stays for when the animation layer is dealt with (a foot-locked
version would need to solve the legs to the floor rather than loop them), and
a file made with it says so in ``legs``/``legs_loop_start``/``legs_loop_frames``.
The FK self-check skips looped joints.

Euler order
-----------
The two reference readers in ``{TPC_VIEWER}`` disagree; the standing decision
is ``Data/main.cpp``'s XYZ (``Rx(aX) · Ry(aY) · Rz(aZ)`` in the file's axes),
kept behind ``EULER_ORDER`` so switching it is one edit.

Validation
----------
Every clip is checked by forward kinematics after it is written: the SMPL-X
global orientations of the trunk and head must equal the carried source ones,
and every limb bone must point along the source bone.

``--check-markers`` is the test of the *decoding*, against ground truth: it
poses the source skeleton itself (the VSK's segment lengths, the export's
angles and offsets), fits one constant local offset per Vicon marker to the
segment it sits on, and reports the residual -- under every Euler order, so the
convention of record is measured rather than assumed. A rigidly attached marker
leaves a few millimetres; a wrong order leaves tens to hundreds. On 2026-09-07
XYZ gave 2-5 mm on five of six subjects over two dates, against 40-300 mm for
every other order. Occluded markers are stored as zeros in the C3D (C7 is
missing half the time on one subject) and are skipped. Needs the ``c3d``
package and ``{TPC_VICON}``.

``--oracle`` re-runs ``Data/main.cpp``'s eye-ray-against-head-sphere test from
the same inputs and compares it with the published
``Session_<S>_gazeBehavior.txt``. It does **not** reproduce it under any Euler
order or eye-angle convention (25% row agreement on 01-28-2022 Session 1, where
the published file codes P1 as looking at nobody 97% of the time), so the
published annotation was evidently made from other inputs; the marker check is
the oracle that works. Kept for the record, on the dates whose ``Gaze/Convert``
angle files still exist (12-15-2021 has none).

Usage::

    uv run python Tools/convert_3people_smplx.py                  # all 23 sessions
    uv run python Tools/convert_3people_smplx.py --date 12-15-2021 --session 1
    uv run python Tools/convert_3people_smplx.py --date 01-28-2022 --session 1 --check-markers --dry-run
"""

from __future__ import annotations

import argparse
import difflib
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from scipy.ndimage import median_filter
from scipy.spatial.transform import Rotation, Slerp

REPO = Path(__file__).resolve().parent.parent
DEFAULT_DATA_ROOT = Path(r"D:\3People-2022\mocap")
DEFAULT_VICON_ROOT = Path(r"D:\3People-2024\3Party-Conversation-2022")
DEFAULT_OUT_ROOT = Path(r"F:\Data\3People-2022-SMPLX")
TEMPLATE_JSON = REPO / "Assets/SMPLX/Resources/smplx_betas_to_joints_male.json"

FRAME_RATE = 60
SCHEMA = "gazecontrol.3people-smplx/1"

# --------------------------------------------------------------------------- source

# Column order of the export (Utils/data.py::motion_load), and the hierarchy the
# VSK declares. Angles are degrees, local to the parent, in the segment's own axes.
SOURCE_JOINTS = [
    "pelvis", "lfemur", "ltibia", "lfoot", "ltoes", "rfemur", "rtibia", "rfoot", "rtoes",
    "thorax", "head", "lclavicle", "lhumerus", "lradius", "lhand",
    "rclavicle", "rhumerus", "rradius", "rhand",
]
SOURCE_PARENT = {
    "pelvis": None,
    "lfemur": "pelvis", "ltibia": "lfemur", "lfoot": "ltibia", "ltoes": "lfoot",
    "rfemur": "pelvis", "rtibia": "rfemur", "rfoot": "rtibia", "rtoes": "rfoot",
    "thorax": "pelvis", "head": "thorax",
    "lclavicle": "thorax", "lhumerus": "lclavicle", "lradius": "lhumerus", "lhand": "lradius",
    "rclavicle": "thorax", "rhumerus": "rclavicle", "rradius": "rhumerus", "rhand": "rradius",
}

# The Euler convention of record. Uppercase = intrinsic in scipy, so "XYZ" is
# Rx(aX) · Ry(aY) · Rz(aZ) applied to column vectors -- Data/main.cpp's sequence.
# Demo/main.cpp (the renderer) composes "XZY" instead; see the decisions log.
EULER_ORDER = "XYZ"

# Source rest bone directions in the source's own frame (X forward, Y left, Z up),
# read off the VSK's Segment POSITION attributes, for the joints whose bone
# direction is matched: the arms hang along -Z, the foot points forward, the
# clavicle runs sideways with a small rise (0, ±Shoulder, ShoulderHeight ≈
# 0, ±165, 28 mm).
#
# Joints with no entry carry their *orientation* instead (A = I): the source
# rotation is applied on top of SMPL-X's own rest. That is right wherever both
# rests mean the same thing -- trunk and head rest upright, and the legs rest in
# a natural standing stance in both models. The legs used to be direction-matched
# too, and that crossed the knees: the Vicon model's hip centres sit 200 mm apart
# and its femurs hang parallel, SMPL-X's sit 115 mm apart and splay 10°, so
# parallel femurs from the narrow hips put the knees 123 mm apart against the
# person's 212 (Clayton, 01-28-2022 S1). The foot stays direction-matched
# because its Vicon rest -- horizontal, toes forward -- is a modelling artifact
# ~34° off the standing foot, not a stance. Leaves inherit their parent.
_CLAV = np.array([0.0, 165.5, 28.2]); _CLAV /= np.linalg.norm(_CLAV)
SOURCE_REST_BONE = {
    "lfoot": (1, 0, 0), "rfoot": (1, 0, 0),
    "lclavicle": tuple(_CLAV), "lhumerus": (0, 0, -1), "lradius": (0, 0, -1),
    "rclavicle": tuple(_CLAV * (1, -1, 1)), "rhumerus": (0, 0, -1), "rradius": (0, 0, -1),
}
INHERIT_ALIGNMENT = {"ltoes": "lfoot", "rtoes": "rfoot", "lhand": "lradius", "rhand": "rradius"}

# Extra twist about the bone axis, degrees, applied after the minimal alignment.
# The minimal rotation fixes where a bone points but not how it is rolled; this is
# the knob for the visual check. Zero until a rendered clip says otherwise.
TWIST_DEG: dict[str, float] = {}

# Source room frame (X fwd, Y left, Z up) -> SMPL-X frame (X left, Y up, Z fwd).
# Column vectors: target = C @ source, i.e. (x, y, z) -> (y, z, x). A cyclic
# permutation, so it is a proper rotation and conjugates rotations cleanly. It is
# the same (y, z, x) remap both reference readers apply.
C = np.array([[0.0, 1.0, 0.0],
              [0.0, 0.0, 1.0],
              [1.0, 0.0, 0.0]])

# --------------------------------------------------------------------------- target

SMPLX_JOINTS = [
    "pelvis", "left_hip", "right_hip", "spine1", "left_knee", "right_knee", "spine2",
    "left_ankle", "right_ankle", "spine3", "left_foot", "right_foot", "neck",
    "left_collar", "right_collar", "head", "left_shoulder", "right_shoulder",
    "left_elbow", "right_elbow", "left_wrist", "right_wrist",
    "jaw", "left_eye_smplhf", "right_eye_smplhf",
    "left_index1", "left_index2", "left_index3", "left_middle1", "left_middle2", "left_middle3",
    "left_pinky1", "left_pinky2", "left_pinky3", "left_ring1", "left_ring2", "left_ring3",
    "left_thumb1", "left_thumb2", "left_thumb3",
    "right_index1", "right_index2", "right_index3", "right_middle1", "right_middle2", "right_middle3",
    "right_pinky1", "right_pinky2", "right_pinky3", "right_ring1", "right_ring2", "right_ring3",
    "right_thumb1", "right_thumb2", "right_thumb3",
]
SMPLX_PARENT = [
    -1, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 12, 13, 14, 16, 17, 18, 19, 15, 15, 15,
    20, 25, 26, 20, 28, 29, 20, 31, 32, 20, 34, 35, 20, 37, 38,
    21, 40, 41, 21, 43, 44, 21, 46, 47, 21, 49, 50, 21, 52, 53,
]
JOINT_COUNT = len(SMPLX_JOINTS)
assert JOINT_COUNT == 55 and len(SMPLX_PARENT) == 55
T = {name: i for i, name in enumerate(SMPLX_JOINTS)}

# SMPL-X joints whose global orientation is carried from one source joint. The
# rest-direction alignment for a limb joint uses the bone to TARGET_CHILD.
SOURCE_TO_SMPLX = {
    "pelvis": "pelvis",
    "lfemur": "left_hip", "ltibia": "left_knee", "lfoot": "left_ankle", "ltoes": "left_foot",
    "rfemur": "right_hip", "rtibia": "right_knee", "rfoot": "right_ankle", "rtoes": "right_foot",
    "thorax": "spine3", "head": "head",
    "lclavicle": "left_collar", "lhumerus": "left_shoulder", "lradius": "left_elbow", "lhand": "left_wrist",
    "rclavicle": "right_collar", "rhumerus": "right_shoulder", "rradius": "right_elbow", "rhand": "right_wrist",
}
TARGET_CHILD = {
    "left_hip": "left_knee", "left_knee": "left_ankle", "left_ankle": "left_foot",
    "right_hip": "right_knee", "right_knee": "right_ankle", "right_ankle": "right_foot",
    "left_collar": "left_shoulder", "left_shoulder": "left_elbow", "left_elbow": "left_wrist",
    "right_collar": "right_shoulder", "right_shoulder": "right_elbow", "right_elbow": "right_wrist",
}

# How the one-joint source trunk spreads over SMPL-X's chain. Any split composing
# to the total preserves the global orientation above it; these only shape the
# silhouette. Fractions of the thorax rotation on spine1/2/3 and of the head
# rotation on neck/head.
SPINE_SPLIT = (1 / 3, 1 / 3, 1 / 3)
NECK_HEAD_SPLIT = (0.4, 0.6)

# Height of the SMPL-X toe joint above the sole, used to ground a clip: the lowest
# toe joint over the clip is put this far above y = 0. The TalkingWithHands
# grounded clips sit their toe joints at ~0.02 m, which is what this reproduces.
TOE_JOINT_FLOOR_HEIGHT = 0.02
GROUND_PERCENTILE = 1.0

# The SMPL-X Unity package's relaxed hand (SMPLX.cs, _handRelaxedLeft/Right),
# 15 joints x 3 axis-angle in index/middle/pinky/ring/thumb order -- the same
# order as SMPLX_JOINTS[25:40] and [40:55]. Native right-handed values: the
# package's QuatFromRodrigues applies the same mirror our loader does.
HAND_RELAXED_LEFT = np.array([
    0.11167871206998825, 0.042892176657915115, -0.41644182801246643, 0.10881132632493973, -0.06598567962646484, -0.7562199831008911,
    -0.09639296680688858, -0.09091565757989883, -0.18845929205417633, -0.1180950403213501, 0.050943851470947266, -0.529584527015686,
    -0.14369840919971466, 0.055241700261831284, -0.7048571109771729, -0.01918291673064232, -0.09233684837818146, -0.33791351318359375,
    -0.4570329785346985, -0.1962839514017105, -0.6254575252532959, -0.21465237438678741, -0.06599828600883484, -0.5068942308425903,
    -0.3697243630886078, -0.060344625264406204, -0.07949022948741913, -0.1418696939945221, -0.08585263043642044, -0.6355282664299011,
    -0.3033415973186493, -0.05788097530603409, -0.6313892006874084, -0.17612089216709137, -0.13209307193756104, -0.37335458397865295,
    0.8509643077850342, 0.27692273259162903, -0.09154807031154633, -0.4998394250869751, 0.02655647136271, 0.05288087576627731,
    0.5355591773986816, 0.04596104100346565, -0.2773580253124237,
])
HAND_RELAXED_RIGHT = HAND_RELAXED_LEFT * np.tile([1.0, -1.0, -1.0], 15)


# --------------------------------------------------------------------------- reading


@dataclass
class SourceTake:
    date: str
    session: int
    subject: str
    block: int                 # 1-based position in the CSV = the Separate/ PC_<N> number
    frame_index: np.ndarray    # (F,) int, the C3D frame number
    angles_deg: np.ndarray     # (F, 19, 3)
    pelvis_mm: np.ndarray      # (F, 3) room position
    thorax_offset_mm: np.ndarray  # (F, 3) local bone offset pelvis -> thorax
    head_offset_mm: np.ndarray    # (F, 3) local bone offset thorax -> head
    csv: Path
    local_rot: np.ndarray | None = None  # (F, 19, 3, 3) despiked local rotations, set by despike_take


def source_local_matrices(take: SourceTake) -> np.ndarray:
    """The local rotations the clip is built from: despiked if a pass ran, else decoded."""
    return take.local_rot if take.local_rot is not None else euler_to_matrix(take.angles_deg)


def read_session_csv(csv: Path, date: str, session: int) -> list[SourceTake]:
    """Slice the stacked export into its three participant blocks, by content."""
    lines = csv.read_text().splitlines()
    takes: list[SourceTake] = []
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        if not line or line[0].isdigit() or line.startswith("Frame"):
            i += 1
            continue
        subject = line
        header = [h.strip() for h in lines[i + 1].split(",") if h.strip()]
        j = i + 2
        rows = []
        while j < len(lines) and lines[j].strip() and lines[j].lstrip()[0].isdigit():
            rows.append(lines[j].rstrip(",").split(","))
            j += 1
        data = np.array(rows, dtype=float)
        if data.shape[1] != len(header) or header[0] != "Frame":
            sys.exit(f"{csv}: block '{subject}' has {data.shape[1]} columns against {len(header)} headers")
        cols = {h: data[:, k] for k, h in enumerate(header)}

        def triplet(joint: str, kind: str) -> np.ndarray:
            return np.stack([cols[f"{joint}<{kind}-{ax}>"] for ax in "XYZ"], axis=1)

        angles = np.stack([triplet(name, "a") for name in SOURCE_JOINTS], axis=1)
        takes.append(SourceTake(
            date=date, session=session, subject=subject, block=len(takes) + 1,
            frame_index=cols["Frame"].astype(np.int32), angles_deg=angles,
            pelvis_mm=triplet("pelvis", "t"), thorax_offset_mm=triplet("thorax", "t"),
            head_offset_mm=triplet("head", "t"), csv=csv,
        ))
        i = j
    if len(takes) != 3:
        sys.exit(f"{csv}: expected 3 participant blocks, found {len(takes)}")
    return takes


def read_register(notes_xlsx: Path) -> dict[int, str]:
    """``{pc number: name}`` from the ``PC n (Name)`` cells of a date's Notes.xlsx.

    Only the first session names the machines; later sessions say ``PC 1``. Some
    cells carry a remark after the name (``PC 1 (Clayton) - eyegaze is too low``)
    and some parenthesised cells are remarks, not names (``PC 2 (audio issue
    ...)``); the first name seen for each PC wins, which is the register row.
    """
    import openpyxl  # local: only this reader needs it

    ws = openpyxl.load_workbook(notes_xlsx, data_only=True, read_only=True).worksheets[0]
    register: dict[int, str] = {}
    for row in ws.iter_rows(values_only=True):
        for value in row:
            if not isinstance(value, str):
                continue
            m = re.match(r"\s*PC\s*(\d)\s*\(([A-Za-z]+)\)", value)
            if m and int(m.group(1)) not in register:
                register[int(m.group(1))] = m.group(2)
    if sorted(register) != [1, 2, 3]:
        sys.exit(f"{notes_xlsx}: could not read three 'PC n (Name)' cells, got {register}")
    return register


def match_subjects(register: dict[int, str], subjects: list[str]) -> dict[str, int]:
    """Map each CSV subject name onto a register PC number; the registers use
    nicknames and misspellings (``Ben``/``Benjamin``, ``Kathik``/``Karthik``)."""
    def score(a: str, b: str) -> float:
        a, b = a.lower(), b.lower()
        if a == b:
            return 2.0
        if a.startswith(b) or b.startswith(a):
            return 1.5
        return difflib.SequenceMatcher(None, a, b).ratio()

    mapping: dict[str, int] = {}
    free = dict(register)
    for subject in subjects:
        pc, best = max(free.items(), key=lambda kv: score(subject, kv[1]))
        if score(subject, best) < 0.7:
            sys.exit(f"cannot match CSV subject '{subject}' to the register {register}")
        mapping[subject] = pc
        del free[pc]
    return mapping


# --------------------------------------------------------------------------- maths


def euler_to_matrix(angles_deg: np.ndarray) -> np.ndarray:
    """(..., 3) file-axis Euler degrees -> (..., 3, 3) rotation, in EULER_ORDER."""
    flat = angles_deg.reshape(-1, 3)
    return Rotation.from_euler(EULER_ORDER, flat, degrees=True).as_matrix().reshape(*angles_deg.shape[:-1], 3, 3)


def rotation_between(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    """Minimal rotation taking unit vector a onto unit vector b."""
    a = a / np.linalg.norm(a)
    b = b / np.linalg.norm(b)
    axis = np.cross(a, b)
    s, c = np.linalg.norm(axis), float(np.dot(a, b))
    if s < 1e-12:
        if c > 0:
            return np.eye(3)
        # antiparallel: any perpendicular axis
        perp = np.cross(a, [1, 0, 0] if abs(a[0]) < 0.9 else [0, 1, 0])
        return Rotation.from_rotvec(np.pi * perp / np.linalg.norm(perp)).as_matrix()
    return Rotation.from_rotvec(axis / s * np.arctan2(s, c)).as_matrix()


def fractional(R: np.ndarray, fraction: float) -> np.ndarray:
    """R ** fraction for a stack (..., 3, 3) of rotations."""
    shape = R.shape[:-2]
    rv = Rotation.from_matrix(R.reshape(-1, 3, 3)).as_rotvec() * fraction
    return Rotation.from_rotvec(rv).as_matrix().reshape(*shape, 3, 3)


def to_axis_angle(R: np.ndarray) -> np.ndarray:
    shape = R.shape[:-2]
    return Rotation.from_matrix(R.reshape(-1, 3, 3)).as_rotvec().reshape(*shape, 3)


def load_template_joints() -> np.ndarray:
    with open(TEMPLATE_JSON) as f:
        return np.array(json.load(f)["template_J"], dtype=float)


def rest_alignments(template: np.ndarray) -> dict[str, np.ndarray]:
    """A[source joint]: the constant that takes the SMPL-X rest bone onto the
    source rest bone (both expressed in the SMPL-X frame), plus any TWIST_DEG."""
    A: dict[str, np.ndarray] = {}
    for src, tgt in SOURCE_TO_SMPLX.items():
        if src in SOURCE_REST_BONE:
            b_tgt = template[T[TARGET_CHILD[tgt]]] - template[T[tgt]]
            b_src = C @ np.array(SOURCE_REST_BONE[src], dtype=float)
            A[src] = rotation_between(b_tgt, b_src)
            if src in TWIST_DEG:
                axis = b_tgt / np.linalg.norm(b_tgt)
                A[src] = A[src] @ Rotation.from_rotvec(np.radians(TWIST_DEG[src]) * axis).as_matrix()
    for src, parent in INHERIT_ALIGNMENT.items():
        A[src] = A[parent]
    for src in SOURCE_TO_SMPLX:
        A.setdefault(src, np.eye(3))
    return A


def source_globals(local: np.ndarray) -> dict[str, np.ndarray]:
    """Global orientation of every source segment, (F, 3, 3), in the source frame,
    from the (F, 19, 3, 3) stack of local rotations."""
    G: dict[str, np.ndarray] = {}
    for k, name in enumerate(SOURCE_JOINTS):
        parent = SOURCE_PARENT[name]
        G[name] = local[:, k] if parent is None else G[parent] @ local[:, k]
    return G


# --------------------------------------------------------------------------- despike

# A spike is a frame whose value sits further than this from the median of the
# SPIKE_WINDOW frames around it, in a run of at most SPIKE_MAX_FRAMES consecutive
# such frames. Runs up to that length are bridged by interpolating between the
# good frames either side; longer runs are a fit failure that lasted, and are
# left alone but counted. Rotation thresholds are per source joint in degrees,
# translation in mm.
SPIKE_WINDOW = 7
SPIKE_MAX_FRAMES = 3
SPIKE_DEG = 3.0
SPIKE_MM = 10.0

# Second pass, for the slow joints only: a fit that flips into a wrong solution
# and stays there for tens of frames before flipping back (Maryum's pelvis, whose
# T10 marker is missing, snaps up to 136° and dwells 20-100 frames). A 2 s running
# median follows the majority state, so frames this far from it in runs up to
# this long are the minority state and are bridged. Arms and hands are excluded:
# an arm can legitimately be somewhere else for a second.
EXCURSION_WINDOW = 121
EXCURSION_MAX_FRAMES = 120
EXCURSION_DEG = {"pelvis": 20.0, "thorax": 20.0,
                 "lfemur": 25.0, "rfemur": 25.0, "ltibia": 25.0, "rtibia": 25.0}
EXCURSION_MM = 30.0
# The head is deliberately not in EXCURSION_DEG: a 40-60° glance at the third
# person held for a second is exactly the behaviour this corpus is for, and a
# 2 s median would call it an excursion. The head gets the spike pass only.


def _spike_runs(bad: np.ndarray, max_frames: int = SPIKE_MAX_FRAMES) -> tuple[list[tuple[int, int]], int]:
    """Runs of flagged frames as (start, end-exclusive) pairs that are short enough
    to bridge and have a good frame on both sides, plus the count of runs left."""
    runs, left = [], 0
    i, n = 0, len(bad)
    while i < n:
        if not bad[i]:
            i += 1
            continue
        j = i
        while j < n and bad[j]:
            j += 1
        if j - i <= max_frames and i > 0 and j < n:
            runs.append((i, j))
        else:
            left += 1
        i = j
    return runs, left


def despike_rotations(local: np.ndarray, window: int = SPIKE_WINDOW, threshold_deg: float = SPIKE_DEG,
                      max_frames: int = SPIKE_MAX_FRAMES) -> tuple[np.ndarray, int, int]:
    """(F, 3, 3) local rotations of one joint -> despiked copy, frames bridged, runs left."""
    q = Rotation.from_matrix(local).as_quat()
    flip = np.cumsum(np.sum(q[1:] * q[:-1], axis=1) < 0) % 2 == 1  # keep one hemisphere
    q[1:][flip] *= -1
    med = median_filter(q, size=(window, 1), mode="nearest")
    med /= np.linalg.norm(med, axis=1, keepdims=True)
    deviation = np.degrees((Rotation.from_quat(med) * Rotation.from_quat(q).inv()).magnitude())
    runs, left = _spike_runs(deviation > threshold_deg, max_frames)
    out = q.copy()
    bridged = 0
    for start, end in runs:
        a, b = q[start - 1], q[end]
        if np.dot(a, b) < 0:
            b = -b
        for k, f in enumerate(range(start, end), start=1):
            t = k / (end - start + 1)
            out[f] = Slerp([0.0, 1.0], Rotation.from_quat([a, b]))([t]).as_quat()[0]
        bridged += end - start
    return Rotation.from_quat(out).as_matrix(), bridged, left


def despike_translation(p: np.ndarray, window: int = SPIKE_WINDOW, threshold_mm: float = SPIKE_MM,
                        max_frames: int = SPIKE_MAX_FRAMES) -> tuple[np.ndarray, int, int]:
    """(F, 3) mm -> despiked copy, frames bridged, runs left."""
    med = median_filter(p, size=(window, 1), mode="nearest")
    deviation = np.linalg.norm(p - med, axis=1)
    runs, left = _spike_runs(deviation > threshold_mm, max_frames)
    out = p.copy()
    bridged = 0
    for start, end in runs:
        for k, f in enumerate(range(start, end), start=1):
            t = k / (end - start + 1)
            out[f] = (1 - t) * p[start - 1] + t * p[end]
        bridged += end - start
    return out, bridged, left


# Everything below repairs GLOBAL orientations, not the exported local angles.
# Vicon fits each segment to its own markers, so a glitch in one segment's fit
# shows in that segment's global orientation alone -- its children's globals are
# still right, and their exported locals compensate for the parent's error.
# Bridging a parent's local would therefore push the error onto every descendant
# (an earlier version despiked locals and gave a clean head 25 new jumps). The
# locals are re-derived from the repaired globals at the end.
#
# A pelvis whose fit keeps flipping into another solution (Maryum, whose T10
# marker is missing: the pelvis snaps up to 136° and dwells 20-100 frames in the
# wrong state, 40% of the time) cannot be repaired frame by frame -- the wrong
# state is not a minority. When the pelvis global jumps this much more often
# than the thorax global, the pelvis is rebuilt for the whole take as "thorax
# with the subject's median offset", in orientation and position. Gating on the
# thorax being the smoother of the two is what stops a jumpy thorax from being
# copied onto a good pelvis (An, 03-11-2022 S1).
PELVIS_REBUILD_MIN_JUMPS = 50      # pelvis global jumps over JUMP_DEG per take
PELVIS_REBUILD_RATIO = 3.0         # ... and at least this many times the thorax's
JUMP_DEG = 5.0


def _median_rotation(R: np.ndarray) -> np.ndarray:
    q = Rotation.from_matrix(R).as_quat()
    q[np.sum(q * q[0], axis=1) < 0] *= -1
    m = np.median(q, axis=0)
    return Rotation.from_quat(m / np.linalg.norm(m)).as_matrix()


def _jump_count(G: np.ndarray, threshold_deg: float = JUMP_DEG) -> int:
    r = Rotation.from_matrix(G)
    return int(np.sum(np.degrees((r[1:] * r[:-1].inv()).magnitude()) > threshold_deg))


def rebuild_pelvis(take: SourceTake, G: dict[str, np.ndarray]) -> bool:
    """Replace the pelvis global by the thorax global carrying the subject's
    median pelvis-to-thorax offset, when the pelvis is the jumpy one of the two."""
    pelvis_jumps, thorax_jumps = _jump_count(G["pelvis"]), _jump_count(G["thorax"])
    if pelvis_jumps < PELVIS_REBUILD_MIN_JUMPS or pelvis_jumps < PELVIS_REBUILD_RATIO * max(thorax_jumps, 1):
        return False
    thorax_local = np.swapaxes(G["pelvis"], 1, 2) @ G["thorax"]
    L0 = _median_rotation(thorax_local)
    deviation = np.degrees(Rotation.from_matrix(np.swapaxes(thorax_local, 1, 2) @ L0).magnitude())
    good = deviation <= np.percentile(deviation, 50)
    offset0 = np.median(take.thorax_offset_mm[good], axis=0)
    thorax_world = take.pelvis_mm + np.einsum("fij,fj->fi", G["pelvis"], take.thorax_offset_mm)
    G["pelvis"] = G["thorax"] @ L0.T
    # The position is rebuilt the same way only when that comes out smoother: the
    # export's thorax offset is not marker truth either, and on some takes the
    # rebuilt position jumped where the exported one had not (Eric, 03-11 S2).
    rebuilt = thorax_world - np.einsum("fij,j->fi", G["pelvis"], offset0)

    def translation_jumps(p: np.ndarray) -> int:
        return int(np.sum(np.linalg.norm(np.diff(p, axis=0), axis=1) > 15.0))

    if translation_jumps(rebuilt) < translation_jumps(take.pelvis_mm):
        take.pelvis_mm = rebuilt
    return True


def despike_take(take: SourceTake) -> dict:
    """Repair fit glitches in every segment's global orientation and in the
    pelvis translation, then re-derive the local rotations the clip is built
    from (``take.local_rot``, ``take.pelvis_mm``). The exported angles are kept
    for the marker check, which tests the export."""
    G = source_globals(euler_to_matrix(take.angles_deg))
    report: dict[str, tuple[int, int]] = {}
    if rebuild_pelvis(take, G):
        report["pelvis_rebuilt"] = (len(take.frame_index), 0)

    def note(name: str, bridged: int, left: int) -> None:
        if bridged or left:
            b, l = report.get(name, (0, 0))
            report[name] = (b + bridged, l + left)

    # Excursions first (the majority state is the reference), then the glitches.
    for name in SOURCE_JOINTS:
        if name in EXCURSION_DEG:
            G[name], bridged, left = despike_rotations(
                G[name], EXCURSION_WINDOW, EXCURSION_DEG[name], EXCURSION_MAX_FRAMES)
            note(name, bridged, left)
    take.pelvis_mm, bridged, left = despike_translation(
        take.pelvis_mm, EXCURSION_WINDOW, EXCURSION_MM, EXCURSION_MAX_FRAMES)
    note("pelvis_t", bridged, left)
    for name in SOURCE_JOINTS:
        G[name], bridged, left = despike_rotations(G[name])
        note(name, bridged, left)
    take.pelvis_mm, bridged, left = despike_translation(take.pelvis_mm)
    note("pelvis_t", bridged, left)

    local = np.zeros((len(take.frame_index), len(SOURCE_JOINTS), 3, 3))
    for k, name in enumerate(SOURCE_JOINTS):
        parent = SOURCE_PARENT[name]
        local[:, k] = G[name] if parent is None else np.swapaxes(G[parent], 1, 2) @ G[name]
    take.local_rot = local
    return report


# --------------------------------------------------------------------------- legs

# Standing participants barely move their legs, and the legs are where the fits
# fail longest (a missing heel marker flips a whole leg for stretches at a time),
# so a clip's legs are replaced by a loop of its own cleanest leg stretch: the
# longest run in which no leg joint steps more than LEG_LOOP_STEP_DEG between
# frames, capped at LEG_LOOP_MAX_SECONDS, played forward-backward-forward so the
# loop is continuous. The pelvis stays real (the upper body hangs from it), so the
# feet slide by the pelvis sway, about a centimetre. Runs shorter than
# LEG_LOOP_MIN_SECONDS are not worth looping and the real legs are kept.
LEG_JOINTS = ("left_hip", "right_hip", "left_knee", "right_knee",
              "left_ankle", "right_ankle", "left_foot", "right_foot")
LEG_LOOP_STEP_DEG = 3.0
LEG_LOOP_MIN_SECONDS = 5.0
LEG_LOOP_MAX_SECONDS = 20.0


def pingpong_index(frames: int, length: int) -> np.ndarray:
    """Frame -> index into a window of `length`, forward then backward, endpoints not doubled."""
    period = 2 * length - 2
    idx = np.arange(frames) % period
    return np.where(idx < length, idx, period - idx)


def loop_legs(poses: np.ndarray) -> tuple[np.ndarray, tuple[int, int] | None]:
    """Replace the leg joints of `poses` (F, 165) by a ping-pong loop of the clip's
    cleanest leg stretch. Returns (poses, (start, length)) or (poses, None) if no
    stretch is long enough."""
    F = len(poses)
    columns = [slice(3 * T[j], 3 * T[j] + 3) for j in LEG_JOINTS]
    bad = np.zeros(F - 1, dtype=bool)
    for cols in columns:
        r = Rotation.from_rotvec(poses[:, cols])
        bad |= np.degrees((r[1:] * r[:-1].inv()).magnitude()) > LEG_LOOP_STEP_DEG
    best, start, i = 0, 0, 0
    while i < F - 1:
        if bad[i]:
            i += 1
            continue
        j = i
        while j < F - 1 and not bad[j]:
            j += 1
        if j - i + 1 > best:
            best, start = j - i + 1, i
        i = j
    if best < LEG_LOOP_MIN_SECONDS * FRAME_RATE:
        return poses, None
    length = min(best, int(LEG_LOOP_MAX_SECONDS * FRAME_RATE))
    idx = start + pingpong_index(F, length)
    out = poses.copy()
    for cols in columns:
        out[:, cols] = poses[idx, cols]
    return out, (start, length)


def retarget(take: SourceTake, template: np.ndarray, hands: str, legs: str = "loop") -> tuple[np.ndarray, np.ndarray, dict]:
    """Return (poses (F,165), trans (F,3), report) for one participant."""
    F = len(take.frame_index)
    A = rest_alignments(template)
    G_src = source_globals(source_local_matrices(take))

    # Target-frame global orientation of every mapped SMPL-X joint.
    G_tgt = np.zeros((F, JOINT_COUNT, 3, 3))
    for src, tgt in SOURCE_TO_SMPLX.items():
        G_tgt[:, T[tgt]] = C @ G_src[src] @ C.T @ A[src]

    local = np.tile(np.eye(3), (F, JOINT_COUNT, 1, 1))

    def local_of(tgt: str, parent_tgt: str) -> np.ndarray:
        return np.swapaxes(G_tgt[:, T[parent_tgt]], 1, 2) @ G_tgt[:, T[tgt]]

    local[:, T["pelvis"]] = G_tgt[:, T["pelvis"]]
    for side in ("left", "right"):
        local[:, T[f"{side}_hip"]] = local_of(f"{side}_hip", "pelvis")
        local[:, T[f"{side}_knee"]] = local_of(f"{side}_knee", f"{side}_hip")
        local[:, T[f"{side}_ankle"]] = local_of(f"{side}_ankle", f"{side}_knee")
        local[:, T[f"{side}_foot"]] = local_of(f"{side}_foot", f"{side}_ankle")
        local[:, T[f"{side}_collar"]] = local_of(f"{side}_collar", "spine3")
        local[:, T[f"{side}_shoulder"]] = local_of(f"{side}_shoulder", f"{side}_collar")
        local[:, T[f"{side}_elbow"]] = local_of(f"{side}_elbow", f"{side}_shoulder")
        local[:, T[f"{side}_wrist"]] = local_of(f"{side}_wrist", f"{side}_elbow")

    thorax_local = local_of("spine3", "pelvis")
    for name, fraction in zip(("spine1", "spine2", "spine3"), SPINE_SPLIT):
        local[:, T[name]] = fractional(thorax_local, fraction)
    head_local = local_of("head", "spine3")
    for name, fraction in zip(("neck", "head"), NECK_HEAD_SPLIT):
        local[:, T[name]] = fractional(head_local, fraction)

    poses = to_axis_angle(local).reshape(F, JOINT_COUNT * 3)
    if hands == "relaxed":
        poses[:, 25 * 3:40 * 3] = HAND_RELAXED_LEFT
        poses[:, 40 * 3:55 * 3] = HAND_RELAXED_RIGHT
    leg_window = None
    if legs == "loop":
        poses, leg_window = loop_legs(poses)

    # Root: room position carried into the SMPL-X frame, metres, then grounded
    # (after the legs are final, since the feet are what is grounded).
    pelvis_world = (take.pelvis_mm / 1000.0) @ C.T
    trans = pelvis_world - template[0]
    positions = forward_kinematics(poses, trans, template)
    toe_height = np.minimum(positions[:, T["left_foot"], 1], positions[:, T["right_foot"], 1])
    ground_offset = TOE_JOINT_FLOOR_HEIGHT - np.percentile(toe_height, GROUND_PERCENTILE)
    trans[:, 1] += ground_offset

    report = {
        "ground_offset_m": float(ground_offset),
        "source_pelvis_height_m": float(take.pelvis_mm[:, 2].mean() / 1000.0),
        "leg_window": leg_window,
    }
    return poses, trans, report


def forward_kinematics(poses: np.ndarray, trans: np.ndarray, template: np.ndarray) -> np.ndarray:
    """(F, 55, 3) world joint positions of the default body under the clip."""
    F = len(poses)
    local = Rotation.from_rotvec(poses.reshape(-1, 3)).as_matrix().reshape(F, JOINT_COUNT, 3, 3)
    G = np.zeros_like(local)
    P = np.zeros((F, JOINT_COUNT, 3))
    G[:, 0] = local[:, 0]
    P[:, 0] = template[0] + trans
    for j in range(1, JOINT_COUNT):
        p = SMPLX_PARENT[j]
        G[:, j] = G[:, p] @ local[:, j]
        P[:, j] = P[:, p] + np.einsum("fij,j->fi", G[:, p], template[j] - template[p])
    return P


def global_orientations(poses: np.ndarray) -> np.ndarray:
    F = len(poses)
    local = Rotation.from_rotvec(poses.reshape(-1, 3)).as_matrix().reshape(F, JOINT_COUNT, 3, 3)
    G = np.zeros_like(local)
    G[:, 0] = local[:, 0]
    for j in range(1, JOINT_COUNT):
        G[:, j] = G[:, SMPLX_PARENT[j]] @ local[:, j]
    return G


# --------------------------------------------------------------------------- checks


def self_check(take: SourceTake, poses: np.ndarray, template: np.ndarray, stride: int = 7,
               skip: tuple[str, ...] = ()) -> float:
    """Re-derive the clip's global orientations by FK and test the retarget
    contract: an orientation-carried joint (A = I) has the carried source
    orientation exactly, a direction-matched bone points along the source bone.
    Joints in `skip` (looped legs) are not the source's any more and are not
    tested. Returns the worst angular error, degrees."""
    frames = np.arange(0, len(poses), stride)
    G_tgt = global_orientations(poses[frames])
    G_src = source_globals(source_local_matrices(take)[frames])
    A = rest_alignments(template)
    worst = 0.0
    for src, tgt in SOURCE_TO_SMPLX.items():
        if tgt in skip or not np.allclose(A[src], np.eye(3)):
            continue
        carried = C @ G_src[src] @ C.T
        delta = np.swapaxes(carried, 1, 2) @ G_tgt[:, T[tgt]]
        worst = max(worst, float(np.degrees(np.linalg.norm(to_axis_angle(delta), axis=1)).max()))
    for src, tgt in SOURCE_TO_SMPLX.items():
        if src not in SOURCE_REST_BONE or tgt in skip:
            continue
        b_tgt = template[T[TARGET_CHILD[tgt]]] - template[T[tgt]]
        b_tgt /= np.linalg.norm(b_tgt)
        want = np.einsum("fij,j->fi", C @ G_src[src], np.array(SOURCE_REST_BONE[src], dtype=float))
        have = np.einsum("fij,j->fi", G_tgt[:, T[tgt]], b_tgt)
        ang = np.degrees(np.arccos(np.clip(np.sum(want * have, axis=1), -1, 1)))
        worst = max(worst, float(ang.max()))
    return worst


def gaze_oracle(takes_by_block: list[SourceTake], gaze_dir: Path, published: Path) -> tuple[int, int]:
    """Re-run Data/main.cpp's test in its own GL frame and compare with the
    published Session_<S>_gazeBehavior.txt. Returns (agreeing frames, frames)."""
    eye_height, eye_front, head_front, sphere = 1.667, 0.198, 0.1, 0.4  # noqa: F841 (eye_height unused there too)
    n = min(len(t.frame_index) for t in takes_by_block)
    angles = []
    for k in range(3):
        path = gaze_dir / f"Session_{takes_by_block[k].session}_PC_{k + 1}_EyeTracker_data_gapfilled.txt"
        a = np.loadtxt(path)
        n = min(n, len(a))
        angles.append(a)
    P = C  # the (y, z, x) remap: GL_x <- file_y, GL_y <- file_z, GL_z <- file_x

    pupils, ends, spheres = [], [], []
    for k, take in enumerate(takes_by_block):
        # Rotations in GL axes are the file rotations conjugated by the remap.
        R_hip = P @ euler_to_matrix(take.angles_deg[:n, 0]) @ P.T
        R_spine = P @ euler_to_matrix(take.angles_deg[:n, 9]) @ P.T
        R_head = P @ euler_to_matrix(take.angles_deg[:n, 10]) @ P.T
        hip = (take.pelvis_mm[:n] / 1000.0) @ P.T
        spine_off = (take.thorax_offset_mm[:n] / 1000.0) @ P.T
        head_off = (take.head_offset_mm[:n] / 1000.0) @ P.T
        spine_pos = hip + np.einsum("fij,fj->fi", R_hip, spine_off)
        head_pos = spine_pos + np.einsum("fij,fj->fi", R_hip @ R_spine, head_off)
        R_head_world = R_hip @ R_spine @ R_head
        pupil = head_pos + np.einsum("fij,j->fi", R_head_world, [0, 0, eye_front])
        yaw, pitch = angles[k][:n, 0], angles[k][:n, 1]
        eye = (Rotation.from_euler("x", -pitch[:, None], degrees=True)
               * Rotation.from_euler("y", -yaw[:, None], degrees=True)).as_matrix()
        end = pupil + np.einsum("fij,fj->fi", R_head_world, np.einsum("fij,j->fi", eye, [0, 0, 1.0]))
        centre = head_pos + np.einsum("fij,j->fi", R_hip @ R_spine, [0, 0, head_front])
        pupils.append(pupil); ends.append(end); spheres.append(centre)

    predicted = np.zeros((n, 3), dtype=int)
    for k in range(3):
        U = ends[k] - pupils[k]
        U /= np.linalg.norm(U, axis=1, keepdims=True)
        for other, code in (((k + 1) % 3, 1), ((k + 2) % 3, 2)):
            Q = pupils[k] - spheres[other]
            b = 2 * np.sum(U * Q, axis=1)
            c = np.sum(Q * Q, axis=1) - sphere * sphere
            hit = b * b - 4 * c >= 0
            predicted[hit, k] = code  # code 2 overwrites 1, as in the C++
    truth = np.loadtxt(published, skiprows=1, dtype=int)[:n]
    return int(np.sum(np.all(predicted == truth, axis=1))), n


# Which source segment each Plug-in Gait marker rides on, for --check-markers.
MARKER_SEGMENTS = {
    "head": ["LFHD", "RFHD", "LBHD", "RBHD"], "pelvis": ["LFWT", "RFWT", "LBWT", "RBWT"],
    "thorax": ["C7", "T10", "CLAV", "STRN", "RBAC"],
    "lclavicle": ["LSHO"], "lhumerus": ["LUPA", "LELB"], "lradius": ["LFRM", "LWRA", "LWRB"], "lhand": ["LFIN"],
    "rclavicle": ["RSHO"], "rhumerus": ["RUPA", "RELB"], "rradius": ["RFRM", "RWRA", "RWRB"], "rhand": ["RFIN"],
    "lfemur": ["LTHI", "LKNE"], "ltibia": ["LSHN", "LANK"], "lfoot": ["LHEE", "LTOE", "LMT5"],
    "rfemur": ["RTHI", "RKNE"], "rtibia": ["RSHN", "RANK"], "rfoot": ["RHEE", "RTOE", "RMT5"],
}
EULER_ORDERS = ("XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX")


def vicon_session_dir(vicon_root: Path, date: str) -> Path:
    """``{TPC_VICON}`` names its dates unpadded (``1-28-2022`` for ``01-28-2022``)."""
    month, day, year = date.split("-")
    return vicon_root / f"{int(month)}-{int(day)}-{year}" / "Session 1"


def read_vsk_lengths(vsk: Path) -> dict[str, float]:
    return {k: float(v) for k, v in re.findall(r'<Parameter NAME="(\w+)" VALUE="([^"]+)"', vsk.read_text())}


def source_segment_origins(take: SourceTake, L: dict[str, float], idx: np.ndarray, order: str
                           ) -> tuple[dict[str, np.ndarray], dict[str, np.ndarray]]:
    """Pose the source skeleton itself: global rotations and origins (mm) of every
    segment at the sampled frames, from the VSK's segment offsets."""
    global EULER_ORDER
    saved, EULER_ORDER = EULER_ORDER, order
    try:
        G = source_globals(euler_to_matrix(take.angles_deg[idx]))  # as exported, not despiked
    finally:
        EULER_ORDER = saved
    P = {"pelvis": take.pelvis_mm[idx]}

    def child(name: str, parent: str, offset) -> None:
        offset = np.asarray(offset, dtype=float)
        step = np.einsum("fij,fj->fi", G[parent], offset) if offset.ndim == 2 else np.einsum("fij,j->fi", G[parent], offset)
        P[name] = P[parent] + step

    child("thorax", "pelvis", take.thorax_offset_mm[idx])
    child("head", "thorax", take.head_offset_mm[idx])
    for s, sign in (("l", 1.0), ("r", -1.0)):
        child(f"{s}clavicle", "thorax", (0, 0, L["ClavHeight"]))
        child(f"{s}humerus", f"{s}clavicle", (0, sign * L["Shoulder"], L["ShoulderHeight"]))
        child(f"{s}radius", f"{s}humerus", (0, 0, -L["UpperArm"]))
        child(f"{s}hand", f"{s}radius", (0, 0, -L["LowerArm"]))
        child(f"{s}femur", "pelvis", (0, sign * L["InterHip"], 0))
        child(f"{s}tibia", f"{s}femur", (0, 0, -L["Thigh"]))
        child(f"{s}foot", f"{s}tibia", (0, 0, -L["Shin"]))
        child(f"{s}toes", f"{s}foot", (L["Foot"], 0, 0))
    return G, P


def marker_check(take: SourceTake, pc: int, vicon_dir: Path, stride: int = 20) -> dict[str, tuple[float, dict[str, float]]]:
    """Per Euler order: (overall RMS mm, per-segment RMS mm) of a rigid-offset fit
    of every marker to its segment, over every ``stride``-th exported frame."""
    import warnings
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        import c3d  # optional dependency: `uv add c3d`

    c3d_path = vicon_dir / "c3d" / f"convo{take.session}_pc{pc}.c3d"
    wanted = take.frame_index[::stride]
    want = set(wanted.tolist())
    with open(c3d_path, "rb") as handle, warnings.catch_warnings():
        warnings.simplefilter("ignore")
        reader = c3d.Reader(handle)
        labels = [label.strip() for label in reader.point_labels]
        subject = labels[0].split(":")[0]
        labels = [label.split(":")[-1] for label in labels]
        points = {}
        for frame_no, pts, _ in reader.read_frames():
            if frame_no in want:
                points[frame_no] = pts[:, :3].copy()
    have = np.array([f in points for f in wanted])
    idx = np.arange(0, len(take.frame_index), stride)[have]
    M = np.stack([points[f] for f in wanted[have]])  # (F, markers, 3) mm; occlusions are zeros
    vsk = next(p for p in vicon_dir.glob("*.vsk") if p.stem.lower().startswith(subject[:3].lower()))
    L = read_vsk_lengths(vsk)

    result = {}
    for order in EULER_ORDERS:
        G, P = source_segment_origins(take, L, idx, order)
        per_segment, all_sq = {}, []
        for segment, markers in MARKER_SEGMENTS.items():
            sq = []
            for marker in markers:
                if marker not in labels:
                    continue
                p = M[:, labels.index(marker)]
                ok = ~np.all(p == 0, axis=1)
                if ok.sum() < 50:
                    continue
                Gs, Ps = G[segment][ok], P[segment][ok]
                offset = np.einsum("fji,fj->fi", Gs, p[ok] - Ps).mean(axis=0)  # least-squares local offset
                err = p[ok] - Ps - np.einsum("fij,j->fi", Gs, offset)
                sq.append(np.mean(np.sum(err * err, axis=1)))
            if sq:
                per_segment[segment] = float(np.sqrt(np.mean(sq)))
                all_sq += sq
        result[order] = (float(np.sqrt(np.mean(all_sq))), per_segment)
    return result


# --------------------------------------------------------------------------- driver


def find_sessions(data_root: Path, date: str | None, session: int | None) -> list[tuple[str, int, Path]]:
    found = []
    for date_dir in sorted(p for p in data_root.iterdir() if p.is_dir() and re.match(r"\d\d-\d\d-\d{4}$", p.name)):
        if date and date_dir.name != date:
            continue
        for csv in sorted(date_dir.glob("Mocap/Session_*_mocap_data.csv")):
            s = int(re.search(r"Session_(\d+)", csv.name).group(1))
            if session and s != session:
                continue
            found.append((date_dir.name, s, csv))
    return found


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--data-root", type=Path, default=DEFAULT_DATA_ROOT, help="{TPC_DATA}/mocap")
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT_ROOT, help="output root; clips go to <out>/<date>/")
    ap.add_argument("--date", help="only this capture date, e.g. 12-15-2021")
    ap.add_argument("--session", type=int, help="only this session number")
    ap.add_argument("--hands", choices=("relaxed", "flat"), default="relaxed")
    ap.add_argument("--legs", choices=("real", "loop"), default="real",
                    help="loop: replace the legs by a ping-pong loop of the clip's cleanest leg stretch "
                         "(dropped as the default on 2026-09-08: the feet drift; see the docstring)")
    ap.add_argument("--no-despike", action="store_true", help="keep the export's isolated fit glitches")
    ap.add_argument("--check-markers", action="store_true",
                    help="fit the Vicon markers to the posed source skeleton under every Euler order (needs c3d)")
    ap.add_argument("--vicon-root", type=Path, default=DEFAULT_VICON_ROOT, help="{TPC_VICON}, for --check-markers")
    ap.add_argument("--oracle", action="store_true",
                    help="also re-run the gaze-behaviour test against the published annotation")
    ap.add_argument("--dry-run", action="store_true", help="convert and check, write nothing")
    args = ap.parse_args()

    sessions = find_sessions(args.data_root, args.date, args.session)
    if not sessions:
        sys.exit(f"no sessions under {args.data_root} for date={args.date} session={args.session}")
    template = load_template_joints()
    print(f"euler order {EULER_ORDER}; {len(sessions)} session(s); hands {args.hands}; legs {args.legs}")

    registers: dict[str, dict[int, str]] = {}
    for date, session, csv in sessions:
        if date not in registers:
            registers[date] = read_register(args.data_root / date / "Notes.xlsx")
        register = registers[date]
        takes = read_session_csv(csv, date, session)
        pc_of = match_subjects(register, [t.subject for t in takes])
        print(f"\n{date} Session {session}: " + ", ".join(
            f"block {t.block} {t.subject} -> pc{pc_of[t.subject]} ({register[pc_of[t.subject]]})" for t in takes))

        for take in takes:
            spikes = {} if args.no_despike else despike_take(take)
            poses, trans, report = retarget(take, template, args.hands, args.legs)
            leg_window = report["leg_window"]
            worst = self_check(take, poses, template, skip=LEG_JOINTS if leg_window else ())
            pc = pc_of[take.subject]
            name = f"Session_{session}_pc{pc}_{take.subject}.npz"
            bridged = sum(b for b, _ in spikes.values())
            left = sum(l for _, l in spikes.values())
            print(f"  {name}: {len(poses)} frames, frames {take.frame_index[0]}-{take.frame_index[-1]}, "
                  f"pelvis {report['source_pelvis_height_m']:.3f} m, ground offset {report['ground_offset_m']:+.3f} m, "
                  f"FK self-check worst {worst:.2e} deg")
            if args.legs == "loop":
                print("    legs: " + (f"looping {leg_window[1] / FRAME_RATE:.1f} s from frame {leg_window[0]}"
                                     if leg_window else "no stretch long enough, real legs kept"))
            if spikes:
                worst_joints = sorted(spikes.items(), key=lambda kv: -(kv[1][0] + kv[1][1]))[:4]
                print(f"    despike: {bridged} frames bridged, {left} longer runs left ("
                      + ", ".join(f"{j} {b}+{l}" for j, (b, l) in worst_joints) + ")")
            if worst > 1e-3:
                sys.exit("retarget self-check failed")
            if args.check_markers:
                try:
                    fits = marker_check(take, pc, vicon_session_dir(args.vicon_root, date))
                except (FileNotFoundError, StopIteration) as e:
                    print(f"    markers: skipped ({e})")
                else:
                    rms, per_segment = fits[EULER_ORDER]
                    others = ", ".join(f"{o} {fits[o][0]:.0f}" for o in EULER_ORDERS if o != EULER_ORDER)
                    print(f"    markers: {EULER_ORDER} RMS {rms:.1f} mm (" +
                          " ".join(f"{s}:{v:.0f}" for s, v in per_segment.items()) + f"); other orders {others}")
            if args.dry_run:
                continue
            out = args.out / date / name
            out.parent.mkdir(parents=True, exist_ok=True)
            np.savez(
                out,
                poses=poses.astype(np.float32), trans=trans.astype(np.float32),
                mocap_frame_rate=np.int32(FRAME_RATE),
                betas=np.zeros(16, dtype=np.int32), gender="male",
                frame_index=take.frame_index,
                schema=SCHEMA, date=date, session=np.int32(session), pc=np.int32(pc),
                subject=take.subject, source_block=np.int32(take.block),
                source_csv=str(csv), euler_order=EULER_ORDER, hands=args.hands,
                ground_offset_m=report["ground_offset_m"],
                despiked_frames=np.int32(bridged), spike_runs_left=np.int32(left),
                legs="loop" if leg_window else "real",
                legs_loop_start=np.int32(leg_window[0] if leg_window else -1),
                legs_loop_frames=np.int32(leg_window[1] if leg_window else 0),
            )

        if args.oracle:
            gaze_dir = args.data_root / date / "Gaze" / "Convert"
            published = args.data_root / date / f"Session_{session}_gazeBehavior.txt"
            if not gaze_dir.is_dir() or not published.is_file():
                print(f"  oracle: skipped, no {gaze_dir.name}/ angle files or annotation on this date")
                continue
            agree, n = gaze_oracle(takes, gaze_dir, published)
            print(f"  oracle: {agree}/{n} frames agree with {published.name} ({100.0 * agree / n:.2f}%)")


if __name__ == "__main__":
    main()
