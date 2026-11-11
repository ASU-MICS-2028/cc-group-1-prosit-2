# Regenerate: python3 deploy/diagram/generate.py deploy/architecture.drawio [before]
#   "before" draws the architecture as it was before the Lab 7 event layer and video calls
#   (the Lab 8 review's starting point); the default draws it as it is now, new parts in pink.
# Generates deploy/architecture.drawio: HealthConnect on AWS, with draw.io's AWS 2025 icons.
import sys
from xml.sax.saxutils import quoteattr

BEFORE = len(sys.argv) > 2 and sys.argv[2] == "before"
NEW = "#D6336C"   # what the Lab 7/8 revision added

cells, n = [], [1]
def nid():
    n[0] += 1; return f"c{n[0]}"

ICON = "sketch=0;outlineConnect=0;fontColor=#232F3E;gradientColor=none;fillColor={c};strokeColor=#ffffff;dashed=0;verticalLabelPosition=bottom;verticalAlign=top;align=center;html=1;fontSize=12;fontStyle=0;aspect=fixed;shape=mxgraph.aws4.resourceIcon;resIcon=mxgraph.aws4.{i};"
GROUP = "points=[[0,0],[0.25,0],[0.5,0],[0.75,0],[1,0],[1,0.25],[1,0.5],[1,0.75],[1,1],[0.75,1],[0.5,1],[0.25,1],[0,1],[0,0.75],[0,0.5],[0,0.25]];outlineConnect=0;gradientColor=none;html=1;whiteSpace=wrap;fontSize=13;fontStyle=1;container=0;pointerEvents=0;collapsible=0;recursiveResize=0;shape=mxgraph.aws4.group;grIcon=mxgraph.aws4.{g};strokeColor={s};fillColor={f};verticalAlign=top;align=left;spacingLeft=30;fontColor={s};dashed={d};"
COLORS = {"net": "#8C4FFF", "compute": "#ED7100", "db": "#C925D1", "storage": "#7AA116", "sec": "#DD344C", "mgmt": "#E7157B", "integ": "#E7157B", "cont": "#ED7100"}

def cell(value, style, x, y, w, h, vertex=True, src=None, tgt=None, cid=None):
    cid = cid or nid()
    attrs = f'id="{cid}" value={quoteattr(value)} style={quoteattr(style)} parent="1"'
    if vertex:
        cells.append(f'<mxCell {attrs} vertex="1"><mxGeometry x="{x}" y="{y}" width="{w}" height="{h}" as="geometry"/></mxCell>')
    else:
        cells.append(f'<mxCell {attrs} edge="1" source="{src}" target="{tgt}"><mxGeometry relative="1" as="geometry"/></mxCell>')
    return cid

def group(label, g, x, y, w, h, stroke, fill="none", dashed=0):
    return cell(label, GROUP.format(g=g, s=stroke, f=fill, d=dashed), x, y, w, h)

def icon(label, kind, i, x, y, size=56):
    return cell(label, ICON.format(c=COLORS[kind], i=i), x, y, size, size)

def note(text, x, y, w, h, fill="#F5F5F5"):
    return cell(text, f"rounded=1;whiteSpace=wrap;html=1;fillColor={fill};strokeColor=#B3B3B3;fontSize=11;align=left;verticalAlign=top;spacing=8;arcSize=6;", x, y, w, h)

def edge(src, tgt, label="", dashed=False, color="#545B64", pins=""):
    style = f"edgeStyle=orthogonalEdgeStyle;rounded=1;orthogonalLoop=1;jettySize=auto;html=1;endArrow=block;endFill=1;strokeColor={color};fontSize=11;fontColor=#232F3E;labelBackgroundColor=#FFFFFF;{pins}"
    if dashed: style += "dashed=1;"
    return cell(label, style, 0, 0, 0, 0, vertex=False, src=src, tgt=tgt)

# Title
cell("<b>HealthConnect Ghana: AWS architecture</b><br>Every request enters through CloudFront; nothing private faces the internet. Production and staging share one Graviton server.",
     "text;html=1;align=left;verticalAlign=top;whiteSpace=wrap;fontSize=16;", 40, 20, 1100, 50)
H = 0 if BEFORE else 240   # room for the event layer

# Users and edge
phone = cell("Health worker's phone<br><font style='font-size:10px'>installed PWA, works offline<br>(IndexedDB + outbox)</font>",
             "sketch=0;outlineConnect=0;fontColor=#232F3E;gradientColor=none;fillColor=#232F3D;strokeColor=none;dashed=0;verticalLabelPosition=bottom;verticalAlign=top;align=center;html=1;fontSize=12;aspect=fixed;pointerEvents=1;shape=mxgraph.aws4.mobile_client;",
             60, 380, 41, 66)
edge_grp = group("Edge (global)", "group_aws_cloud", 170, 250, 200, 420, "#8C4FFF", dashed=1)
cf_prod = icon("CloudFront: production<br><font style='font-size:10px'>d2opah5iezm17k.cloudfront.net<br>HTTPS only, http → 301</font>", "net", "cloudfront", 242, 300)
cf_stg = icon("CloudFront: staging<br><font style='font-size:10px'>adds X-HC-Env: staging<br>noindex</font>", "net", "cloudfront", 242, 500)

# AWS cloud, region, VPC
group("AWS Cloud (account 659500703236)", "group_aws_cloud_alt", 400, 90, 1160, 860 + H, "#232F3E")
group("Africa (Cape Town) af-south-1", "group_region", 420, 130, 1120, 800 + H, "#00A4A6", dashed=1)
group("VPC healthconnect 10.30.0.0/16 (2 Availability Zones)", "group_vpc2", 440, 170, 620, 740, "#8C4FFF")

group("Public subnet", "group_public_subnet", 460, 210, 580, 150, "#7AA116", "#F2F6E8")
nat = icon("fck-nat (t4g.nano)<br><font style='font-size:10px'>self-healing, outbound only</font>", "net", "nat_gateway", 714, 240, 48)
igw = icon("Internet gateway", "net", "internet_gateway", 930, 240, 48)

group("Private app subnets", "group_private_subnet", 460, 390, 580, 300, "#00A4A6", "#E6F6F7")
alb = icon("Internal load balancer<br><font style='font-size:10px'>staging header → :8081<br>otherwise → :80</font>", "net", "application_load_balancer", 500, 460)
ec2 = icon("App server, EC2 t4g.small<br><font style='font-size:10px'>Graviton, Compose, SSM only</font>", "compute", "instance2", 710, 460)
note("<b>production :80</b> (main)<br>nginx + PWA, API<br><b>staging :8081</b> (development)<br>nginx + PWA, API"
     + ("" if BEFORE else "<br><font color='#D6336C'><b>+ /api/calls WebSocket:</b> video-call signalling</font>"), 800, 560, 200, 110 if not BEFORE else 90, "#FFF4E6")
s3ep = icon("S3 gateway endpoint<br><font style='font-size:10px'>free</font>", "net", "endpoints", 950, 440, 48)

group("Private database subnets (no internet route)", "group_private_subnet", 460, 720, 580, 170, "#00A4A6", "#E6F6F7")
rds = icon("RDS PostgreSQL 17, db.t4g.micro<br><font style='font-size:10px'>healthconnect + healthconnect_staging<br>KMS-encrypted, TLS only, 7-day restore</font>", "db", "rds_instance", 710, 760)

# Regional services, two columns
a, b = 1110, 1330
ecr = icon("ECR<br><font style='font-size:10px'>arm64 images, immutable,<br>scanned on push</font>", "cont", "ecr", a, 190, 48)
s3 = icon("S3 media bucket<br><font style='font-size:10px'>consent recordings, photos<br>SSE-KMS, versioned</font>", "storage", "s3", a, 360, 48)
sm = icon("Secrets Manager<br><font style='font-size:10px'>database password</font>", "sec", "secrets_manager", a, 530, 48)
kms = icon("KMS key<br><font style='font-size:10px'>alias/healthconnect-data<br>encrypts RDS, S3, secret, disk</font>", "sec", "key_management_service", a, 690, 48)
ssm = icon("Systems Manager<br><font style='font-size:10px'>Run Command deploys,<br>Session Manager</font>", "mgmt", "systems_manager", b, 190, 48)
cw = icon("CloudWatch<br><font style='font-size:10px'>alarms, dashboard, logs<br>EC2 auto-recover, reboot</font>", "mgmt", "cloudwatch_2", b, 360, 48)
sns = icon("SNS alerts<br><font style='font-size:10px'>email the team</font>", "integ", "sns", b, 530, 48)
sched = icon("EventBridge Scheduler<br><font style='font-size:10px'>optional nightly stop<br>23:00-07:00 Ghana time</font>", "integ", "eventbridge_scheduler", b, 690, 48)

# CI/CD and notes, outside AWS
gh = cell("<b>GitHub Actions</b><br><font style='font-size:10px'>ci.yml: lint, tests ≥70%, build,<br>Terraform, Trivy, gitleaks" + ("" if BEFORE else ", Lambda tests") + "<br>deploy.yml: arm64 build → Trivy →<br>SSM release → health → rollback</font>",
          "rounded=1;whiteSpace=wrap;html=1;fillColor=#24292F;strokeColor=none;fontColor=#FFFFFF;fontSize=12;align=left;spacing=10;arcSize=8;", 1600, 130, 240, 120)
oidc = cell("GitHub OIDC → IAM role<br>healthconnect-github-deploy<br><font style='font-size:10px'>production needs approval; staging automatic</font>",
            "rounded=1;whiteSpace=wrap;html=1;fillColor=#FDECEE;strokeColor=#DD344C;fontSize=11;", 1600, 300, 240, 70)
note("<b>Phones upload photos and recordings straight to S3</b> with a 5-minute presigned link from the API.", 1600, 420, 240, 60)
note("<b>Budget</b>: $20/month on Project=healthconnect tags, alerts at 50 / 80 / 100%.", 1600, 500, 240, 50)
note("<b>us-east-1</b>: CloudFront 5xx alarm + its SNS topic (CloudFront metrics live there).", 1600, 570, 240, 60)

# Request path
edge(phone, cf_prod, "HTTPS", pins="exitX=1;exitY=0.5;exitDx=0;exitDy=0;entryX=0;entryY=0.5;entryDx=0;entryDy=0;")
edge(phone, cf_stg, "HTTPS", dashed=True, pins="exitX=1;exitY=0.5;exitDx=0;exitDy=0;entryX=0;entryY=0.5;entryDx=0;entryDy=0;")
edge(cf_prod, alb, "VPC origin (private)")
edge(cf_stg, alb, "VPC origin + X-HC-Env")
edge(alb, ec2, ":80 / :8081")
edge(ec2, rds, "TLS 5432")
edge(ec2, nat, "443 out", dashed=True)
edge(nat, igw, "")
edge(ec2, s3ep, "", pins="exitX=1;exitY=0.25;exitDx=0;exitDy=0;entryX=0;entryY=0.5;entryDx=0;entryDy=0;")
edge(s3ep, s3, "media", pins="exitX=1;exitY=0.5;exitDx=0;exitDy=0;entryX=0;entryY=0.5;entryDx=0;entryDy=0;")
edge(ec2, sm, "password", pins="exitX=1;exitY=0.75;exitDx=0;exitDy=0;entryX=0;entryY=0.5;entryDx=0;entryDy=0;")
edge(cw, sns, "alarms")
# Delivery
edge(gh, oidc, "short-lived credentials")
edge(oidc, ecr, "push images")
edge(oidc, ssm, "send-command")
edge(ssm, ec2, "run remote-deploy.sh", dashed=True)

if not BEFORE:
    # ---- Lab 7: appointment events, two independent serverless consumers ----
    y0 = 940
    group("Appointment events (serverless, Lab 7): the API publishes created / moved / cancelled to two independent consumers", "group_aws_cloud", 440, y0, 1080, 210, NEW, "#FFF0F5")
    topic = icon("SNS topic<br><font style='font-size:10px'>appointment-events<br>KMS-encrypted</font>", "integ", "sns", 480, y0 + 70, 48)
    q1 = icon("SQS reminders<br><font style='font-size:10px'>+ DLQ after 3 tries</font>", "integ", "sqs", 640, y0 + 40, 44)
    q2 = icon("SQS specialist-notify<br><font style='font-size:10px'>referrals + teleconsults only</font>", "integ", "sqs", 640, y0 + 125, 44)
    l1 = icon("Lambda appointment-reminder", "compute", "lambda", 820, y0 + 40, 44)
    l2 = icon("Lambda specialist-notify", "compute", "lambda", 820, y0 + 125, 44)
    sch = icon("Scheduler<br><font style='font-size:10px'>one per visit, 24 h before</font>", "integ", "eventbridge_scheduler", 1000, y0 + 40, 44)
    ca = icon("SNS clinician alerts<br><font style='font-size:10px'>email; URGENT if emergency</font>", "integ", "sns", 1000, y0 + 125, 44)
    icon("DynamoDB<br><font style='font-size:10px'>events each function handled<br>(no duplicate texts)</font>", "db", "dynamodb", 1200, y0 + 125, 44)
    sms = note("<b>SMS to the patient</b><br>Arkesel (Ghana) when a key is set; logged until then", 1170, y0 + 30, 170, 60, "#FFFFFF")
    note("<b>Alarms</b> on dead letters and function errors → SNS alerts. A failed publish never fails the save.", 1360, y0 + 30, 150, 110, "#FFFFFF")
    edge(ec2, topic, "", color=NEW, pins="exitX=0.25;exitY=1;exitDx=0;exitDy=0;entryX=0.5;entryY=0;entryDx=0;entryDy=0;")
    edge(topic, q1, "", color=NEW)
    edge(topic, q2, "", color=NEW)
    edge(q1, l1, "", color=NEW)
    edge(q2, l2, "", color=NEW)
    edge(l1, sch, "", color=NEW)
    edge(sch, sms, "when due", color=NEW)
    edge(l2, ca, "", color=NEW)

    # ---- Lab 7: teleconsult video call ----
    doc = cell("Doctor or nurse<br><font style='font-size:10px'>same app, Referrals inbox</font>",
               "sketch=0;outlineConnect=0;fontColor=#232F3E;gradientColor=none;fillColor=#232F3D;strokeColor=none;dashed=0;verticalLabelPosition=bottom;verticalAlign=top;align=center;html=1;fontSize=12;aspect=fixed;pointerEvents=1;shape=mxgraph.aws4.mobile_client;",
               60, 760, 41, 66)
    note("<font color='#D6336C'><b>Video call (WebRTC)</b></font><br>Signalling over the API's WebSocket; audio/video go phone to phone. STUN finds the path; a TURN relay is needed when both are behind mobile-carrier NAT (not deployed yet). 320×240, 15 fps, ≤250 kbps.",
         20, 870, 360, 90, "#FFF0F5")
    edge(phone, doc, "WebRTC media (direct)", dashed=True, color=NEW, pins="exitX=0.5;exitY=1;exitDx=0;exitDy=0;entryX=0.5;entryY=0;entryDx=0;entryDy=0;")

xml = f'''<mxfile host="app.diagrams.net" agent="HealthConnect deploy docs" version="24.7.17">
  <diagram id="healthconnect-aws" name="HealthConnect on AWS">
    <mxGraphModel dx="1600" dy="1000" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="1880" pageHeight="{1000 + H}" math="0" shadow="0">
      <root>
        <mxCell id="0"/>
        <mxCell id="1" parent="0"/>
        {chr(10).join("        " + c for c in cells).strip()}
      </root>
    </mxGraphModel>
  </diagram>
</mxfile>
'''
open(sys.argv[1], "w").write(xml)
print("cells:", len(cells))
