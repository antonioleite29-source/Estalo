#!/usr/bin/env bash
# Uploads a freshly built Linux server and restarts it. Run from your Mac.
#
#   ./deploy.sh root@YOUR_SERVER_IP
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
# --delete so a file removed from the build is removed on the server too. Without it, leftovers
# from an older build sit in _Data alongside the new ones and Unity loads whichever it finds first.
rsync -az --delete "$BUILD/" "$TARGET:/opt/triviaduel/"

echo "==> Fixing ownership and permissions"
ssh "$TARGET" 'chmod +x /opt/triviaduel/TriviaDuelServer && chown -R trivia:trivia /opt/triviaduel'

echo "==> Restarting"
ssh "$TARGET" 'systemctl restart triviaduel && sleep 2 && systemctl --no-pager --lines=15 status triviaduel'

echo
echo "==> Live log:  ssh $TARGET 'tail -f /var/log/triviaduel/server.log'"
