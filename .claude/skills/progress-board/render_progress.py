#!/usr/bin/env python
"""Render PROGRESS.md -> progress.html (working-memory dashboard).

Deterministic: output depends only on PROGRESS.md content (no wall-clock), so a
no-op commit produces identical bytes and the pre-commit hook adds nothing.

Usage: python render_progress.py [PROGRESS.md] [progress.html]
"""
import re
import sys
import html

TASK_RE = re.compile(r"^(?P<indent>\s*)- \[(?P<st>[ x~!])\]\s*(?P<body>.*)$")
NOTE_RE = re.compile(r"^(?P<indent>\s*)- (?!\[)(?P<text>.*)$")
DONE_RE = re.compile(
    r"_?\(done\s+(\d{4}-\d{2}-\d{2})(?:\s*[·•|,]?\s*([0-9a-f]{7,40}))?\)_?")
DATE_RE = re.compile(r"(\d{4}-\d{2}-\d{2})")

# status -> (css class, label)
STATUS = {
    " ": ("todo", "To do"),
    "~": ("wip", "In progress"),
    "x": ("done", "Done"),
    "!": ("blocked", "Blocked"),
}

RECENT_DONE = 3      # completed items shown in full; older ones compacted
RECENT_DECISIONS = 6  # decisions shown in full; older ones collapsed


class Node:
    def __init__(self, indent, status, title, desc, date, commit=None):
        self.indent = indent
        self.status = status
        self.title = title
        self.desc = desc
        self.date = date
        self.commit = commit
        self.notes = []
        self.children = []
        self.parent = None


def parse_body(body):
    """Split a task body into (title, desc, done_date, commit)."""
    date = None
    commit = None
    m = DONE_RE.search(body)
    if m:
        date = m.group(1)
        commit = m.group(2)
        body = DONE_RE.sub("", body).strip()
    body = body.strip()
    mb = re.match(r"\*\*(?P<t>.+?)\*\*(?P<rest>.*)$", body)
    if mb:
        title = mb.group("t").strip()
        desc = mb.group("rest").strip()
    else:
        # split on em dash / double hyphen
        parts = re.split(r"\s+[—–]\s+|\s+--\s+", body, maxsplit=1)
        title = parts[0].strip()
        desc = parts[1].strip() if len(parts) > 1 else ""
    desc = re.sub(r"^[—–\-:]\s*", "", desc).strip()
    return title, desc, date, commit


def parse(md):
    """Return (roots, decisions). roots = top-level task Nodes."""
    lines = md.splitlines()
    section = None
    roots = []
    decisions = []
    stack = []  # (indent, node)
    for line in lines:
        h = re.match(r"^##\s+(.*)$", line)
        if h:
            section = h.group(1).strip().lower()
            stack = []
            continue
        if section and section.startswith("decision"):
            mb = re.match(r"^\s*-\s+(.*)$", line)
            if mb:
                decisions.append(mb.group(1).strip())
            continue
        if section != "tasks":
            continue
        mt = TASK_RE.match(line)
        if mt:
            indent = len(mt.group("indent"))
            title, desc, date, commit = parse_body(mt.group("body"))
            node = Node(indent, mt.group("st"), title, desc, date, commit)
            while stack and stack[-1][0] >= indent:
                stack.pop()
            if stack:
                node.parent = stack[-1][1]
                stack[-1][1].children.append(node)
            else:
                roots.append(node)
            stack.append((indent, node))
            continue
        mn = NOTE_RE.match(line)
        if mn and stack:
            indent = len(mn.group("indent"))
            # attach to deepest task with smaller indent
            target = None
            for ind, nd in reversed(stack):
                if ind < indent:
                    target = nd
                    break
            (target or stack[-1][1]).notes.append(mn.group("text").strip())
    return roots, decisions


def dfs(roots):
    out = []
    def walk(n):
        out.append(n)
        for c in n.children:
            walk(c)
    for r in roots:
        walk(r)
    return out


# ---- minimal inline markdown (code spans, bold, links) -------------------
def md_inline(s):
    s = html.escape(s)
    s = re.sub(r"`([^`]+)`", r"<code>\1</code>", s)
    s = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", s)
    s = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', s)
    return s


def badge(status):
    cls, label = STATUS.get(status, ("todo", "?"))
    return f'<span class="badge {cls}">{label}</span>'


def meta(n):
    """Trailing date + commit hash for a completed node."""
    out = ""
    if n.date:
        out += f' <span class="date">{n.date}</span>'
    if n.commit:
        out += f' <code class="commit" title="completing commit">{html.escape(n.commit[:7])}</code>'
    return out


def render_notes(notes):
    if not notes:
        return ""
    items = "".join(f"<li>{md_inline(n)}</li>" for n in notes)
    return f'<ul class="notes">{items}</ul>'


def render_task(n, show_children=True):
    sub = ""
    if show_children and n.children:
        rows = []
        for c in n.children:
            # within an ongoing group, compact done children to one line
            if c.status == "x":
                rows.append(f'<li class="line">{badge(c.status)} <span class="t">{md_inline(c.title)}</span>{meta(c)}</li>')
            else:
                rows.append(f"<li>{render_task(c)}</li>")
        sub = f'<ul class="children">{"".join(rows)}</ul>'
    desc = f'<div class="desc">{md_inline(n.desc)}</div>' if n.desc else ""
    return (f'<div class="task">{badge(n.status)} <span class="t">{md_inline(n.title)}</span>'
            f"{desc}{render_notes(n.notes)}{sub}</div>")


def card(title, body, count=None):
    c = f' <span class="count">{count}</span>' if count is not None else ""
    return f'<section class="card"><h2>{title}{c}</h2>{body or "<p class=empty>None.</p>"}</section>'


def build_html(roots, decisions):
    all_nodes = dfs(roots)

    ongoing = [n for n in roots if n.status == "~" or any(d.status == "~" for d in dfs([n]))]
    upcoming = [n for n in roots if n.status == " " and n not in ongoing]
    blocked = [n for n in all_nodes if n.status == "!"]
    done = [n for n in all_nodes if n.status == "x"]
    done.sort(key=lambda n: (n.date or "0000-00-00"), reverse=True)

    as_of = max([d for d in (DATE_RE.search(x).group(1) for x in decisions if DATE_RE.search(x))] +
                [n.date for n in done if n.date] + ["—"])

    # ongoing
    ob = "".join(render_task(n) for n in ongoing)
    # upcoming (brief: title, desc, notes, child titles only)
    ub = ""
    for n in upcoming:
        kids = "".join(
            f'<li class="line">{badge(c.status)} <span class="t">{md_inline(c.title)}</span></li>'
            for c in n.children if c.status != "x")
        kids = f'<ul class="children">{kids}</ul>' if kids else ""
        desc = f'<div class="desc">{md_inline(n.desc)}</div>' if n.desc else ""
        ub += (f'<div class="task">{badge(n.status)} <span class="t">{md_inline(n.title)}</span>'
               f"{desc}{render_notes(n.notes)}{kids}</div>")
    # blocked
    bb = "".join(
        f'<div class="task">{badge(n.status)} <span class="t">{md_inline(n.title)}</span>'
        f'{("<div class=desc>" + md_inline(n.desc) + "</div>") if n.desc else ""}</div>'
        for n in blocked)
    # completed: recent full, rest compacted
    recent, older = done[:RECENT_DONE], done[RECENT_DONE:]
    cb = ""
    for n in recent:
        ctx = f' <span class="ctx">in {md_inline(n.parent.title)}</span>' if n.parent else ""
        desc = f'<div class="desc">{md_inline(n.desc)}</div>' if n.desc else ""
        cb += (f'<div class="task done-card">{badge(n.status)} <span class="t">{md_inline(n.title)}</span>'
               f"{meta(n)}{ctx}{desc}{render_notes(n.notes)}</div>")
    if older:
        rows = "".join(
            f'<li class="line">{md_inline(n.title)}{meta(n)}</li>' for n in older)
        cb += (f'<details class="archive"><summary>{len(older)} earlier completed</summary>'
               f'<ul class="archive-list">{rows}</ul></details>')

    # decisions: newest first
    dec_sorted = sorted(decisions, key=lambda x: (DATE_RE.search(x).group(1) if DATE_RE.search(x) else ""), reverse=True)
    db = ""
    for d in dec_sorted[:RECENT_DECISIONS]:
        db += f'<li>{md_inline(d)}</li>'
    db = f'<ul class="decisions">{db}</ul>' if db else ""
    if len(dec_sorted) > RECENT_DECISIONS:
        more = "".join(f"<li>{md_inline(d)}</li>" for d in dec_sorted[RECENT_DECISIONS:])
        db += f'<details class="archive"><summary>{len(dec_sorted) - RECENT_DECISIONS} earlier decisions</summary><ul class="decisions">{more}</ul></details>'

    body = "".join([
        card("In progress", ob, len(ongoing)),
        card("Upcoming", ub, len(upcoming)),
        card("Blocked", bb, len(blocked)) if blocked else "",
        card("Recently completed", cb, len(done)),
        card("Decisions", db, len(decisions)),
    ])

    return PAGE.replace("{{AS_OF}}", html.escape(as_of)).replace("{{BODY}}", body)


PAGE = """<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>GazeControl — Progress</title>
<style>
:root{--bg:#0f1115;--card:#171a21;--ink:#e6e8ec;--mut:#9aa0a6;--line:#262b35;
--todo:#6b7280;--wip:#f5a623;--done:#2ea043;--blocked:#e5534b;--acc:#5aa9ff}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);
font:15px/1.55 ui-sans-serif,-apple-system,Segoe UI,Roboto,sans-serif}
header{padding:24px 28px 8px;border-bottom:1px solid var(--line)}
h1{margin:0;font-size:20px;letter-spacing:.2px}
.sub{color:var(--mut);font-size:13px;margin-top:4px}
main{max-width:980px;margin:0 auto;padding:20px 16px 60px;
display:grid;grid-template-columns:1fr 1fr;gap:16px}
.card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:16px 18px}
.card:nth-child(1),.card:nth-child(4){grid-column:1/-1}
@media(max-width:760px){main{grid-template-columns:1fr}.card{grid-column:1/-1!important}}
h2{margin:0 0 12px;font-size:13px;text-transform:uppercase;letter-spacing:.08em;color:var(--mut)}
.count{display:inline-block;margin-left:6px;color:var(--acc);font-size:12px}
.task{padding:10px 0;border-top:1px solid var(--line)}
.task:first-of-type{border-top:none}
.t{font-weight:600}
.desc{color:var(--mut);font-size:13.5px;margin:3px 0 0 2px}
.children{list-style:none;margin:8px 0 0;padding:0 0 0 14px;border-left:2px solid var(--line)}
.children>li{margin:6px 0}
.line{list-style:none}
.notes{margin:6px 0 0;padding-left:20px;color:var(--mut);font-size:13px}
.notes li{margin:2px 0}
.badge{display:inline-block;font-size:11px;font-weight:700;padding:1px 7px;border-radius:20px;
vertical-align:middle;margin-right:6px}
.badge.todo{background:rgba(107,114,128,.18);color:#aab1bb}
.badge.wip{background:rgba(245,166,35,.16);color:var(--wip)}
.badge.done{background:rgba(46,160,67,.16);color:var(--done)}
.badge.blocked{background:rgba(229,83,75,.16);color:var(--blocked)}
.date{color:var(--mut);font-size:12px;font-variant-numeric:tabular-nums}
code.commit{background:#0b0d11;border:1px solid var(--line);color:var(--acc);
border-radius:5px;padding:0 5px;font-size:11.5px}
.ctx{color:var(--mut);font-size:12px;font-style:italic}
.done-card .t{font-weight:600}
code{background:#0b0d11;border:1px solid var(--line);border-radius:5px;padding:.5px 5px;font-size:12.5px}
a{color:var(--acc)}
.decisions{margin:0;padding-left:18px}.decisions li{margin:5px 0}
details.archive{margin-top:10px}
details.archive summary{cursor:pointer;color:var(--acc);font-size:13px}
.archive-list{margin:8px 0 0;padding-left:18px;color:var(--mut);font-size:13px}
.empty{color:var(--mut);font-style:italic;margin:0}
footer{max-width:980px;margin:0 auto;padding:0 16px 40px;color:var(--mut);font-size:12px}
</style></head><body>
<header><h1>GazeControl — Progress</h1>
<div class="sub">Data-driven gaze control for virtual agents in triad conversation (2 agents + user) · as of {{AS_OF}}</div>
</header>
<main>{{BODY}}</main>
<footer>Auto-generated from <code>PROGRESS.md</code> by the <code>progress-board</code> skill. Do not edit by hand.</footer>
</body></html>
"""


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "PROGRESS.md"
    out = sys.argv[2] if len(sys.argv) > 2 else "progress.html"
    with open(src, encoding="utf-8") as f:
        md = f.read()
    roots, decisions = parse(md)
    page = build_html(roots, decisions)
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write(page)
    print(f"[progress-board] wrote {out} ({len(roots)} top-level tasks, {len(decisions)} decisions)")


if __name__ == "__main__":
    main()
