#!/bin/bash
# Builds the print-ready poster: embeds a real QR for <url>, renders A4 PDF.
#
#   ./Server/scripts/build-poster.sh "https://abyyworld.github.io/vr-voice-study/"
#
# Writes study-poster.pdf (print this) and study-qr.svg (spare copy).
#
# One command because the URL changes — artifact link today, GitHub Pages
# tomorrow — and a poster whose QR and printed text disagree is worse than one
# with neither. Regenerating both from a single source of truth is the only way
# they stay in step.

set -euo pipefail

URL="${1:-}"
if [ -z "$URL" ]; then
    echo "Usage: $0 <landing-page-url>"
    exit 1
fi

case "$URL" in
    *bit.ly*|*tinyurl*|*t.co/*|*goo.gl*)
        echo "Refusing a shortened URL — if that service dies, every printed poster breaks."
        exit 1 ;;
esac

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SRC="$ROOT/study-site/poster.html"
[ -f "$SRC" ] || { echo "Missing $SRC"; exit 1; }

command -v qrencode >/dev/null || { echo "Install qrencode:  brew install qrencode"; exit 1; }

EDGE="/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
if   [ -x "$CHROME" ]; then BROWSER="$CHROME"
elif [ -x "$EDGE" ];   then BROWSER="$EDGE"
else echo "Need Chrome or Edge to render the PDF."; exit 1; fi

# -l H survives a scuff or a pin through a corner. -m 0 because the poster
# already provides the quiet zone as white padding around the box; qrencode's
# own margin on top of that would shrink the modules for no benefit.
qrencode -t SVG -l H -m 0 -o "$ROOT/study-qr.svg" "$URL"

python3 - "$SRC" "$ROOT/study-qr.svg" "$URL" "$ROOT/study-site/poster-print.html" <<'PY'
import re, sys
src, qr, url, out = sys.argv[1:5]
html = open(src).read()

# Lift the QR's drawing commands out of qrencode's standalone SVG so they can be
# inlined. An <img> would need an external file, which the artifact host's CSP
# blocks and which would break the moment the PDF is moved.
body = re.search(r'<g id="QRcode".*?</g>', open(qr).read(), re.S).group(0)
box  = re.search(r'viewBox="([^"]+)"', open(qr).read()).group(1)

inline = (f'<svg viewBox="{box}" width="100%" height="100%" '
          f'shape-rendering="crispEdges" role="img" '
          f'aria-label="QR code linking to the study information page">{body}</svg>')

html = re.sub(r'<div class="qr">.*?</div>',
              f'<div class="qr" style="padding:1.5mm;width:40mm;height:40mm;">{inline}</div>',
              html, flags=re.S)

# A long URL is unreadable and untypeable in 10pt. The QR carries it; the
# printed line just needs to give someone without a camera a way through.
short = re.sub(r'^https?://', '', url).rstrip('/')
html = re.sub(r'<div class="url">.*?</div>',
              f'<div class="url" style="font-size:10.4pt;">{short}</div>'
              '<div class="hint" style="margin-top:1.6mm;">Or email '
              '<b>axj509@student.bham.ac.uk</b> and we&rsquo;ll send the link.</div>',
              html, flags=re.S)

html = html.replace('<title>Participants Wanted — VR Voice Study</title>\n', '')
open(out, "w").write(
    '<!DOCTYPE html><html lang="en"><head><meta charset="utf-8">'
    '<title>Participants Wanted — VR Voice Study</title></head><body>\n'
    + html + '\n</body></html>\n')
print(f"  QR encodes: {url}")
PY

# One source of truth: whatever gets printed is also what gets published.
cp "$ROOT/study-site/poster-print.html" "$ROOT/study-site/poster-built.html"

"$BROWSER" --headless --disable-gpu --window-size=1200,1700 --no-pdf-header-footer \
    --print-to-pdf="$ROOT/study-poster.pdf" \
    "file://$ROOT/study-site/poster-print.html" >/dev/null 2>&1

python3 - "$ROOT/study-poster.pdf" <<'PY'
import re, sys
d = open(sys.argv[1], "rb").read()
pages = d.count(b"/Type /Page") - d.count(b"/Type /Pages")
box = [float(x) for x in re.search(rb"/MediaBox\s*\[([\d\.\s]+)\]", d).group(1).split()]
w, h = box[2]-box[0], box[3]-box[1]
ok = pages == 1 and abs(w-595) < 3 and abs(h-842) < 3
print(f"  study-poster.pdf — {pages} page, {w:.0f}x{h:.0f}pt "
      f"{'(A4 portrait)' if ok else '*** WRONG — check before printing ***'}")
PY

echo
echo "  Print study-poster.pdf at 100% scale."
echo "  Then scan the printed sheet with your phone, from where someone would stand."
