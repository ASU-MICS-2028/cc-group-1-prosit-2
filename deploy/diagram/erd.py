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
COLUMNS = [  # left to right; each column is stacked top to bottom with a gap
    ("people", ["users", "login_codes", "auth_keys", "user_photos"]),
    ("clinical", ["patients", "patient_identifiers", "consents"]),
    ("clinical", ["appointments", "vitals", "observations"]),
    ("clinical", ["test_results", "prescriptions", "observation_codes"]),
    ("money", ["payments", "media"]),
    ("audit", ["access_log"]),
]
ROW, HEAD, W, GAP = 19, 28, 280, 36
PLACE, bottom = {}, 0
for k, (area, tables) in enumerate(COLUMNS):
    y = 120
    for t in tables:
        PLACE[t] = (area, 40 + k * 320, y)
        y += HEAD + ROW * len(cols.get(t, [])) + 6 + GAP
    bottom = max(bottom, y)

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

# many (child) -> one (parent), crow's foot at the child end
for t, c, rt, rc in fks:
    if t in ids and rt in ids:
        same_column = PLACE[t][1] == PLACE[rt][1]
        pins = "exitX=0;exitY=0;exitDx=0;exitDy=14;entryX=0;entryY=0;entryDx=0;entryDy=14;" if same_column else ""
        cells.append(f'<mxCell id="{nid()}" value={quoteattr(c)} style={quoteattr("edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;endArrow=ERmandOne;startArrow=ERmany;endFill=0;startFill=0;strokeColor=#666666;fontSize=10;fontColor=#555555;labelBackgroundColor=#FFFFFF;" + pins)} edge="1" parent="1" source="{ids[t]}" target="{ids[rt]}"><mxGeometry relative="1" as="geometry"/></mxCell>')

legend = ("<b>HealthConnect Ghana: database (PostgreSQL 17)</b><br>"
          "<font style='font-size:11px'>Read from a database with every migration applied (0000-0009). PK primary key · FK foreign key · '·' = required. "
          "Crow's foot = many. 16 tables + the view <b>surveillance_weekly</b> (anonymised weekly counts, cells under 5 hidden). "
          "Workers' database roles (hc_chv, hc_clinician, hc_ghs_official, hc_auditor) see patient rows only in their district; "
          "access_log is append-only and hash-chained.</font>")
cells.append(f'<mxCell id="{nid()}" value={quoteattr(legend)} style={quoteattr("text;html=1;align=left;verticalAlign=top;whiteSpace=wrap;fontSize=16;")} vertex="1" parent="1"><mxGeometry x="40" y="20" width="1880" height="80" as="geometry"/></mxCell>')
for i, (fill, stroke, title) in enumerate(AREAS.values()):
    cells.append(f'<mxCell id="{nid()}" value={quoteattr(title)} style={quoteattr(f"rounded=1;html=1;fillColor={fill};strokeColor={stroke};fontSize=11;")} vertex="1" parent="1"><mxGeometry x="{40 + i * 330}" y="{bottom}" width="310" height="28" as="geometry"/></mxCell>')

xml = f'''<mxfile host="app.diagrams.net" agent="HealthConnect deploy docs" version="24.7.17">
  <diagram id="healthconnect-erd" name="Database (ERD)">
    <mxGraphModel dx="1600" dy="1000" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="1960" pageHeight="{bottom + 60}" math="0" shadow="0">
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
