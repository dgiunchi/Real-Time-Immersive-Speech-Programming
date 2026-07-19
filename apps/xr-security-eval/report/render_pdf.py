#!/usr/bin/env python3
"""Render PAPER.md -> self-contained styled HTML -> PDF (chrome headless).

Figures are inlined as base64 data URIs so the PDF is fully self-contained.
Usage: python3 report/render_pdf.py  (run from apps/xr-security-eval)
"""
import base64
import mimetypes
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)  # apps/xr-security-eval
# Optional CLI: render_pdf.py [input.md] [output.pdf]  (defaults to the paper).
import sys as _sys  # noqa: E402

PAPER = os.path.join(ROOT, _sys.argv[1]) if len(_sys.argv) > 1 else os.path.join(ROOT, "PAPER.md")
_stem = os.path.splitext(os.path.basename(PAPER))[0].lower()
OUT_HTML = os.path.join(HERE, f"{_stem}.html")
OUT_PDF = (
    os.path.expanduser(_sys.argv[2])
    if len(_sys.argv) > 2
    else os.path.expanduser("~/Desktop/DreamCodeVR-Security-Paper.pdf")
)

import markdown  # noqa: E402


def data_uri(path):
    mime = mimetypes.guess_type(path)[0] or "image/png"
    with open(path, "rb") as fh:
        b64 = base64.b64encode(fh.read()).decode("ascii")
    return f"data:{mime};base64,{b64}"


def main():
    with open(PAPER) as fh:
        md = fh.read()

    # Inline figures: ![alt](figures/x.png) -> data URI
    def repl(m):
        alt, rel = m.group(1), m.group(2)
        p = os.path.join(ROOT, rel)
        if os.path.exists(p):
            return f"![{alt}]({data_uri(p)})"
        return m.group(0)

    md = re.sub(r"!\[([^\]]*)\]\((figures/[^)]+)\)", repl, md)

    body = markdown.markdown(
        md,
        extensions=["tables", "fenced_code", "toc", "sane_lists", "attr_list"],
    )

    html = f"""<!doctype html><html><head><meta charset="utf-8">
<style>
  @page {{ size: A4; margin: 18mm 16mm; }}
  html {{ -webkit-print-color-adjust: exact; print-color-adjust: exact; }}
  body {{ font-family: "Georgia","Times New Roman",serif; font-size: 10.5pt;
          line-height: 1.5; color: #1a1a1a; max-width: 720px; margin: 0 auto; }}
  h1 {{ font-size: 20pt; line-height: 1.2; margin: 0 0 4pt; text-wrap: balance; }}
  h2 {{ font-size: 13.5pt; margin: 18pt 0 6pt; border-bottom: 1px solid #ccc;
        padding-bottom: 3pt; }}
  h3 {{ font-size: 11.5pt; margin: 13pt 0 4pt; }}
  h1 + p em, p > em:only-child {{ color: #444; }}
  blockquote {{ font-size: 9.3pt; color: #333; background: #f4f5f7;
                border-left: 3px solid #b3b9c4; margin: 10pt 0; padding: 7pt 12pt; }}
  code {{ font-family: "SFMono-Regular",Consolas,monospace; font-size: 8.8pt;
          background: #f0f1f3; padding: 0.5pt 3pt; border-radius: 3px; }}
  pre {{ background: #1e2530; color: #e6e6e6; padding: 10pt 12pt; border-radius: 5px;
         overflow-x: auto; font-size: 8.4pt; line-height: 1.4; }}
  pre code {{ background: none; color: inherit; padding: 0; }}
  table {{ border-collapse: collapse; width: 100%; margin: 10pt 0; font-size: 9pt; }}
  th, td {{ border: 1px solid #ccc; padding: 4pt 7pt; text-align: left;
            vertical-align: top; }}
  th {{ background: #eceef1; font-family: Helvetica,Arial,sans-serif; }}
  tr:nth-child(even) td {{ background: #fafbfc; }}
  img {{ max-width: 100%; display: block; margin: 12pt auto; }}
  a {{ color: #10457a; text-decoration: none; }}
  hr {{ border: none; border-top: 1px solid #ddd; margin: 16pt 0; }}
  h2, h3 {{ page-break-after: avoid; }}
  table, pre, img {{ page-break-inside: avoid; }}
</style></head><body>
{body}
</body></html>"""

    with open(OUT_HTML, "w") as fh:
        fh.write(html)

    cmd = [
        "google-chrome", "--headless=new", "--no-sandbox",
        "--disable-gpu", "--no-pdf-header-footer",
        f"--print-to-pdf={OUT_PDF}", f"file://{OUT_HTML}",
    ]
    r = subprocess.run(cmd, capture_output=True, text=True)
    if not os.path.exists(OUT_PDF):
        sys.stderr.write(r.stderr)
        sys.exit("PDF render failed")
    print(f"wrote {OUT_HTML}")
    print(f"wrote {OUT_PDF}  ({os.path.getsize(OUT_PDF)//1024} KB)")


if __name__ == "__main__":
    main()
