#!/usr/bin/env bash
# Uploads a freshly built Linux server and restarts it. Run from your Mac.
#
#   ./deploy.sh root@YOUR_SERVER_IP
#
# Sends the build as a tar stream over ssh rather than with rsync. rsync needs a
# matching rsync on the far end and opens its own channel, and when that channel
# closes early all you get is "unexpected end of file" with nothing saying why.
# tar over ssh needs only ssh, asks for the password exactly once, and either
# lands whole or does not land at all.
set -euo pipefail

TARGET="${1:-}"
if [[ -z "$TARGET" ]]; then
    echo "usage: ./deploy.sh user@server-ip" >&2
    exit 1
fi

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD="$(cd "$HERE/../../Builds/LinuxServer" 2>/dev/null && pwd || true)"

if [[ -z "$BUILD" || ! -x "$BUILD/TriviaDuelServer" ]]; then
    echo "No build found at Builds/LinuxServer/TriviaDuelServer." >&2
    echo "In Unity: Trivia Duel > Build Linux Server, then run this again." >&2
    exit 1
fi

echo "==> Uploading $(du -sh "$BUILD" | cut -f1) from $BUILD"
echo "    (one password prompt, then it goes quiet for a minute while it copies)"

# Unpacked beside the live copy and swapped into place only once it is complete,
# so a connection that drops halfway leaves the running server untouched rather
# than half-overwritten. The previous build stays as .old for one deploy.
tar czf - -C "$BUILD" . | ssh "$TARGET" 'set -e
    rm -rf /opt/triviaduel.new
    mkdir -p /opt/triviaduel.new
    tar xzf - -C /opt/triviaduel.new
    chmod +x /opt/triviaduel.new/TriviaDuelServer
    chown -R trivia:trivia /opt/triviaduel.new

    echo "==> Swapping it in"
    rm -rf /opt/triviaduel.old
    [ -d /opt/triviaduel ] && mv /opt/triviaduel /opt/triviaduel.old
    mv /opt/triviaduel.new /opt/triviaduel

    echo "==> Restarting"
    systemctl restart triviaduel
    sleep 2
    systemctl --no-pager --lines=15 status triviaduel'

echo
echo "==> Live log:  ssh $TARGET 'tail -f /var/log/triviaduel/server.log'"
