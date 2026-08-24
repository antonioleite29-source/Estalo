#!/usr/bin/env bash
# First-time setup for a fresh Ubuntu box. Run once, on the server, as root.
#
#   scp setup-server.sh root@YOUR_SERVER_IP:/root/
#   ssh root@YOUR_SERVER_IP 'bash /root/setup-server.sh'
set -euo pipefail

PORT="${PORT:-7777}"

echo "==> Creating the trivia user"
# A dedicated unprivileged user. The server is exposed to the internet, so anything it can reach
# is what an attacker who finds a hole in it can reach -- which should be as close to nothing as
# possible. --system means no login shell and no home directory to leave lying around.
id -u trivia >/dev/null 2>&1 || adduser --system --group --no-create-home trivia

echo "==> Creating directories"
mkdir -p /opt/triviaduel /var/log/triviaduel
chown -R trivia:trivia /opt/triviaduel /var/log/triviaduel

echo "==> Installing what a Unity headless player needs"
apt-get update -qq
# Unity's Linux player links against these even with -nographics. A box installed from a minimal
# image has none of them, and the failure looks like the binary silently doing nothing.
apt-get install -y -qq ca-certificates libc6 libstdc++6 rsync >/dev/null

echo "==> Opening UDP $PORT"
# UDP, not TCP. Unity Transport is UDP; a TCP rule here is the single most common way to end up
# with a server that runs perfectly and refuses every connection without logging anything.
if command -v ufw >/dev/null 2>&1; then
    ufw allow OpenSSH >/dev/null 2>&1 || true
    ufw allow "${PORT}/udp"
    ufw --force enable
    ufw status verbose | sed 's/^/    /'
else
    echo "    ufw not installed; open UDP ${PORT} in your provider's firewall panel instead."
fi

echo
echo "==> Done. Still to do:"
echo "    1. Open UDP ${PORT} in your PROVIDER's firewall too (Vultr/AWS panel)."
echo "       The box firewall and the provider firewall are two separate walls."
echo "    2. Run deploy.sh from your Mac to upload the build."
