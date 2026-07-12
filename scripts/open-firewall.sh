#!/usr/bin/env bash
# Allow the Quest to reach this laptop: inbound TCP 8009 (Ubiq RoomServer) and
# inbound UDP 8987 (DreamCodeVR+ auto-discovery probes from the headset).
#
# The RoomServer already listens on all interfaces; the only thing that can block the
# headset over Wi-Fi is a host firewall. This detects the active firewall (ufw / firewalld
# / iptables) and opens both ports. If no firewall is active, nothing is blocking.
#
# Needs root:   sudo scripts/open-firewall.sh
set -u

PORT=8009
DISC=8987

if [ "$(id -u)" -ne 0 ]; then
  echo "This needs root. Re-run:  sudo $0" >&2
  exit 1
fi

opened=0

# 1) ufw (Ubuntu default)
if command -v ufw >/dev/null 2>&1; then
  status=$(ufw status 2>/dev/null | head -n1)
  if echo "$status" | grep -qi 'active'; then
    ufw allow "$PORT"/tcp && echo "[ufw] allowed TCP $PORT" && opened=1
    ufw allow "$DISC"/udp && echo "[ufw] allowed UDP $DISC (auto-discovery)"
  else
    echo "[ufw] firewall is INACTIVE — nothing is blocking TCP $PORT / UDP $DISC."
    opened=1
  fi
fi

# 2) firewalld (Fedora/RHEL)
if [ "$opened" -eq 0 ] && command -v firewall-cmd >/dev/null 2>&1; then
  if firewall-cmd --state >/dev/null 2>&1; then
    firewall-cmd --add-port="$PORT"/tcp >/dev/null 2>&1 \
      && echo "[firewalld] allowed TCP $PORT (this session)" && opened=1
    firewall-cmd --permanent --add-port="$PORT"/tcp >/dev/null 2>&1 \
      && echo "[firewalld] allowed TCP $PORT (permanent)"
    firewall-cmd --add-port="$DISC"/udp >/dev/null 2>&1 \
      && echo "[firewalld] allowed UDP $DISC (auto-discovery)"
    firewall-cmd --permanent --add-port="$DISC"/udp >/dev/null 2>&1 || true
  else
    echo "[firewalld] not running — nothing is blocking TCP $PORT / UDP $DISC."
    opened=1
  fi
fi

# 3) raw iptables fallback
if [ "$opened" -eq 0 ] && command -v iptables >/dev/null 2>&1; then
  if iptables -C INPUT -p tcp --dport "$PORT" -j ACCEPT 2>/dev/null; then
    echo "[iptables] TCP $PORT already allowed."
  else
    iptables -I INPUT -p tcp --dport "$PORT" -j ACCEPT \
      && echo "[iptables] inserted ACCEPT for TCP $PORT"
  fi
  if iptables -C INPUT -p udp --dport "$DISC" -j ACCEPT 2>/dev/null; then
    echo "[iptables] UDP $DISC already allowed."
  else
    iptables -I INPUT -p udp --dport "$DISC" -j ACCEPT \
      && echo "[iptables] inserted ACCEPT for UDP $DISC (auto-discovery)"
  fi
  opened=1
fi

if [ "$opened" -eq 0 ]; then
  echo "No known firewall tool found (ufw/firewalld/iptables). If the headset still" >&2
  echo "can't connect, check your distro's firewall for inbound TCP $PORT." >&2
  exit 1
fi

echo "Done. The Quest should now reach the RoomServer on this laptop at :$PORT."
