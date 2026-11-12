# Regenerate: python3 deploy/diagram/erd.py <columns.txt> <fks.txt> deploy/database-erd.drawio
# The two inputs come from a database with every migration applied (information_schema), so the
# diagram is the real schema, not a drawing of it:
#   columns.txt  table|column|type|nullable|PK      fks.txt  table|column|ref_table|ref_column
import sys
from collections import defaultdict
from xml.sax.saxutils import quoteattr

cols, fks = defaultdict(list), []
for line in open(sys.argv[1]):
    t, c, ty, null, pk = line.rstrip("\n").split("|")
    cols[t].append((c, ty, null == "YES", pk == "PK"))
for line in open(sys.argv[2]):
    if line.strip():
        fks.append(tuple(line.rstrip("\n").split("|")))
fk_cols = {(t, c) for t, c, _, _ in fks}

SHORT = {"character varying": "varchar", "timestamp with time zone": "timestamptz", "timestamp without time zone": "timestamp",
         "integer": "int", "bigint": "bigint", "boolean": "bool", "numeric": "numeric", "uuid": "uuid", "text": "text",
         "bytea": "bytea", "date": "date", "jsonb": "jsonb", "smallint": "smallint", "ARRAY": "array", "inet": "inet"}

AREAS = {  # colour, title
    "people": ("#DAE8FC", "#6C8EBF", "People and sign-in"),
    "clinical": ("#D5E8D4", "#82B366", "Clinical record (row-level security per district)"),
    "money": ("#FFF2CC", "#D6B656", "Files and payments"),
    "audit": ("#F8CECC", "#B85450", "Audit"),
}
# A hub layout: patients in the middle, what points at it on either side, chains running outwards,
# and the tables with no foreign keys (sign-in and audit) set apart. Every line runs from the
# foreign-key row to the key it references, and bends at its own x in the gap, so none overlap.
ROW, HEAD, W, GAP, COLGAP = 19, 28, 270, 30, 150
X = [40 + k * (W + COLGAP) for k in range(5)]
def height(t): return HEAD + ROW * len(cols.get(t, [])) + 6
LAYOUT = [  # (table, column, y) ; y None = below the previous table in that column
    ("patient_identifiers", 0, 140), ("consents", 0, None), ("media", 0, None),
    ("patients", 1, 140),
    ("appointments", 2, 140), ("prescriptions", 2, None), ("test_results", 2, None), ("vitals", 2, None), ("observations", 2, None),
    ("payments", 3, 140), ("observation_codes", 3, "observations"),
    ("users", 4, 140), ("user_photos", 4, None),
    ("login_codes", 4, "gap"), ("auth_keys", 4, None), ("access_log", 4, None),
]
AREA = {"users": "people", "user_photos": "people", "login_codes": "people", "auth_keys": "people",
        "payments": "money", "media": "money", "access_log": "audit"}
PLACE, last, bottom = {}, {}, 0
for t, k, y in LAYOUT:
    if y is None: y = last[k]
    elif y == "gap": y = last[k] + 70
    elif isinstance(y, str): y = PLACE[y][2]
    PLACE[t] = (AREA.get(t, "clinical"), X[k], y)
    last[k] = y + height(t) + GAP
    bottom = max(bottom, last[k])
STANDALONE_Y = PLACE["login_codes"][2] - 40

cells, ids, n = [], {}, [1]
def nid():
    n[0] += 1
    return f"e{n[0]}"

for table, columns in cols.items():
    area, x, y = PLACE.get(table, ("audit", 1640, 700))
    fill, stroke, _ = AREAS[area]
    tid = nid(); ids[table] = tid
    h = HEAD + ROW * len(columns) + 6
    cells.append(f'<mxCell id="{tid}" value={quoteattr("<b>" + table + "</b>")} style={quoteattr(f"swimlane;html=1;fontStyle=0;childLayout=stackLayout;horizontal=1;startSize={HEAD};horizontalStack=0;resizeParent=1;resizeLast=0;collapsible=0;marginBottom=0;rounded=1;arcSize=4;fillColor={fill};strokeColor={stroke};swimlaneFillColor=#FFFFFF;fontSize=13;")} vertex="1" parent="1"><mxGeometry x="{x}" y="{y}" width="{W}" height="{h}" as="geometry"/></mxCell>')
    for i, (c, ty, null, pk) in enumerate(columns):
        key = "PK" if pk else ("FK" if (table, c) in fk_cols else "")
        label = f"<b>{c}</b>" if pk else c
        label = f"<font color='#999999'>{key:<2}</font>&nbsp;{label}" if key else f"&nbsp;&nbsp;&nbsp;&nbsp;{label}"
        label += f" <font color='#888888'>{SHORT.get(ty, ty)}{'' if null else ' ·'}</font>"
        cells.append(f'<mxCell id="{nid()}" value={quoteattr(label)} style={quoteattr("text;html=1;strokeColor=none;fillColor=none;align=left;verticalAlign=middle;spacingLeft=6;fontSize=11;fontFamily=Menlo;")} vertex="1" parent="{tid}"><mxGeometry y="{HEAD + i * ROW}" width="{W}" height="{ROW}" as="geometry"/></mxCell>')

# many (child) -> one (parent): from the FK row to the referenced key's row, crow's foot at the child
def row_y(t, c):
    names = [x[0] for x in cols[t]]
    return HEAD + ROW * names.index(c) + ROW / 2
fan = defaultdict(int)
for t, c, rt, rc in fks:
    if t not in ids or rt not in ids: continue
    _, cx, cy = PLACE[t]; _, px, py = PLACE[rt]
    ch, ph = height(t), height(rt)
    style = "edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;endArrow=ERmandOne;startArrow=ERmany;endFill=0;startFill=0;strokeColor=#5A6B80;strokeWidth=1.3;"
    points = ""
    if cx == px:  # same column: straight up into the table above
        style += "exitX=0.5;exitY=0;exitDx=0;exitDy=0;entryX=0.5;entryY=1;entryDx=0;entryDy=0;"
    else:
        child_right = cx < px  # the child is left of its parent
        ey, ry = cy + row_y(t, c), py + row_y(rt, rc)
        style += f"exitX={1 if child_right else 0};exitY={(ey - cy) / ch:.4f};exitDx=0;exitDy=0;"
        style += f"entryX={0 if child_right else 1};entryY={(ry - py) / ph:.4f};entryDx=0;entryDy=0;"
        gap_left = (cx + W) if child_right else (px + W)
        key = (gap_left, "r" if child_right else "l")
        fan[key] += 1
        ex = gap_left + 20 + 22 * (fan[key] - 1)
        points = f'<Array as="points"><mxPoint x="{ex}" y="{ey}"/><mxPoint x="{ex}" y="{ry}"/></Array>'
    cells.append(f'<mxCell id="{nid()}" value="" style={quoteattr(style)} edge="1" parent="1" source="{ids[t]}" target="{ids[rt]}"><mxGeometry relative="1" as="geometry">{points}</mxGeometry></mxCell>')

cells.append(f'<mxCell id="{nid()}" value={quoteattr("<b>No foreign keys</b>: sign-in codes, the token signing key, and the audit log (who did what; append-only, hash-chained)")} style={quoteattr("text;html=1;align=left;verticalAlign=bottom;whiteSpace=wrap;fontSize=11;fontColor=#555555;")} vertex="1" parent="1"><mxGeometry x="{X[4]}" y="{STANDALONE_Y - 10}" width="{W}" height="44" as="geometry"/></mxCell>')
legend = ("<b>HealthConnect Ghana: database (PostgreSQL 17)</b><br>"
          "<font style='font-size:11px'>Read from a database with every migration applied (0000-0009). PK primary key · FK foreign key · '·' = required. "
          "Lines run from a foreign key to the key it references; crow's foot = many, bar = exactly one. patients is the hub. 16 tables + the view <b>surveillance_weekly</b> (anonymised weekly counts, cells under 5 hidden). "
          "Workers' database roles (hc_chv, hc_clinician, hc_ghs_official, hc_auditor) see patient rows only in their district; "
          "access_log is append-only and hash-chained.</font>")
cells.append(f'<mxCell id="{nid()}" value={quoteattr(legend)} style={quoteattr("text;html=1;align=left;verticalAlign=top;whiteSpace=wrap;fontSize=16;")} vertex="1" parent="1"><mxGeometry x="40" y="20" width="2100" height="90" as="geometry"/></mxCell>')
for i, (fill, stroke, title) in enumerate(AREAS.values()):
    cells.append(f'<mxCell id="{nid()}" value={quoteattr(title)} style={quoteattr(f"rounded=1;html=1;fillColor={fill};strokeColor={stroke};fontSize=11;")} vertex="1" parent="1"><mxGeometry x="{40 + i * 330}" y="{bottom}" width="310" height="28" as="geometry"/></mxCell>')

xml = f'''<mxfile host="app.diagrams.net" agent="HealthConnect deploy docs" version="24.7.17">
  <diagram id="healthconnect-erd" name="Database (ERD)">
    <mxGraphModel dx="1600" dy="1000" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="2200" pageHeight="{bottom + 60}" math="0" shadow="0">
      <root>
        <mxCell id="0"/>
        <mxCell id="1" parent="0"/>
        {chr(10).join("        " + c for c in cells).strip()}
      </root>
    </mxGraphModel>
  </diagram>
</mxfile>
'''
open(sys.argv[3], "w").write(xml)
print("tables:", len(cols), "relations:", len(fks))
