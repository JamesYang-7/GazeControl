"""Draw the statistical figures for the user study section of the CHI paper.

Two figures, specified in ``Assets/Docs/study-figures.md``:

* ``us_ratings.pdf``       -- Figure 1, the subjective summary. Three panels:
                              ratings by construct, ranked naturalness, and the
                              distribution of ranks.
* ``us_per_participant.pdf`` -- Figure 2, the per-participant breakdown, in the
                              layout of ISMAR 2024's Figure 10(a). Two rows:
                              inclusion, which is uniform across participants,
                              and gaze naturalness, which is not.

Regenerate with::

    python Tools/build_study_figures.py

Reads ``Recordings/P05..P22/responses.csv`` -- the 18 analysed participants;
P01-P04 are pilots and are excluded (``user-study-design.md`` section 0.1). The
figures label them P01-P18, renumbering the analysed set from one for publication;
see ``DISPLAY_LABEL`` below, which is printed on every run so a bar can be traced
back to the log it came from.
Writes into the paper's ``figures/`` directory. **The paper is a separate git
repository, outside this one** (moved out of ``Research/`` on 2026-09-06 so that it
could have version control of its own; ``Research/`` is git-ignored here). Its
location is ``PAPER_DIR`` below, overridable with the ``GAZECONTROL_PAPER_DIR``
environment variable or the ``--out`` flag, so a clone on another machine needs no
edit to this file. The ``.tex`` fragments that caption these figures are
hand-written and live beside the PDFs; this script does not touch them.

Two things this script is careful about, both of them named in
``style_guidlines.md`` section 9 as errors the reference paper makes:

* **The unit of analysis.** No interval anywhere is computed over trials pooled
  across people. Figure 1's intervals are over participants (or come from a mixed
  model that carries the participant and conversation variance); Figure 2's are
  over one participant's five conversations, which is what that panel is about.
* **The estimated marginal means are the model's, not the raw cell means.** They
  are numerically equal here because the design is balanced, but the intervals are
  not, and the prose reports the model's. ``statsmodels`` is therefore a hard
  requirement rather than something to fall back from -- a silent fallback to raw
  means would put a figure in the paper that disagrees with the sentence next to it.
"""

from __future__ import annotations

import argparse
import os
import sys
import warnings

import numpy as np
import pandas as pd
from scipy import stats

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

try:
    import statsmodels.formula.api as smf
except ImportError:  # pragma: no cover - environment guard, see module docstring
    sys.exit("statsmodels is required (the estimated marginal means come from the "
             "mixed models). Install it, or the figure will not match the prose.")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RECORDINGS = os.path.join(REPO, "Recordings")
# The paper lives in its own git repository, outside this one. Set
# GAZECONTROL_PAPER_DIR to relocate it without editing this file, or pass --out.
PAPER_DIR = os.environ.get(
    "GAZECONTROL_PAPER_DIR",
    r"F:\Research\CHI_2027___Explainable_Gaze_Patterns_for_Turn_Taking")
DEFAULT_OUT = os.path.join(PAPER_DIR, "figures")

# The 18 analysed participants, by the label their files carry on disk. P01-P04 are
# pilots, excluded before any further participant ran, on a ground independent of how
# they answered.
PARTICIPANTS = ["P%02d" % i for i in range(5, 23)]

# What the paper calls them. The analysed participants are numbered from one for
# publication, because the gap at the front is an artifact of the pilot exclusion and a
# reader who sees an axis starting at P05 asks where P01 went -- a question the exclusion
# paragraph has already answered in the prose. Derived from position rather than written
# out, so the two lists cannot drift apart.
#
# The mapping is one-to-one and order-preserving, so it loses nothing: display P_k is the
# k-th entry of PARTICIPANTS. It is printed on every run, and `Assets/Docs/study-figures.md`
# records it, because anyone tracing a bar back to a log needs the disk label, not this one.
DISPLAY_LABEL = {disk: "P%02d" % (k + 1) for k, disk in enumerate(PARTICIPANTS)}

# Condition names as logged, and the order they are drawn in everywhere.
CONDITION_NAME = {"Proposed": "Ours", "RoleConditioned": "A", "SpeakerFollowing": "B"}
METHODS = ["Ours", "A", "B"]
LEGEND = {"Ours": "Ours", "A": "Baseline A", "B": "Baseline B"}
SHORT = {"Ours": "Ours", "A": "A", "B": "B"}

# Greyscale-safe: distinct in colour, in lightness, and (Figure 1) in marker.
COLOUR = {"Ours": "#111111", "A": "#6f6f6f", "B": "#bdbdbd"}
MARKER = {"Ours": "o", "A": "s", "B": "^"}
RANK_COLOUR = ["#2f2f2f", "#8c8c8c", "#d9d9d9"]

# Keys are the item codes the instrument and `responses.csv` use. They never appear in
# the paper: there is exactly one item per construct, so a code plus a number
# disambiguates nothing a reviewer needs disambiguated, and it invites the question the
# codes cannot answer -- "T2" is the second boundary item drafted, "T1" having been cut
# during design, which is our history and not the reader's. Constructs are named instead.
CONSTRUCTS = ["N1", "T2", "A1", "I1"]
# Axis labels are one word each, so all four sit on one line and the axis reads evenly.
# "Boundary gaze quality" does not fit on one line at the body size and wrapping only it
# leaves a ragged axis, so the axis carries the distinguishing word and the full construct
# names live where there is room for them: Table `tab:us_items` and the figure caption.
CONSTRUCT_LABEL = {
    "N1": "Naturalness",
    "T2": "Boundary",
    "A1": "Engagement",
    "I1": "Inclusion",
}
CONSTRUCT_TITLE = {
    "N1": "gaze naturalness",
    "T2": "boundary gaze quality",
    "A1": "mutual engagement",
    "I1": "inclusion",
}

N_BLOCKS = 5          # conversations per participant
N_POSITIONS = 3       # versions per block
SCALE = (1, 7)

PLOT_STYLE = {
    "font.size": 7.5,
    "font.family": "serif",
    "axes.linewidth": .6,
    "xtick.major.width": .6,
    "ytick.major.width": .6,
    "xtick.major.size": 2.5,
    "ytick.major.size": 2.5,
    "pdf.fonttype": 42,      # TrueType, so figure text stays selectable
}


# ---------------------------------------------------------------- data loading

def load_responses():
    """Read the 18 analysed participants and check the design closed as intended.

    Raises rather than warns on anything unexpected: a figure drawn from a
    half-loaded study is worse than no figure.
    """
    frames = []
    for participant in PARTICIPANTS:
        path = os.path.join(RECORDINGS, participant, "responses.csv")
        if not os.path.exists(path):
            raise SystemExit("missing %s" % path)
        frames.append(pd.read_csv(path, encoding="utf-8-sig"))
    d = pd.concat(frames, ignore_index=True)
    d["m"] = d.condition.map(CONDITION_NAME)
    if d.m.isna().any():
        raise SystemExit("unmapped condition(s): %s"
                         % sorted(d.loc[d.m.isna(), "condition"].unique()))

    ratings = d[d.item.isin(CONSTRUCTS)]
    per_cell = ratings.groupby(["participant", "m", "item"]).size().unique()
    if list(per_cell) != [N_BLOCKS]:
        raise SystemExit("expected %d trials per participant x method x item, got %s"
                         % (N_BLOCKS, per_cell))

    ranks = d[d.item == "R1"]
    bad = ranks.groupby(["participant", "block"]).response.apply(
        lambda s: sorted(s.tolist()) != list(range(1, N_POSITIONS + 1)))
    if bad.any():
        raise SystemExit("blocks whose ranking is not a permutation of 1..%d: %s"
                         % (N_POSITIONS, list(bad[bad].index)))

    counts = ratings[ratings.item == CONSTRUCTS[0]].pivot_table(
        index="m", columns="version_position", values="response", aggfunc="count")
    if counts.to_numpy().min() != counts.to_numpy().max():
        raise SystemExit("counterbalancing did not close: method x serial position "
                         "counts are\n%s" % counts)

    print("loaded %d participants, %d ratings, %d rankings; counterbalancing closed at "
          "%d trials per method x serial position"
          % (d.participant.nunique(), len(ratings), len(ranks), counts.to_numpy().min()))
    return d


# ------------------------------------------------------------------ statistics

def marginal_means(d):
    """Estimated marginal means with 95% CIs, per construct, from the mixed models.

    One model per construct, exactly as registered: method and serial position as
    fixed effects, crossed random intercepts for participant and conversation. The
    EMM averages over serial position, which for this balanced design means giving
    each of the three positions weight 1/3.
    """
    out = {}
    for construct in CONSTRUCTS:
        s = d[d.item == construct].copy()
        s["mm"] = pd.Categorical(s.m, categories=METHODS)
        s["pos"] = pd.Categorical(s.version_position)
        s["g"] = 1
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            fit = smf.mixedlm(
                "response ~ C(mm) + C(pos)", s, groups=s["g"],
                vc_formula={"participant": "0+C(participant)",
                            "conversation": "0+C(conversation)"},
            ).fit(reml=True, method="lbfgs")

        names = list(fit.params.index[:len(fit.fe_params)])
        cov = fit.cov_params().values[:len(names), :len(names)]
        out[construct] = {}
        for method in METHODS:
            c = np.zeros(len(names))
            c[names.index("Intercept")] = 1.0
            if method != METHODS[0]:
                c[names.index("C(mm)[T.%s]" % method)] = 1.0
            for position in range(2, N_POSITIONS + 1):
                c[names.index("C(pos)[T.%d]" % position)] = 1.0 / N_POSITIONS
            est = float(c @ fit.fe_params.values)
            se = float(np.sqrt(c @ cov @ c))
            out[construct][method] = (est, est - 1.96 * se, est + 1.96 * se)
    return out


def mean_ranks(d):
    """Per-participant mean rank per method, and its 95% CI over participants."""
    per_participant = d[d.item == "R1"].pivot_table(
        index="participant", columns="m", values="response", aggfunc="mean")[METHODS]
    summary = {}
    for method in METHODS:
        v = per_participant[method]
        half = stats.t.ppf(.975, len(v) - 1) * v.std(ddof=1) / np.sqrt(len(v))
        summary[method] = (v.mean(), v.mean() - half, v.mean() + half)
    return per_participant, summary


def rank_distribution(d):
    """How many of the blocks put each method first, second and third."""
    r = d[d.item == "R1"]
    return {m: [int(((r.m == m) & (r.response == k)).sum())
                for k in range(1, N_POSITIONS + 1)] for m in METHODS}


def per_participant(d, construct):
    """Each participant's mean per method, with the standard error over conversations.

    The whiskers are within-participant variability across the five conversations,
    which says whether someone answered consistently, and they are zero where a
    participant gave the same answer every time.

    **Standard error, not a 95% CI, and this is the one place the paper departs from
    that convention.** With five conversations the t interval carries four degrees of
    freedom, giving a mean half-width of 1.2 to 1.5 scale points; 21 of the 108 cells
    would then show an interval extending past 7 or below 1, that is, advertising
    ratings the instrument cannot produce. Sizing the axis to fit them would spend a
    fifth of the panel on impossible values and shrink the bars the panel exists to
    compare. The caption must say "standard error" wherever this is drawn.
    """
    s = d[d.item == construct]
    means = s.pivot_table(index="participant", columns="m",
                          values="response", aggfunc="mean")[METHODS]
    sds = s.pivot_table(index="participant", columns="m",
                        values="response", aggfunc=lambda v: v.std(ddof=1))[METHODS]
    return means, (sds / np.sqrt(N_BLOCKS)).fillna(0.0)


# --------------------------------------------------------------------- drawing

def draw_ratings(d, emm, rank_summary, ranks_by_place, out_path):
    """Figure 1: ratings by construct, ranked naturalness, rank distribution."""
    # Width is fixed at 	extwidth, so panel (c)'s legend cannot be given more room
    # sideways without taking it from (a) and (b). Height is free: a taller figure lets
    # that legend stack vertically, which costs almost no width, and the three panels
    # keep the proportions their content wants.
    fig, axes = plt.subplots(1, 3, figsize=(7.0, 2.65),
                             gridspec_kw={"width_ratios": [2.9, 1.0, 1.0],
                                          "wspace": 0.34})

    ax = axes[0]
    offset = {"Ours": -0.20, "A": 0.0, "B": 0.20}
    for method in METHODS:
        xs = [i + offset[method] for i in range(len(CONSTRUCTS))]
        ys = [emm[c][method][0] for c in CONSTRUCTS]
        lo = [emm[c][method][0] - emm[c][method][1] for c in CONSTRUCTS]
        hi = [emm[c][method][2] - emm[c][method][0] for c in CONSTRUCTS]
        ax.errorbar(xs, ys, yerr=[lo, hi], fmt=MARKER[method], ms=4.2,
                    color=COLOUR[method], ecolor=COLOUR[method], capsize=2.4,
                    elinewidth=.9, mew=.7,
                    mec="black" if method != "Ours" else COLOUR[method],
                    label=LEGEND[method], ls="none")
    ax.set_xticks(range(len(CONSTRUCTS)))
    ax.set_xticklabels([CONSTRUCT_LABEL[c] for c in CONSTRUCTS])
    ax.set_xlim(-0.55, len(CONSTRUCTS) - 0.45)
    ax.set_ylim(*SCALE)
    ax.set_yticks(range(SCALE[0], SCALE[1] + 1))
    ax.set_ylabel("rating (1 to 7)")
    ax.grid(axis="y", lw=.35, color="0.86")
    ax.set_axisbelow(True)
    ax.legend(frameon=False, ncol=3, loc="lower center", handletextpad=.25,
              columnspacing=1.5, borderpad=0.1)
    ax.set_title("(a) Ratings by construct", fontsize=7.5, pad=4)

    ax = axes[1]
    for i, method in enumerate(METHODS):
        est, lo, hi = rank_summary[method]
        ax.errorbar([i], [est], yerr=[[est - lo], [hi - est]], fmt=MARKER[method],
                    ms=4.2, color=COLOUR[method], ecolor=COLOUR[method], capsize=2.4,
                    elinewidth=.9, mew=.7,
                    mec="black" if method != "Ours" else COLOUR[method], ls="none")
    ax.set_xticks(range(len(METHODS)))
    ax.set_xticklabels([SHORT[m] for m in METHODS])
    ax.set_xlim(-.6, len(METHODS) - .4)
    ax.set_ylim(3.0, 1.0)                       # inverted: better reads as higher
    ax.set_yticks([1, 1.5, 2, 2.5, 3])
    ax.set_ylabel("mean rank (1 = most natural)")
    ax.grid(axis="y", lw=.35, color="0.86")
    ax.set_axisbelow(True)
    ax.set_title("(b) Ranked naturalness", fontsize=7.5, pad=4)

    ax = axes[2]
    total = sum(ranks_by_place[METHODS[0]])
    for i, method in enumerate(METHODS):
        bottom = 0
        for place, count in enumerate(ranks_by_place[method]):
            ax.bar(i, count, bottom=bottom, width=.62, color=RANK_COLOUR[place],
                   edgecolor="black", linewidth=.5,
                   label=("%d%s" % (place + 1, ["st", "nd", "rd"][place])) if i == 0 else None)
            ax.text(i, bottom + count / 2, str(count), ha="center", va="center",
                    fontsize=6.5, color="white" if place == 0 else "black")
            bottom += count
    ax.set_xticks(range(len(METHODS)))
    ax.set_xticklabels([SHORT[m] for m in METHODS])
    ax.set_xlim(-.6, len(METHODS) - .4)
    # Headroom above the tallest stack (which is always `total`) so the legend
    # sits inside the axes without landing on a bar.
    ax.set_ylim(0, total * 1.34)
    ax.set_yticks(range(0, total + 1, 30))
    ax.set_ylabel("blocks (of %d)" % total)
    ax.legend(frameon=False, ncol=1, loc="upper center", handlelength=.8,
              handletextpad=.35, labelspacing=.22, borderpad=0.1, fontsize=6.5)
    ax.set_title("(c) Rank distribution", fontsize=7.5, pad=4)

    fig.savefig(out_path, bbox_inches="tight", pad_inches=0.02)
    plt.close(fig)
    print("wrote", out_path)


def draw_per_participant(d, out_path):
    """Figure 2: per participant, inclusion above gaze naturalness.

    Participants stay in label order. Sorting by effect would tidy both rows and
    misrepresent both: (a) is about nearly every bar going the same way, (b) is
    about them not doing so.
    """
    panels = [("I1", "(a) Inclusion"), ("N1", "(b) Gaze naturalness")]
    fig, axes = plt.subplots(len(panels), 1, figsize=(7.0, 3.7),
                             gridspec_kw={"hspace": 0.42})

    width = 0.26
    overflow = 0
    for ax, (construct, title) in zip(axes, panels):
        means, half = per_participant(d, construct)
        overflow += int(((means + half).to_numpy() > SCALE[1] * 1.06).sum())
        xs = np.arange(len(means.index))
        for k, method in enumerate(METHODS):
            ax.bar(xs + (k - 1) * width, means[method], width=width,
                   yerr=half[method], color=COLOUR[method], edgecolor="black",
                   linewidth=.5, error_kw=dict(elinewidth=.7, capsize=1.6, capthick=.7),
                   label=LEGEND[method] if construct == panels[0][0] else None)
        ax.set_xticks(xs)
        ax.set_xticklabels([DISPLAY_LABEL[p] for p in means.index], fontsize=6.2)
        ax.set_xlim(-.6, len(xs) - .4)
        # Bars grow from zero, so the axis has to start there. Starting it at the
        # scale floor would clip a rating of 1 to nothing, and five cells here are
        # exactly 1 (disk P15, drawn as P11, on all three methods; and baseline B for
        # four others) -- they must read as floor responses, not as missing data.
        ax.set_ylim(0, SCALE[1] * 1.06)
        ax.set_yticks(range(SCALE[0], SCALE[1] + 1, 2))
        ax.set_ylabel("rating (1 to 7)")
        ax.grid(axis="y", lw=.35, color="0.86")
        ax.set_axisbelow(True)
        ax.set_title(title, fontsize=7.5, pad=3)

    axes[0].legend(frameon=False, ncol=3, loc="upper center",
                   bbox_to_anchor=(.5, 1.42), handletextpad=.35,
                   columnspacing=1.8, borderpad=0.1)
    if overflow:
        print("  warning: %d whisker(s) run past the top of the axis" % overflow)

    fig.savefig(out_path, bbox_inches="tight", pad_inches=0.02)
    plt.close(fig)
    print("wrote", out_path)


# ------------------------------------------------------------------------ main

def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", default=DEFAULT_OUT,
                        help="directory to write the PDFs into (default: the paper's)")
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)

    d = load_responses()
    emm = marginal_means(d)
    _, rank_summary = mean_ranks(d)
    ranks_by_place = rank_distribution(d)

    for construct in CONSTRUCTS:
        print("  %s EMM: %s" % (construct, ", ".join(
            "%s %.2f [%.2f, %.2f]" % (m, *emm[construct][m]) for m in METHODS)))
    print("  mean rank: %s" % ", ".join(
        "%s %.2f [%.2f, %.2f]" % (m, *rank_summary[m]) for m in METHODS))
    print("  rank distribution (1st/2nd/3rd): %s" % ranks_by_place)
    print("  display labels (disk -> paper): %s"
          % ", ".join("%s->%s" % (k, v) for k, v in DISPLAY_LABEL.items()))

    plt.rcParams.update(PLOT_STYLE)
    draw_ratings(d, emm, rank_summary, ranks_by_place,
                 os.path.join(args.out, "us_ratings.pdf"))
    draw_per_participant(d, os.path.join(args.out, "us_per_participant.pdf"))


if __name__ == "__main__":
    main()
