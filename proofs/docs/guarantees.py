#!/usr/bin/env python3
"""The theorem scans of docs.sh, and the guarantees overview. Run from proofs/.

  guarantees.py comments        fails on a Theorem or Corollary with no (**md comment and bold title directly above
  guarantees.py outline DIR     fails on a page whose outline, as ranvier.js builds it, lacks a theorem of the page
  guarantees.py overview DIR    writes DIR/guarantees.html from the comments, and links it from DIR/index.html
"""
import html
import re
import sys
from pathlib import Path

DECLARATION = re.compile(r"^\s*(?:Theorem|Corollary)\s+([A-Za-z0-9_']+)")
HEADING = re.compile(r"^\(\*\* \*+ (.+?) \*\)\s*$")
TITLE = re.compile(r"^\*\*(.+)\*\*\s*$")
CHECK_MARKER = "*Check, not a guarantee.*"


def source_files():
    lines = Path("_CoqProject").read_text(encoding="utf-8").splitlines()
    return [Path(line) for line in lines if line.startswith("theories/") and line.endswith(".v")]


def page_of(path):
    return "Ranvier." + ".".join(path.relative_to("theories").with_suffix("").parts) + ".html"


def comment_above(lines, i):
    """(title or None, whether it holds the check marker) of the (**md comment ending on the line above line i."""
    end = k = i - 1
    if k < 0 or not lines[k].rstrip().endswith("*)"):
        return None, False
    while k >= 0 and not lines[k].lstrip().startswith("(*"):
        k -= 1
    if k < 0 or not lines[k].lstrip().startswith("(**md"):
        return None, False
    first = lines[k].lstrip()[len("(**md"):].strip() or lines[k + 1].strip()
    match = TITLE.match(first)
    is_check = any(line.strip() == CHECK_MARKER for line in lines[k:end + 1])
    return (match.group(1) if match else None), is_check


def theorems(path):
    """(name, heading, title or None, is_check) of each theorem in the file, in order."""
    lines = path.read_text(encoding="utf-8").split("\n")
    heading, found = "", []
    for i, line in enumerate(lines):
        match = HEADING.match(line)
        if match:
            heading = match.group(1)
        match = DECLARATION.match(line)
        if match:
            found.append((match.group(1), heading, *comment_above(lines, i)))
    return found


def check_comments():
    bad = [f"{path}: {name}" for path in source_files() for name, _, title, _ in theorems(path) if title is None]
    for entry in bad:
        print(f"docs.sh: no (**md comment with a bold title directly above {entry}", file=sys.stderr)
    return not bad


def check_outline(out):
    """Mirrors buildOutline in ranvier.js: a keyword outside <details>, then the name's span."""
    ok = True
    entry = re.compile(r'vernacular">(?:Theorem|Corollary)</span>(?:<span class="id">\s*</span>)*<span id="([^"]+)"')
    for path in source_files():
        names = [name for name, *_ in theorems(path)]
        if not names:
            continue
        text = (out / page_of(path)).read_text(encoding="utf-8")
        listed = set(entry.findall(re.sub(r"<details.*?</details>", "", text, flags=re.S)))
        missing = [name for name in names if name not in listed]
        if missing:
            what = "is empty" if not listed else f"lacks {', '.join(missing)}"
            print(f"docs.sh: {page_of(path)} holds {len(names)} theorems and its outline {what}", file=sys.stderr)
            ok = False
    return ok


def inline(text):
    escaped = html.escape(text, quote=False)
    return re.sub(r"`([^`]+)`", r"<code>\1</code>", escaped)


def overview_body():
    parts, total, falsified, checks = [], 0, 0, 0
    for path in source_files():
        found = theorems(path)
        if not found:
            continue
        page = page_of(path)
        parts.append(f'<h2><a href="{page}">{path.relative_to("theories").as_posix()}</a></h2>')
        current = None
        for name, heading, title, is_check in found:
            if heading != current:
                if current is not None:
                    parts.append("</ul>")
                parts.append(f"<h3>{inline(heading)}</h3>")
                parts.append('<ul class="guarantees">')
                current = heading
            labels = ""
            if name.endswith("_falsified"):
                labels += ' <span class="label falsified">falsified</span>'
                falsified += 1
            if is_check:
                labels += ' <span class="label check">check</span>'
                checks += 1
            total += 1
            parts.append(f'<li><a href="{page}#{name}">{inline(title.rstrip("."))}</a>{labels}'
                         f' <code class="name">{name}</code></li>')
        parts.append("</ul>")
    intro = (f'<p class="overview-intro">{total} theorems and what each states, generated from the comments in '
             f'the source. {falsified} marked <span class="label falsified">falsified</span> prove a counterexample. '
             f'{checks} marked <span class="label check">check</span> test a definition or a '
             f"single step, and are not guarantees.</p>")
    return '<h1 class="title">Guarantees</h1>\n' + intro + "\n" + "\n".join(parts) + "\n"


def write_overview(out):
    index = out / "index.html"
    text = index.read_text(encoding="utf-8")
    start = text.index('<h1 class="title">')
    end = text.index('<div class="footer">')
    page = text[:start] + overview_body() + text[end:]
    page = re.sub(r"<title>.*?</title>", "<title>Guarantees</title>", page, flags=re.S)
    (out / "guarantees.html").write_text(page, encoding="utf-8")
    heading_end = text.index("</h1>", start) + len("</h1>")
    link = ('\n<p class="overview-intro"><a href="guarantees.html">Guarantees overview</a>: '
            "every theorem and corollary, with what it guarantees.</p>")
    index.write_text(text[:heading_end] + link + text[heading_end:], encoding="utf-8")
    return True


def main():
    command = sys.argv[1]
    if command == "comments":
        ok = check_comments()
    elif command == "outline":
        ok = check_outline(Path(sys.argv[2]))
    elif command == "overview":
        ok = write_overview(Path(sys.argv[2]))
    else:
        sys.exit(f"guarantees.py: unknown command {command}")
    sys.exit(0 if ok else 1)


main()
