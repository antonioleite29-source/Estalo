#!/usr/bin/env bash
# Answers one question: is the server running the build sitting in Builds/LinuxServer?
#
#   ./verify.sh root@YOUR_SERVER_IP
#
# Compares the checksum of the game code itself, not a timestamp, because a file
# copied by any means keeps its own contents but rarely its date.
set -euo pipefail

TARGET="${1:-}"
if [[ -z "$TARGET" ]]; then
    echo "usage: ./verify.sh user@server-ip" >&2
    exit 1
fi

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DLL="$HERE/../../Builds/LinuxServer/TriviaDuelServer_Data/Managed/Assembly-CSharp.dll"

if [[ ! -f "$DLL" ]]; then
    echo "No local build. In Unity: Trivia Duel > Build Linux Server." >&2
    exit 1
fi

LOCAL="$(shasum -a 256 "$DLL" | cut -d' ' -f1)"
echo "local:  $LOCAL"

REMOTE="$(ssh "$TARGET" 'sha256sum /opt/triviaduel/TriviaDuelServer_Data/Managed/Assembly-CSharp.dll 2>/dev/null | cut -d" " -f1')"
echo "server: ${REMOTE:-<not found>}"
echo

if [[ "$LOCAL" == "$REMOTE" ]]; then
    echo "MATCH — the server is running this build."
else
    echo "MISMATCH — the server is running something else. Run deploy.sh."
    exit 1
fi
