from pathlib import Path
import json, shutil, hashlib

root = Path(__file__).resolve().parents[1]
docs = root / 'docs/maps/layouts'
runtime = root / 'Lumenfall/Assets/Resources/Maps'
backup = root / 'Temp/recovery/planning-import'
backup.mkdir(parents=True, exist_ok=True)
for name in ('campus','westForest'):
    for ext in ('.txt','.json'):
        src = runtime / (name + ext)
        if src.exists(): shutil.copy2(src, backup / src.name)
    source = docs / (name + '.txt')
    lines = source.read_text(encoding='utf-8').splitlines()
    body = []
    for line in lines[1:]:
        if not line: continue
        if line and line[-1].isdigit(): line = line[:-1]
        body.append(line)
    expected = (92,54) if name == 'campus' else (50,38)
    if len(body) != expected[1] or any(len(r) != expected[0] for r in body):
        raise SystemExit(f'{name}: invalid shape')
    (runtime / (name + '.txt')).write_text('\n'.join(body)+'\n', encoding='utf-8', newline='')
    data = json.loads((runtime / (name + '.json')).read_text(encoding='utf-8'))
    data['width'], data['height'], data['tileFile'] = expected[0], expected[1], name+'.txt'
    (runtime / (name + '.json')).write_text(json.dumps(data, indent=2)+'\n', encoding='utf-8')
    print(name, expected, hashlib.sha256(source.read_bytes()).hexdigest())
