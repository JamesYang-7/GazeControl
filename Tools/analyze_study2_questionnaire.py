"""Questionnaire results of study 2: ratings, rankings and comments, as they stand.

Reads every participant folder under ``Recordings/Study_02`` (``P01``, ``P02``,
...; ``P00`` is the debugging label and is skipped) -- ``responses.csv``,
``comments.jsonl`` and ``session.json`` -- and reports:

* **at a glance** -- the numbers a significance claim rests on, before the tables
  they come from: the per-condition means, Friedman and Kendall's W per item, the
  Holm-corrected pairwise p and its rank-biserial effect size, what clears alpha,
  and a legend saying what each of them does and does not license;
* **completeness** -- which participants are there, whether each session finished,
  whether every block carries four ratings per version and a proper ranking, and
  how the method x serial-position counts stand (the counterbalancing closes only
  at multiples of three participants; with any other count it is reported, not
  refused);
* **ratings** -- per item and condition: mean, SD, median, and the per-participant
  means the tests run on; a Friedman test across the three conditions on the
  per-participant means, and pairwise Wilcoxon signed-rank tests with Holm
  correction; also the mean by serial position, so an order effect is visible;
* **rankings** -- mean rank per condition, how often each condition was ranked
  first/second/third, Friedman on the per-participant mean ranks, and the
  per-participant table;
* **comments** -- every free-text answer, with the version numbers the participant
  used resolved to conditions from that block's running order, since ``v1`` in a
  comment is a serial position and means a different method in every block.

The report is printed. ``--csv PREFIX`` also writes the per-participant tables as
``<PREFIX>_*.csv`` and the whole report as ``<PREFIX>_report.md`` -- markdown with
every table kept as preformatted text, since they are aligned by column width.

Descriptive and non-parametric on purpose. The mixed-model estimated marginal
means of study 1 live in ``build_study_figures.py`` and need the full balanced
design; this script is for reading the results while they are still coming in.

    uv run python Tools/analyze_study2_questionnaire.py
    uv run python Tools/analyze_study2_questionnaire.py --participants P01 P04 --csv output/study2_questionnaire
"""

from __future__ import annotations

import argparse
import contextlib
import glob
import json
import os
import re
import sys

import numpy as np
import pandas as pd
from scipy import stats

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RECORDINGS = os.path.join(REPO, "Recordings", "Study_02")
INSTRUMENT = os.path.join(REPO, "Assets", "GazeControl", "Resources", "Questionnaire.json")

# Condition names as logged, and the order they are reported in everywhere.
CONDITION_NAME = {"Proposed": "Ours", "RoleConditioned": "A", "SpeakerFollowing": "B"}
METHODS = ["Ours", "A", "B"]
LEGEND = {"Ours": "Ours (prototypes)", "A": "Baseline A (role-conditioned)",
          "B": "Baseline B (speaker-following)"}

PAIRS = [("Ours", "A"), ("Ours", "B"), ("A", "B")]
ALPHA = 0.05

RANK_ITEM = "R1"
COMMENT_ITEM = "D1"
N_POSITIONS = 3
SCALE = (1, 7)


# ---------------------------------------------------------------- data loading

def read_instrument():
    with open(INSTRUMENT, encoding="utf-8-sig") as f:
        q = json.load(f)
    items = [(it["code"], it["construct"], it["text"]) for it in q["perClipItems"]]
    return items, q["ranking"]["prompt"], q["comment"]["prompt"]


def discover_participants(root):
    labels = []
    for path in sorted(glob.glob(os.path.join(root, "P[0-9][0-9]*"))):
        label = os.path.basename(path)
        if not re.fullmatch(r"P\d{2,}", label) or label == "P00":
            continue   # P00_<stamp> debugging runs and the take folders
        if os.path.exists(os.path.join(path, "responses.csv")):
            labels.append(label)
    return labels


def load(root, participants):
    frames, comments, sessions = [], [], {}
    for p in participants:
        folder = os.path.join(root, p)
        path = os.path.join(folder, "responses.csv")
        if not os.path.exists(path):
            raise SystemExit("missing %s" % path)
        frames.append(pd.read_csv(path, encoding="utf-8-sig"))

        cpath = os.path.join(folder, "comments.jsonl")
        if os.path.exists(cpath):
            with open(cpath, encoding="utf-8-sig") as f:
                comments += [json.loads(line) for line in f if line.strip()]

        spath = os.path.join(folder, "session.json")
        if os.path.exists(spath):
            with open(spath, encoding="utf-8-sig") as f:
                sessions[p] = json.load(f)

    d = pd.concat(frames, ignore_index=True)
    d["m"] = d.condition.map(CONDITION_NAME)
    if d.m.isna().any():
        raise SystemExit("unmapped condition(s): %s"
                         % sorted(d.loc[d.m.isna(), "condition"].unique()))
    return d, pd.DataFrame(comments), sessions


# --------------------------------------------------------------- completeness

def report_completeness(d, sessions, items):
    codes = [c for c, _, _ in items]
    print("== Participants and completeness")
    rows = []
    for p, s in d.groupby("participant"):
        sess = sessions.get(p, {})
        ratings = s[s.item.isin(codes)]
        blocks = sorted(s.block.unique())
        problems = []
        for b in blocks:
            sb = s[s.block == b]
            n_rat = len(sb[sb.item.isin(codes)])
            if n_rat != len(codes) * N_POSITIONS:
                problems.append("block %d: %d ratings" % (b, n_rat))
            ranks = sorted(sb[sb.item == RANK_ITEM].response.tolist())
            if ranks != list(range(1, N_POSITIONS + 1)):
                problems.append("block %d: ranking %s" % (b, ranks))
        lo, hi = ratings.response.min(), ratings.response.max()
        if lo < SCALE[0] or hi > SCALE[1]:
            problems.append("ratings outside %s" % (SCALE,))
        rows.append({
            "participant": p,
            "schedule": sess.get("scheduleOrdinal", "?"),
            "clips": "%s/%s" % (sess.get("clipsCompleted", "?"), sess.get("clipCount", "?")),
            "completed": "yes" if sess.get("completedUtc") else "NO",
            "blocks": len(blocks),
            "ratings": len(ratings),
            "commit": (sess.get("gitCommit") or "?")[:8],
            "problems": "; ".join(problems) or "-",
        })
    print(pd.DataFrame(rows).to_string(index=False))

    counts = d[d.item == codes[0]].pivot_table(
        index="m", columns="version_position", values="response", aggfunc="count"
    ).reindex(METHODS)
    print("\nmethod x serial position (trials):")
    print(counts.to_string())
    n = d.participant.nunique()
    if counts.to_numpy().min() == counts.to_numpy().max():
        print("counterbalancing closed (%d participants)." % n)
    else:
        print("counterbalancing NOT closed at %d participants (closes at multiples of 3); "
              "the serial-position means below carry unequal weights." % n)
    print()


# ------------------------------------------------------------------- ratings

def holm(pvals):
    """Holm-Bonferroni adjusted p-values, in the input order."""
    p = np.asarray(pvals, float)
    order = np.argsort(p)
    adj = np.empty_like(p)
    running = 0.0
    for k, i in enumerate(order):
        running = max(running, (len(p) - k) * p[i])
        adj[i] = min(1.0, running)
    return adj


def rank_biserial(diff):
    """Matched-pairs rank-biserial correlation: the effect size beside a Wilcoxon p.

    +1 means every participant moved one way, 0 means the signed ranks cancel. Ties
    are dropped first, exactly as the test drops them.
    """
    d = np.asarray(diff, float)
    d = d[d != 0]
    if d.size == 0:
        return 0.0
    r = stats.rankdata(np.abs(d))
    return float((r[d > 0].sum() - r[d < 0].sum()) / r.sum())


def pairwise_stats(per_participant):
    """Wilcoxon signed-rank on each pair of methods over the per-participant values."""
    rows = []
    for a, b in PAIRS:
        # Rounded so that two means of 0.6 tie rather than differing in the 16th decimal
        # and taking two ranks; the means are multiples of 1/N_BLOCKS.
        diff = (per_participant[a] - per_participant[b]).round(9)
        n = int((diff != 0).sum())
        if n == 0:
            pval, w = 1.0, 0.0
        else:
            res = stats.wilcoxon(diff, zero_method="wilcox",
                                 method="exact" if n <= 25 else "approx")
            pval, w = float(res.pvalue), float(res.statistic)
        rows.append({"pair": "%s vs %s" % (a, b), "mean diff": diff.mean(),
                     "n nonzero": n, "W": w, "r": rank_biserial(diff), "p": pval})
    t = pd.DataFrame(rows)
    t["p (Holm)"] = holm(t["p"])
    return t


def pairwise(per_participant, label):
    t = pairwise_stats(per_participant)
    print("  pairwise Wilcoxon signed-rank (%s):" % label)
    print(t.to_string(index=False, float_format=lambda v: "%.3f" % v))


def friedman_stats(per_participant):
    """Friedman across the three methods, or None with too few participants to run it."""
    n = len(per_participant)
    if n < 3:
        return None
    cols = [per_participant[m].to_numpy() for m in METHODS]
    chi2, p = stats.friedmanchisquare(*cols)
    # Kendall's W from the Friedman statistic: W = chi2 / (n (k - 1)).
    return {"chi2": float(chi2), "p": float(p),
            "W": float(chi2) / (n * (len(METHODS) - 1)), "n": n}


def friedman(per_participant, label):
    f = friedman_stats(per_participant)
    if f is None:
        print("  Friedman (%s): skipped, %d participants" % (label, len(per_participant)))
        return
    print("  Friedman (%s): chi2(%d) = %.2f, p = %.4f, Kendall W = %.2f, n = %d"
          % (label, len(METHODS) - 1, f["chi2"], f["p"], f["W"], f["n"]))


def per_participant(s):
    """Each participant's mean per method -- the unit of analysis every test runs on."""
    return s.pivot_table(index="participant", columns="m", values="response",
                         aggfunc="mean")[METHODS]


def report_summary(d, items):
    """The numbers a significance claim rests on, before the full tables below."""
    print("== At a glance")
    n = d.participant.nunique()
    n_blocks = len(d[["participant", "block"]].drop_duplicates())
    print("   %d participants x %d blocks = %d judgements per method and item."
          % (n, n_blocks // n, n_blocks))
    print("   Every test below runs on the per-participant mean over those blocks, so")
    print("   n = %d for all of them -- that, and not the %d ratings behind each item,"
          % (n, n_blocks * N_POSITIONS))
    print("   is the number that decides the power.")

    rows, pairs, verdicts = [], [], []
    for code, construct, _ in items + [(RANK_ITEM, "Ranking (1 = best)", None)]:
        s = d[d.item == code]
        pp = per_participant(s)
        f = friedman_stats(pp)
        t = pairwise_stats(pp)
        label = "%s %s" % (code, construct)
        row = {"": label}
        row.update({m: pp[m].mean() for m in METHODS})
        row.update({"chi2(2)": f["chi2"] if f else np.nan,
                    "p": f["p"] if f else np.nan,
                    "W": f["W"] if f else np.nan})
        rows.append(row)
        p_row = {"": label}
        for _, r in t.iterrows():
            p_row[(r["pair"], "p Holm")] = r["p (Holm)"]
            p_row[(r["pair"], "r")] = r["r"]
        pairs.append(p_row)
        if f and f["p"] < ALPHA:
            verdicts.append("%s Friedman p = %.4f" % (code, f["p"]))
        for _, r in t.iterrows():
            if r["p (Holm)"] < ALPHA:
                verdicts.append("%s %s p = %.4f (Holm)" % (code, r["pair"], r["p (Holm)"]))

    p4 = lambda v: "%.4f" % v          # p-values, where the third decimal is not enough
    print("\n-- Omnibus: is there any difference among the three methods?")
    ot = pd.DataFrame(rows).set_index("").rename_axis(None)
    print(ot.to_string(float_format=lambda v: "%.2f" % v, formatters={"p": p4}))
    print("\n-- Pairwise: which two differ? (Holm-corrected within each row's three pairs)")
    pt = pd.DataFrame(pairs).set_index("").rename_axis(None)
    pt.columns = pd.MultiIndex.from_tuples(pt.columns)
    print(pt.to_string(float_format=lambda v: "%.2f" % v,
                       formatters={c: p4 for c in pt.columns if c[1] == "p Holm"}))

    print("\n-- Significant at %.2f" % ALPHA)
    for v in verdicts or ["nothing"]:
        print("   %s" % v)
    print("""
   What each number means
     n              participants (%d here), the sample size of every test -- a
                    participant's five blocks are not independent of each other.
     chi2(2), p     Friedman, the omnibus test: does any of the three methods
                    differ at all? Above %.2f, the pairwise column is exploratory.
     W              Kendall's W, the omnibus effect size, 0-1: how consistently
                    participants ordered the methods. ~.1 small, ~.3 moderate, ~.5 large.
     p Holm         the pairwise Wilcoxon p after Holm correction over that row's
                    three pairs. THIS is the number to quote for "Ours beats A",
                    not the uncorrected p in the full tables below.
     r              matched-pairs rank-biserial correlation, the pairwise effect
                    size, -1 to +1. A small p with a small r is a fragile result.
     n nonzero      (full tables) participants whose two means actually differed.
                    Ties are dropped, so a pair with few of them cannot reach a
                    small p however large the difference looks.

   Correction is applied within an item, across its three pairs -- not across the
   %d items. Reading all of them and quoting the smallest inflates the error rate.
""" % (n, ALPHA, len(items) + 1))


def report_ratings(d, items, csv_prefix):
    print("== Ratings (scale %d-%d)" % SCALE)
    all_pp = []
    for code, construct, text in items:
        s = d[d.item == code]
        print("\n-- %s  %s\n   \"%s\"" % (code, construct, text))
        desc = s.groupby("m").response.agg(["count", "mean", "std", "median"]).reindex(METHODS)
        pp = s.pivot_table(index="participant", columns="m", values="response",
                           aggfunc="mean")[METHODS]
        desc["pp mean"] = pp.mean()
        desc["pp sd"] = pp.std(ddof=1)
        desc.index = [LEGEND[m] for m in desc.index]
        print(desc.to_string(float_format=lambda v: "%.2f" % v))

        friedman(pp, "per-participant means")
        pairwise(pp, "per-participant means")

        by_pos = s.groupby(["m", "version_position"]).response.mean().unstack().reindex(METHODS)
        by_pos.loc["all"] = s.groupby("version_position").response.mean()
        print("  mean by serial position (1st/2nd/3rd version seen in a block):")
        print(by_pos.to_string(float_format=lambda v: "%.2f" % v))

        by_conv = s.groupby(["conversation", "m"]).response.mean().unstack()[METHODS]
        print("  mean by conversation:")
        print(by_conv.to_string(float_format=lambda v: "%.2f" % v))

        pp = pp.copy()
        pp["item"] = code
        all_pp.append(pp)

    pp_all = pd.concat(all_pp).reset_index()
    print("\n-- per-participant means, every item")
    print(pp_all.pivot(index="participant", columns="item", values=METHODS)
          .to_string(float_format=lambda v: "%.2f" % v))
    if csv_prefix:
        pp_all.to_csv(csv_prefix + "_rating_participant_means.csv", index=False)
    print()


# ------------------------------------------------------------------ rankings

def report_rankings(d, prompt, csv_prefix):
    print("== Ranking\n   \"%s\"  (1 = most natural)" % prompt)
    r = d[d.item == RANK_ITEM]
    pp = r.pivot_table(index="participant", columns="m", values="response",
                       aggfunc="mean")[METHODS]
    summary = pd.DataFrame(index=[LEGEND[m] for m in METHODS])
    summary["mean rank"] = [r[r.m == m].response.mean() for m in METHODS]
    summary["pp mean"] = pp.mean().to_numpy()
    summary["pp sd"] = pp.std(ddof=1).to_numpy()
    for k in range(1, N_POSITIONS + 1):
        summary["ranked %d" % k] = [int(((r.m == m) & (r.response == k)).sum()) for m in METHODS]
    summary["blocks"] = [int((r.m == m).sum()) for m in METHODS]
    print(summary.to_string(float_format=lambda v: "%.2f" % v))
    friedman(pp, "per-participant mean ranks")
    pairwise(pp, "per-participant mean ranks")

    # Bare vote count of first places, tested against the even split.
    firsts = summary["ranked 1"].to_numpy()
    if firsts.sum() >= 5:
        chi2, p = stats.chisquare(firsts)
        print("  first places %s vs even split: chi2(2) = %.2f, p = %.4f"
              % (list(map(int, firsts)), chi2, p))

    by_pos = r.groupby("version_position").response.apply(
        lambda v: (v == 1).mean())
    print("  share of blocks whose 1st-seen/2nd/3rd version was ranked first: %s"
          % ", ".join("%.2f" % v for v in by_pos.reindex(range(1, N_POSITIONS + 1))))

    print("\n-- per-participant mean rank")
    print(pp.to_string(float_format=lambda v: "%.2f" % v))
    print("\n-- rank per block (participant x conversation)")
    blocks = r.pivot_table(index=["participant", "conversation"], columns="m",
                           values="response")[METHODS]
    print(blocks.to_string())
    if csv_prefix:
        pp.reset_index().to_csv(csv_prefix + "_rank_participant_means.csv", index=False)
        blocks.reset_index().to_csv(csv_prefix + "_rank_blocks.csv", index=False)
    print()


# ------------------------------------------------------------------ comments

VERSION_REF = re.compile(r"\b(?:v|version\s*)(\d)\b", re.IGNORECASE)


def resolve_versions(text, order):
    """Append the condition behind each ``v<k>`` mentioned, from the block's running order."""
    def sub(match):
        k = int(match.group(1))
        m = order.get(k)
        return "%s[%s]" % (match.group(0), m) if m else match.group(0)
    return VERSION_REF.sub(sub, text)


def report_comments(d, comments, prompt, csv_prefix):
    print("== Comments\n   \"%s\"" % prompt)
    if comments.empty:
        print("  none\n")
        return
    order = (d[d.item != RANK_ITEM]
             .drop_duplicates(["participant", "block", "version_position"])
             .set_index(["participant", "block"]))
    rows = []
    for _, c in comments.sort_values(["participant", "block"]).iterrows():
        if c.get("skipped") or not str(c.get("text", "")).strip():
            continue
        blk = order.loc[(c.participant, c.block)]
        running = dict(zip(blk.version_position, blk.m))
        rows.append({"participant": c.participant, "block": int(c.block),
                     "conversation": c.conversation,
                     "order": " ".join("v%d=%s" % (k, running[k]) for k in sorted(running)),
                     "text": resolve_versions(c.text, running)})
    for r in rows:
        print("  %s b%d %s  (%s)\n      %s" % (r["participant"], r["block"], r["conversation"],
                                             r["order"], r["text"]))
    # A rough tally of which condition the comments name at all.
    tally = {m: 0 for m in METHODS}
    for r in rows:
        for m in set(re.findall(r"\[(\w+)\]", r["text"])):
            tally[m] += 1
    print("\n  comments mentioning each condition: %s"
          % ", ".join("%s %d" % (LEGEND[m], n) for m, n in tally.items()))
    if csv_prefix:
        pd.DataFrame(rows).to_csv(csv_prefix + "_comments.csv", index=False,
                                  encoding="utf-8-sig")
    print()


# ------------------------------------------------------------------ markdown

class Tee:
    """Passes everything printed through to the console and keeps a copy."""

    def __init__(self, stream):
        self.stream = stream
        self.parts = []

    def write(self, s):
        self.stream.write(s)
        self.parts.append(s)
        return len(s)

    def flush(self):
        self.stream.flush()

    def text(self):
        return "".join(self.parts)


def to_markdown(report):
    """The printed report as markdown.

    Headings come from the report's own ``==`` and ``--`` rules and the quoted item
    text under one becomes a caption; every other line is kept as preformatted text,
    because the tables are whitespace-aligned by ``to_string`` and a markdown
    renderer would collapse them.
    """
    out = []
    fenced = False
    caption = False
    titled = False

    def close_fence():
        if not fenced:
            return
        while out and not out[-1].strip():
            out.pop()
        out.extend(["```", ""])

    for line in report.splitlines():
        heading = "## " if line.startswith("== ") else "### " if line.startswith("-- ") else None
        if heading:
            close_fence()
            fenced = False
            out.extend([heading + line[3:].strip(), ""])
            caption = True
            continue
        if caption and line.strip().startswith('"'):
            out.extend(["*%s*" % line.strip(), ""])
            continue
        caption = False
        if not fenced:
            if not line.strip():
                continue
            if not titled:      # the "Study 2 questionnaire -- N participants" line
                out.extend(["# " + line.strip(), ""])
                titled = True
                continue
            out.append("```text")
            fenced = True
        out.append(line)
    close_fence()
    return "\n".join(out).rstrip() + "\n"


# ---------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--root", default=RECORDINGS)
    ap.add_argument("--participants", nargs="*",
                    help="labels to include (default: every P<nn> folder with responses.csv)")
    ap.add_argument("--exclude", nargs="*", default=[], help="labels to leave out")
    ap.add_argument("--csv", metavar="PREFIX",
                    help="also write the per-participant tables as <PREFIX>_*.csv "
                         "and the whole report as <PREFIX>_report.md")
    args = ap.parse_args()

    participants = args.participants or discover_participants(args.root)
    participants = [p for p in participants if p not in set(args.exclude)]
    if not participants:
        sys.exit("no participant folders with responses.csv under %s" % args.root)

    items, rank_prompt, comment_prompt = read_instrument()
    d, comments, sessions = load(args.root, participants)
    if args.csv:
        os.makedirs(os.path.dirname(os.path.abspath(args.csv)), exist_ok=True)

    tee = Tee(sys.stdout)
    with contextlib.redirect_stdout(tee):
        print("Study 2 questionnaire -- %d participants: %s\n"
              % (len(participants), ", ".join(participants)))
        report_summary(d, items)
        report_completeness(d, sessions, items)
        report_ratings(d, items, args.csv)
        report_rankings(d, rank_prompt, args.csv)
        report_comments(d, comments, comment_prompt, args.csv)

    if args.csv:
        path = args.csv + "_report.md"
        with open(path, "w", encoding="utf-8") as f:
            f.write(to_markdown(tee.text()))
        print("wrote %s" % path)


if __name__ == "__main__":
    main()
