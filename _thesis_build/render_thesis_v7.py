#!/usr/bin/env python3
"""Render thesis.md -> styled academic PDF (Chrome) with a page-numbered TOC (two-pass) + footer numbers."""
import base64, os, re, subprocess, sys, io, html as htmlmod
import markdown
from pypdf import PdfReader, PdfWriter
from reportlab.pdfgen import canvas

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = "/Users/m/Downloads/DreamCodeVRPlus/_deliverables/thesis_v7.md"
CREST = os.path.join(HERE, "birmingham-crest.png")
OUT_HTML = os.path.join(HERE, "thesis_v7.html")
RAW_PDF = os.path.join(HERE, "thesis_v7_raw.pdf")
OUT_PDF = "/Users/m/Downloads/DreamCodeVR+_Dissertation_v7.pdf"
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

md = open(SRC).read()
body_md = md[md.find("## Declaration"):]
body_md = body_md.replace("*The table of contents is generated automatically on export to PDF.*", "[TOC]")
body_md = body_md.replace("*The lists of figures and tables are generated automatically on export to PDF.*",
                          "*Figures and tables are numbered within their chapters.*")

def make_md():
    return markdown.markdown(body_md, extensions=["tables","fenced_code","toc","sane_lists","attr_list"],
                             extension_configs={"toc": {"toc_depth":"1-2","title":None}})

crest_b64 = base64.b64encode(open(CREST,"rb").read()).decode()
title_page = f"""
<div class="titlepage">
  <img class="crest" src="data:image/png;base64,{crest_b64}"/>
  <div class="ttl">Safe by Construction: Schema-Bounded Action Generation and Rust Orchestration for Real-Time Immersive Speech Programming in VR</div>
  <div class="by">By</div>
  <div class="author">Sandeep Rai</div>
  <div class="meta">Student ID: sxr1156</div>
  <div class="meta">Supervisors: Dr Daniele Giunchi and Dr Eyal Ofek</div>
  <div class="submit">A dissertation submitted to the University of Birmingham<br/>for the degree of <b>Master of Data Science</b></div>
  <div class="meta">Word count: ~10,000 (main body, Chapters&nbsp;1&ndash;6)</div>
  <div class="school">School of Computer Science<br/>University of Birmingham</div>
  <div class="year">2026</div>
</div>"""

CSS = """
@page { size: letter; margin: 22mm 20mm 20mm 20mm; }
* { box-sizing: border-box; }
body { font-family: Georgia,'Times New Roman',serif; font-size:10.8pt; line-height:1.5; color:#111; text-align:justify; hyphens:auto; }
.titlepage { text-align:center; height:246mm; display:flex; flex-direction:column; align-items:center; justify-content:flex-start; page-break-after:always; }
.titlepage .crest { width:34mm; margin:6mm 0 10mm; }
.titlepage .ttl { font-size:20pt; font-weight:700; line-height:1.28; margin:0 6mm 16mm; }
.titlepage .by { margin-top:6mm; font-size:12pt; }
.titlepage .author { font-size:17pt; font-weight:700; margin:3mm 0; }
.titlepage .meta { font-size:12pt; margin:2mm 0; }
.titlepage .submit { font-size:12pt; margin:6mm 0; line-height:1.4; }
.titlepage .school { font-size:12pt; margin-top:12mm; line-height:1.4; }
.titlepage .year { font-size:12pt; margin-top:6mm; }
h1 { font-size:18pt; font-weight:700; page-break-before:always; margin:0 0 .5em; line-height:1.2; padding-bottom:.15em; border-bottom:2px solid #333; }
h1#declaration,h1#acknowledgements,h1#abstract { page-break-before:auto; }
h2 { font-size:13.5pt; font-weight:700; margin:1.25em 0 .4em; line-height:1.25; }
h3 { font-size:11.5pt; font-weight:700; margin:1em 0 .35em; }
p { margin:0 0 .6em; }
a { color:inherit; text-decoration:none; }
code { font-family:'SF Mono',Menlo,Consolas,monospace; font-size:8.7pt; background:#f1f2f4; padding:.5px 3px; border-radius:3px; }
pre { background:#f6f7f8; border:1px solid #e2e4e8; border-radius:5px; padding:9px 11px; overflow:auto; font-size:8.4pt; line-height:1.4; page-break-inside:avoid; }
pre code { background:none; padding:0; }
table { border-collapse:collapse; width:100%; margin:.7em 0; font-size:8.8pt; page-break-inside:avoid; }
th,td { border:1px solid #cfd3d8; padding:4px 7px; text-align:left; vertical-align:top; }
th { background:#eef0f3; font-weight:700; }
tr:nth-child(even) td { background:#fafbfc; }
hr { border:none; border-top:1px solid #dcdfe4; margin:1.1em 0; }
blockquote { border-left:3px solid #c9ced6; margin:.6em 0; padding:.2em 12px; color:#333; background:#f7f8fa; }
ul,ol { margin:0 0 .6em 1.3em; }
li { margin:.12em 0; }
.toc { font-size:10.4pt; }
.toc ul { list-style:none; margin-left:0; padding-left:0; }
.toc ul ul { margin-left:1.3em; }
.toc li { margin:.2em 0; }
.toc a { color:#111; display:flex; align-items:baseline; }
.toc .tt { }
.toc .lead { flex:1 1 auto; border-bottom:1px dotted #b6bcc4; margin:0 5px; position:relative; top:-3px; min-width:12px; }
.toc .pg { flex:none; font-variant-numeric:tabular-nums; }
h1,h2,h3 { page-break-after:avoid; }
table,pre,img { page-break-inside:avoid; }
"""

def render(html_body):
    full = f"""<!doctype html><html><head><meta charset="utf-8"><style>{CSS}</style></head><body>{title_page}{html_body}</body></html>"""
    open(OUT_HTML,"w").write(full)
    if os.path.exists(RAW_PDF): os.remove(RAW_PDF)
    subprocess.run([CHROME,"--headless=new","--no-sandbox","--disable-gpu","--no-pdf-header-footer",
                    f"--print-to-pdf={RAW_PDF}",f"file://{OUT_HTML}"], check=False,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return RAW_PDF

def norm(s): return re.sub(r'[^a-z0-9]','', s.lower())

# ---- pass 1 ----
html1 = make_md()
render(html1)
reader = PdfReader(RAW_PDF)
pages_txt = []
for i in range(len(reader.pages)):
    try: pages_txt.append(reader.pages[i].extract_text() or "")
    except Exception: pages_txt.append("")
# body starts on the page whose text begins with the Chapter-1 title (H1 has page-break-before),
# so we can search only the body and never the TOC list itself.
body_start = next((i for i in range(2, len(pages_txt))
                   if norm(pages_txt[i]).startswith("chapter1introduction")), 2)
mtoc = re.search(r'<div class="toc">(.*?)</div>', html1, re.S)
entries = re.findall(r'<a href="#([^"]+)">(.*?)</a>', mtoc.group(1)) if mtoc else []
first_body = next((k for k,(a,t) in enumerate(entries)
                   if norm(t).startswith("chapter1introduction")), 0)
def page_of(text):
    key = norm(htmlmod.unescape(text))[:22]
    if not key: return None
    for i in range(body_start, len(pages_txt)):
        if key in norm(pages_txt[i]): return i+1   # 1-based printed number
    return None
pagemap = {}
for k,(a,t) in enumerate(entries):
    pagemap[a] = page_of(t) if k >= first_body else None   # leave front matter unnumbered

# ---- rebuild TOC anchors with dot-leader page numbers ----
def repl(m):
    anchor, text = m.group(1), m.group(2)
    pg = pagemap.get(anchor)
    pgs = str(pg) if pg else ""
    return f'<a href="#{anchor}"><span class="tt">{text}</span><span class="lead"></span><span class="pg">{pgs}</span></a>'
html2 = re.sub(r'<a href="#([^"]+)">(.*?)</a>',
               lambda m: repl(m) if mtoc and m.group(0) in mtoc.group(1) else m.group(0), html1)

# ---- pass 2 ----
render(html2)
# ---- stamp footer page numbers (skip title page) ----
reader = PdfReader(RAW_PDF); writer = PdfWriter(); n = len(reader.pages)
for i,page in enumerate(reader.pages):
    if i > 0:
        w=float(page.mediabox.width); h=float(page.mediabox.height)
        buf=io.BytesIO(); c=canvas.Canvas(buf,pagesize=(w,h))
        c.setFont("Times-Roman",9); c.drawCentredString(w/2, 12*72/25.4, str(i+1)); c.save(); buf.seek(0)
        page.merge_page(PdfReader(buf).pages[0])
    writer.add_page(page)
with open(OUT_PDF,"wb") as f: writer.write(f)
mapped = sum(1 for v in pagemap.values() if v)
print(f"pages: {n} | TOC entries: {len(entries)} | numbered: {mapped} | out: {OUT_PDF}")
