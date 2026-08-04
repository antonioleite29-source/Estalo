#!/bin/bash
# Boots the Android test phones and puts the newest APK on each one.
#
#   ./Tools/phones.sh          boot all four and install
#   ./Tools/phones.sh 2        boot only two of them
#   ./Tools/phones.sh 4 skip   boot four, don't reinstall the APK
#
# Each emulator is behind its own NAT, so they cannot see each other or your Wi-Fi. Inside an
# emulator your Mac is always 10.0.2.2 — that is the address to type on the Connect page. Wi-Fi
# auto-discovery does not work here for the same reason; only a real phone can test that.

set -u

SDK=/Applications/Unity/Hub/Editor/6000.4.12f1/PlaybackEngines/AndroidPlayer/SDK
ADB="$SDK/platform-tools/adb"
EMULATOR="$SDK/emulator/emulator"
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

COUNT="${1:-4}"
INSTALL="${2:-install}"

APK=$(ls -t "$PROJECT_DIR"/Builds/*.apk 2>/dev/null | head -1)

if [ "$INSTALL" != "skip" ] && [ -z "$APK" ]; then
    echo "No APK in Builds/. Run 'Trivia Duel > Build Android APK' in Unity first."
    exit 1
fi

echo "Booting $COUNT phone(s)..."

for i in $(seq 1 "$COUNT"); do
    # The first AVD has no numeric suffix.
    if [ "$i" = "1" ]; then NAME="TriviaPhone"; else NAME="TriviaPhone$i"; fi

    # Deliberately NOT -read-only. That flag runs the phone from a throwaway copy and discards
    # everything on exit — profile name, avatar, saved IQ — so testers' phones forgot who they were
    # every restart. It is not needed here: the emulator lock is per-AVD, and these are four
    # separate AVDs with their own data directories, so they run side by side regardless.
    "$EMULATOR" -avd "$NAME" -no-boot-anim >/dev/null 2>&1 &
    echo "  starting $NAME"
done

echo "Waiting for them to finish booting (first boot is slow, later ones use a snapshot)..."

for i in $(seq 1 "$COUNT"); do
    SERIAL="emulator-$((5554 + (i - 1) * 2))"

    until [ "$("$ADB" -s "$SERIAL" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do
        sleep 3
    done

    echo "  $SERIAL ready"
done

if [ "$INSTALL" = "skip" ]; then
    echo "Skipping install."
else
    echo "Installing $(basename "$APK")..."

    for i in $(seq 1 "$COUNT"); do
        SERIAL="emulator-$((5554 + (i - 1) * 2))"
        "$ADB" -s "$SERIAL" install -r "$APK" >/dev/null 2>&1 && echo "  installed on $SERIAL"
    done
fi

echo "Launching the game on each..."

for i in $(seq 1 "$COUNT"); do
    SERIAL="emulator-$((5554 + (i - 1) * 2))"

    # Force-stop first. A phone restored from a snapshot brings back the app that was running when
    # it was closed — the OLD build, already in memory. Installing a new APK replaces the file but
    # not that process, so without this the phone keeps showing old behaviour and the new build
    # looks broken.
    "$ADB" -s "$SERIAL" shell am force-stop com.tomdeleite.triviaduel >/dev/null 2>&1

    "$ADB" -s "$SERIAL" shell am start -n \
        com.tomdeleite.triviaduel/com.unity3d.player.UnityPlayerGameActivity >/dev/null 2>&1
done

echo
echo "Done. On each phone, type 10.0.2.2 on the Connect page to reach your Mac."
echo "Press Play in Unity first so there is a host to join."
echo
echo "Live log from phone 1:   $ADB -s emulator-5554 logcat -s Unity"
