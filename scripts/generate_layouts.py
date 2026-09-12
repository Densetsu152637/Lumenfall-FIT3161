"""Generate deterministic tile layouts and human-readable maps from location-plan.md."""
from __future__ import annotations
import json, re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs" / "location-plan.md"
OUT = ROOT / "docs" / "maps"
W, H = 12, 10

def inventory():
    text = SOURCE.read_text(encoding="utf-8")
    found = {}
    # Building headings are stable location IDs even though they are not table rows.
    for ident, name in re.findall(r"## (B0[1-4]):\s*([^\n]+)", text):
        found[ident] = {"id": ident, "name": name.strip(), "kind": "building", "floor": "Ground", "access": "Chapter 1"}
    for row in text.splitlines():
        m = re.match(r"\|\s*([A-Z][0-9]{2})\s*\|\s*([^|]+)\|\s*([^|]+)\|.*\|\s*(Chapter 1|Optional|Reserved)\s*\|", row)
        if m:
            ident, name, placement, access = m.groups()
            floor = "Upper" if re.search(r"\bUpper\b|upper floor|upper landing", placement, re.I) else "Ground"
            kind = "outdoor" if ident[0] in "FAC" else "room"
            found[ident] = {"id": ident, "name": name.strip(), "kind": kind, "floor": floor, "access": access}
    return [found[k] for k in sorted(found)]

def tiles(area):
    # # wall, . walkable, + door/portal. Reserved areas remain represented and gated.
    grid = [["." for _ in range(W)] for _ in range(H)]
    for y in range(H):
        for x in range(W):
            if x in (0, W-1) or y in (0, H-1): grid[y][x] = "#"
    # A door in every layout guarantees an explicit entry point; portals identify transitions.
    grid[H//2][0] = "+"
    return ["".join(r) for r in grid]

def build():
    areas = []
    for a in inventory():
        entry = dict(a)
        entry.update({"schema": "lumenfall.tile-layout/1", "width": W, "height": H,
                      "origin": {"x": 0, "y": 0}, "tiles": tiles(a),
                      "legend": {"#": "wall", ".": "walkable", "+": "door or portal"},
                      "regions": [{"id": a["id"] + ".main", "name": a["name"], "rect": {"x": 1, "y": 1, "width": W-2, "height": H-2}, "walkable": True}],
                      "spawn": {"x": 1, "y": H//2},
                      "doors": [{"id": a["id"] + ".entry", "cell": {"x": 0, "y": H//2}, "state": "open" if a["access"] == "Chapter 1" else "locked", "requires": None if a["access"] == "Chapter 1" else "content_unlock"}],
                      "portals": [], "notes": "Generated blockout; replace region geometry without changing IDs."})
        areas.append(entry)
    # Stable named world transitions. The forest and campus gates remain explicit and stateful.
    by = {a["id"]: a for a in areas}
    links = [("F01","F02","open"),("F02","F03","open"),("F02","F04","open"),("F02","F05","open"),
             ("F04","F06","locked"),("F03","A01","locked"),("A01","C01","open"),
             ("C01","C02","open"),("C01","C03","open"),("C01","C04","open"),("C01","C05","open"),
             ("C05","B01","open"),("C05","B02","open"),("C05","B03","open"),("C05","B04","open"),
             ("B01","S01","open"),("B02","W01","open"),("B03","N01","open"),("B04","D01","open"),
             ("D04","D07","locked"),("W08","W09","locked"),("N09","N10","locked")]
    for src, dst, state in links:
        if src not in by or dst not in by: continue
        for origin, target in ((src, dst), (dst, src)):
            by[origin]["portals"].append({"id": f"P.{origin}.{target}", "cell": {"x": 1, "y": H//2}, "destination": {"areaId": target, "cell": {"x": 1, "y": H//2}}, "state": state, "requires": None if state == "open" else "content_unlock"})
    return {"schema": "lumenfall.tile-layout/1", "coordinateSystem": "x east, y north; Unity local (x, z) = (x, y); origin is cell (0,0)", "tileLegend": {"#":"wall/blocked", ".":"walkable floor", "+":"door or portal cell"}, "areas": areas}

def main():
    data = build(); OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "academy-layouts.json").write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    lines = ["# Academy tile layouts", "", "Generated from `academy-layouts.json` by `scripts/generate_layouts.py`.", "", "Legend: `#` wall, `.` walkable, `+` door/portal. Coordinates use x east and y north.", "", "## Area index", "", "| ID | Area | Kind | Floor | Access |", "|---|---|---|---|---|"]
    for a in data["areas"]:
        lines.append(f"| {a['id']} | {a['name']} | {a['kind']} | {a['floor']} | {a['access']} |")
    for a in data["areas"]:
        lines += ["", f"## {a['id']} — {a['name']}", "", f"`{a['width']}×{a['height']}` {a['floor']} / {a['access']}", "", "```text"] + a["tiles"] + ["```", "", f"Spawn: ({a['spawn']['x']},{a['spawn']['y']})."]
        if a["portals"]: lines.append("Portals: " + ", ".join(f"{p['id']} → {p['destination']['areaId']} ({p['state']})" for p in a["portals"]))
    (OUT / "academy-layouts.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

if __name__ == "__main__": main()
