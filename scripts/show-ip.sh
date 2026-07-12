#!/usr/bin/env bash
# Show the laptop's LAN IP(s) so you can point the Quest app at this machine.
#
# The Ubiq RoomServer listens on TCP 8009 on every interface, so on the SAME Wi-Fi
# (e.g. an iPhone Personal Hotspot) the headset connects to  <laptop-ip>:8009 .
# On a hotspot the laptop's address is almost always in the  172.20.10.x  range and
# it can change every session, so run this each time and type the shown value into
# the app's "Laptop server IP" field.
set -u

PORT=8009

# Collect non-loopback IPv4 addresses (iproute2 first, fall back to hostname -I).
ips=""
if command -v ip >/dev/null 2>&1; then
  ips=$(ip -4 -o addr show scope global 2>/dev/null | awk '{print $4}' | cut -d/ -f1)
fi
if [ -z "$ips" ] && command -v hostname >/dev/null 2>&1; then
  ips=$(hostname -I 2>/dev/null | tr ' ' '\n' | grep -E '^[0-9]+\.' )
fi

if [ -z "$ips" ]; then
  echo "Could not detect a LAN IP. Are you connected to the hotspot/Wi-Fi?" >&2
  exit 1
fi

# Pick the best candidate: prefer an iPhone-hotspot 172.20.10.x, else the first one.
best=""
for ip in $ips; do
  case "$ip" in
    172.20.10.*) best="$ip"; break ;;
  esac
done
[ -z "$best" ] && best=$(echo "$ips" | head -n1)

echo "================ point the Quest here ================"
echo "  Laptop server IP :  $best"
echo "  Port             :  $PORT   (leave as-is in the app)"
echo
echo "  In the headset app, type  $best  into \"Laptop server IP\""
echo "  then press \"Apply & Reconnect\"."
echo
if echo "$ips" | grep -q '^172\.20\.10\.'; then
  echo "  (172.20.10.x detected = iPhone hotspot — good.)"
fi
echo "  All addresses on this laptop:"
for ip in $ips; do echo "    - $ip"; done
echo "======================================================"
