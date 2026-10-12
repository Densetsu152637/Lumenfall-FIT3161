from pathlib import Path

spec = {
    'campus': (92, 54, { 'c': (40, 22), 'd': (20, 34), 'a': (70, 32), 'k': (23, 40) }),
    'westForest': (50, 38, { 'o': (26, 8), 'f': (12, 16) }),
    'B02.Ground': (36, 24, { 's': (19, 14) }),
    'B01.Ground': (36, 24, {}),
    'B03.Ground': (36, 24, {}),
    'B04.Ground': (36, 24, {}),
}
out = Path('Lumenfall/Assets/Resources/NPCs')
doc_out = Path('docs/maps/layouts')
out.mkdir(parents=True, exist_ok=True)
for name, (width, height, markers) in spec.items():
    rows = [['.' for _ in range(width)] for _ in range(height)]
    for symbol, (x, y) in markers.items():
        if 0 <= x < width and 0 <= y < height:
            rows[y][x] = symbol
    content = '\n'.join(''.join(r) for r in rows) + '\n'
    (out / (name + '.npcs.txt')).write_text(content, encoding='utf-8', newline='')
    (doc_out / (name + '.npcs.txt')).write_text(content, encoding='utf-8', newline='')
