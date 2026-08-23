#!/usr/bin/env bash
# UNI-DAY plug-and-play for an ALREADY-INSTALLED headset app (no adb, no rebuild).
#
# The installed headset app dials ONE fixed server IP:port. On a shared LAN you make
# THIS machine answer at that address, then open the app on the headset — it connects
# to your Rust backend. No address is baked in; set DCVR_ROOMSERVER_HOST or pass it.
# Two modes:
#
#   A) The server is a LIVE remote RoomServer host:
#        scripts/uni-day.sh remote 192.0.2.10 8009        # <host> <port> (example IP)
#      -> points our Rust backend at that RoomServer as a service peer.
#
#   B) The server address must be answered by THIS machine (IP alias):
#        sudo scripts/uni-day.sh become 192.0.2.20        # <ip-the-app-dials> (example IP)
#      -> gives THIS machine that exact IP (alias) + runs the full local stack on :8009,
#         so the app dialing that IP reaches you.
#
#   (no args) -> probe DCVR_ROOMSERVER_HOST + print what to do.
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IFACE="${DCVR_IFACE:-$(ip -o route show default 2>/dev/null | awk '{print $5; exit}')}"

probe() {
  local host="$1"
  for p in 8009 8005 8010; do
    if timeout 4 bash -c "cat < /dev/null > /dev/tcp/$host/$p" 2>/dev/null; then
      echo "  $host:$p  REACHABLE  <-- live RoomServer"
    else
      echo "  $host:$p  closed"
    fi
  done
}

case "${1:-probe}" in
  probe)
    HOST="${DCVR_ROOMSERVER_HOST:-${2:-}}"
    if [ -z "$HOST" ]; then
      echo "== Set DCVR_ROOMSERVER_HOST (or pass a host) to probe a RoomServer =="
      echo "   e.g.  DCVR_ROOMSERVER_HOST=192.0.2.10 scripts/uni-day.sh probe"
      exit 0
    fi
    echo "== Probing RoomServer host $HOST from THIS network =="
    probe "$HOST"
    echo
    echo "If a port is REACHABLE:  scripts/uni-day.sh remote $HOST <that-port>"
    echo "If you must answer at a fixed IP:  sudo scripts/uni-day.sh become <ip>"
    ;;

  discover)
    # Find the IP the installed app dials WITHOUT adb: on the SAME wifi, the headset must
    # ARP-resolve that IP before connecting, and ARP requests are BROADCAST — so this laptop
    # sees "who-has <ip>" from the headset. Works when the target is on this subnet (the
    # remote-host case). Run this, then OPEN THE GENIE APP on the headset.
    if [ "$(id -u)" -ne 0 ]; then echo "needs root: sudo $0 discover" >&2; exit 1; fi
    command -v tcpdump >/dev/null 2>&1 || { echo "install tcpdump first: sudo apt install tcpdump" >&2; exit 1; }
    echo "== Listening for the headset's ARP requests on $IFACE (60s) =="
    echo "   >>> NOW open the genie app on the headset. Watch the 'who-has' lines: <<<"
    echo "   the IP after 'who-has' that ISN'T the router is the server the app dials."
    timeout 60 tcpdump -ni "$IFACE" -l arp 2>/dev/null | grep --line-buffered 'who-has'
    echo "== done. Take that IP -> 'remote <ip> <port>' or 'sudo become <ip>'. =="
    ;;

  remote)
    HOST="${2:?usage: uni-day.sh remote <ip> <port>}"
    PORT="${3:-8009}"
    echo "== Pointing the Rust backend at the remote RoomServer $HOST:$PORT =="
    cd "$HERE"
    if [ -f .env ]; then set -a; . ./.env; set +a; fi
    export DCVR_UBIQ_ADDR="$HOST:$PORT" DCVR_MODE=baseline  \
           DCVR_STT_OPENAI=true DCVR_ADMIN_PORT=7878
    echo "  llm=$([ -n "${OPENAI_API_KEY:-}" ] && echo OpenAI || echo mock)  ubiq=$DCVR_UBIQ_ADDR"
    echo "  Now open the genie app on the headset (same wifi). Ctrl-C to stop."
    cargo build -q -p dreamcodevr-server && exec ./target/debug/dreamcodevr-server
    ;;

  become)
    IP="${2:?usage: sudo uni-day.sh become <ip-the-app-dials>}"
    if [ "$(id -u)" -ne 0 ]; then echo "needs root: sudo $0 become $IP" >&2; exit 1; fi
    echo "== Making $IFACE also answer at $IP (so the app's baked IP reaches this laptop) =="
    # Match the interface's prefix length if we can; default /24.
    CIDR="$(ip -o -4 addr show "$IFACE" 2>/dev/null | awk '{print $4}' | head -1)"
    LEN="${CIDR##*/}"; [ -n "$LEN" ] && [ "$LEN" -ge 1 ] 2>/dev/null || LEN=24
    ip addr add "$IP/$LEN" dev "$IFACE" 2>/dev/null \
      && echo "  added $IP/$LEN to $IFACE" \
      || echo "  ($IP already present or add failed — continuing)"
    if command -v ufw >/dev/null 2>&1; then
      ufw allow 8009/tcp >/dev/null 2>&1 || true   # RoomServer
      ufw allow 8987/udp >/dev/null 2>&1 || true   # auto-discovery
      ufw allow 7878/tcp >/dev/null 2>&1 || true   # admin panel (phone / Quest browser)
    fi
    echo "  Now run the stack as your normal user:  scripts/start-all.sh"
    echo "  Then open the genie app on the headset — it dials $IP -> this laptop."
    ;;

  *)
    echo "usage: uni-day.sh [probe | remote <ip> <port> | (sudo) become <ip>]" >&2
    exit 2
    ;;
esac
