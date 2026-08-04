#!/bin/bash
# Makes a print-ready QR code for the study sign-up link.
#
#   ./Server/scripts/make-qr.sh "https://your-booking-link"
#
# Writes study-qr.svg and study-qr.png next to the project root.
#
# WHY LOCAL, RATHER THAN A WEBSITE
# A QR code cannot expire — it is just an encoding of whatever text you give it.
# What expires is the middleman: most free online generators quietly encode a
# shortened URL they control, so the code stops working when that service dies,
# rate-limits, or moves the free tier behind a paywall. That failure happens
# after the posters are already on the wall, which is the worst possible time.
#
# qrencode puts the destination URL in the code itself. There is nothing to fail
# afterwards, no account, no tracking, and no ads.

set -euo pipefail

URL="${1:-}"
if [ -z "$URL" ]; then
    echo "Usage: $0 <url>"
    echo "Example: $0 'https://calendar.app.google/xxxxx'"
    exit 1
fi

# Refuse shorteners: the whole point is that nothing sits between the poster and
# the destination. A shortened link reintroduces exactly the dependency this
# script exists to avoid.
case "$URL" in
    *bit.ly*|*tinyurl*|*t.co/*|*goo.gl*|*rebrand.ly*|*short.io*)
        echo "warning: that looks like a shortened URL."
        echo "  If that service ever goes away, every printed poster breaks."
        echo "  Use the full destination link instead."
        read -r -p "  Continue anyway? [y/N] " ok
        [ "$ok" = "y" ] || exit 1
        ;;
esac

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SVG="$ROOT/study-qr.svg"
PNG="$ROOT/study-qr.png"

# -l H  = highest error correction. Survives a scuff, a drawing pin through a
#         corner, or a logo dropped in the middle. Costs a little density, which
#         is free at poster size.
# -m 2  = quiet zone. A QR with no margin often will not scan at all; this is the
#         single most common reason a printed code fails.
qrencode -t SVG -l H -m 2 -o "$SVG" "$URL"
qrencode -t PNG -l H -m 2 -s 20 -o "$PNG" "$URL"

echo "Encoded: $URL"
echo
echo "  $SVG   ← use this for print (vector, scales to any size)"
echo "  $PNG   ← use this on screen or in slides"
echo
echo "Before printing: scan it with your own phone, from the printed page,"
echo "at the distance someone would actually stand. A code that scans on a"
echo "monitor can still fail on paper."
