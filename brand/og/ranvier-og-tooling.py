"""Open Graph cards for the Nacara plugin and Fable documentation groups."""
from pathlib import Path
from shutil import copyfile
from oglib import Card, Fibre, CYAN, TEXT, MUTED, dust, exposure, nearest, save, wordmark

CARDS = [
    ("nacara", "NACARA PLUGIN", "Runnable documentation", "Ranvier components, signal maps and editable examples."),
    ("fable", "FABLE", "The JavaScript target", "Compile reactive models and browser views from F#."),
]

for key, section, title, line in CARDS:
    card = Card()
    card.glow(830, 490, 600, strength=1.1)
    fibre = Fibre([((-90, 590), (260, 610), (420, 490), (700, 480)),
                   ((700, 480), (960, 470), (1080, 410), (1300, 370))],
                  crest_x=780, reach=700, wmin=8, wmax=30)
    nodes = fibre.draw(card)
    first = nearest(fibre, nodes, 420)
    hops = nodes[first:first + 4]
    exposure(card, fibre, hops, 2.3)
    for node in nodes:
        fibre.node(card, node, "lit" if node in hops[:2] else "next" if node == hops[2] else "idle")
    dust(card, 30, 380, 250, seed=len(key))
    card.add(wordmark(88, 48, .62),
             f'<text x="100" y="200" class="mono" fill="{CYAN}" font-size="22" letter-spacing="3">{section}</text>',
             f'<text x="100" y="278" fill="{TEXT}" font-size="60" font-weight="600">{title}</text>',
             f'<text x="100" y="330" fill="{MUTED}" font-size="27">{line}</text>')
    path = save(card, f"ranvier-og-{key}")
    copyfile(path, Path(__file__).parents[2] / "docs" / "static" / "og" / f"{key}.png")
    print(f"Rendered {key}: 1200 x 630")
