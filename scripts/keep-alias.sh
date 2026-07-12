#!/usr/bin/env bash
# Keep a fixed LAN IP alias alive on this machine so a headset app that dials a
# FIXED IP:port always reaches this backend — even after the Wi-Fi reconnects and
# NetworkManager flushes manually-added addresses.
#
# The alias is re-added whenever it goes missing, and firewall ports are ensured.
# The address is environment-specific: supply YOUR LAN IP (nothing is baked in).
#
#   sudo DCVR_ALIAS_IP=192.0.2.10 scripts/keep-alias.sh          # example IP only
#   sudo scripts/keep-alias.sh 192.0.2.10 eth0 24               # <lan-ip> [iface] [prefixlen]
#
# Leave it running in a terminal (Ctrl-C to stop). Safe to re-run.
set -u
IP="${1:-${DCVR_ALIAS_IP:-}}"
IFACE="${2:-${DCVR_ALIAS_IFACE:-$(ip -o route show default 2>/dev/null | awk '{print $5; exit}')}}"
LEN="${3:-24}"

if [ -z "$IP" ]; then
  echo "usage: sudo DCVR_ALIAS_IP=<lan-ip> $0    (or: sudo $0 <lan-ip> [iface] [prefixlen])" >&2
  echo "  No LAN IP is baked in; supply the address reachable from your headset/client." >&2
  exit 2
fi
if [ -z "$IFACE" ]; then
  echo "could not auto-detect a network interface; pass one: sudo $0 $IP <iface>" >&2
  exit 2
fi
if [ "$(id -u)" -ne 0 ]; then
  echo "needs root:  sudo DCVR_ALIAS_IP=$IP $0" >&2
  exit 1
fi

# Open firewall once (best-effort).
if command -v ufw >/dev/null 2>&1; then
  ufw allow 8009/tcp >/dev/null 2>&1 || true
  ufw allow 8987/udp >/dev/null 2>&1 || true
  ufw allow 7878/tcp >/dev/null 2>&1 || true
fi

echo "[keep-alias] holding $IP/$LEN on $IFACE — the genie app will always reach this laptop."
echo "[keep-alias] Ctrl-C to stop."
while true; do
  if ! ip -o -4 addr show "$IFACE" 2>/dev/null | grep -q " ${IP}/"; then
    if ip addr add "$IP/$LEN" dev "$IFACE" 2>/dev/null; then
      echo "[keep-alias] $(date +%H:%M:%S)  re-added $IP (had dropped)."
    fi
  fi
  sleep 5
done
