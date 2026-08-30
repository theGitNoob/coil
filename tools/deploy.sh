#!/usr/bin/env bash
#
# COIL — build, export, install, launch and tail. Roadmap M0-03, D-07.
#
# The one command in the definition of done (CONVENTIONS §6.1): a task is not
# finished until this has put it on the handset.
#
#   ./tools/deploy.sh              debug build, install, launch, tail logcat
#   ./tools/deploy.sh --all        same, but do not filter logcat
#   ./tools/deploy.sh --no-tail    stop after launching
#
# Environment, all overridable:
#   ANDROID_SDK  default ~/Android/Sdk   — there is no `adb` on PATH
#   GODOT        default godot-mono      — the binary is not called `godot`
#   PRESET       default Android
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ANDROID_SDK="${ANDROID_SDK:-$HOME/Android/Sdk}"
ADB="$ANDROID_SDK/platform-tools/adb"
GODOT="${GODOT:-godot-mono}"
PRESET="${PRESET:-Android}"

PACKAGE="com.rafaelacosta.coil"
ACTIVITY="com.godot.game.GodotApp"
APK="$ROOT/build/coil-arm64.apk"

tail_logcat=1
filter=1
for arg in "$@"; do
    case "$arg" in
        --all)     filter=0 ;;
        --no-tail) tail_logcat=0 ;;
        *) echo "deploy: unknown option '$arg'" >&2; exit 2 ;;
    esac
done

say()  { printf '\n\033[1m▸ %s\033[0m\n' "$1"; }
die()  { printf '\033[31mdeploy: %s\033[0m\n' "$1" >&2; exit 1; }

# --- preflight ---------------------------------------------------------------
# Every check here has cost someone an hour at least once. Fail with the fix,
# not with a stack trace forty lines into a gradle log.

[ -x "$ADB" ] || die "no adb at $ADB — set ANDROID_SDK to the SDK root"
command -v "$GODOT" >/dev/null || die "no '$GODOT' on PATH — set GODOT to the engine binary"

# A cold `adb devices` starts the daemon and returns before it has finished
# enumerating USB, so the first read can be empty on a perfectly healthy device.
# Start the daemon explicitly, then poll.
"$ADB" start-server >/dev/null 2>&1 || die "adb could not start its daemon"

devices=""
for _ in $(seq 1 10); do
    devices="$("$ADB" devices | awk 'NR>1 && $2=="device" {print $1}')"
    [ -n "$devices" ] && break
    sleep 0.5
done

count="$(printf '%s' "$devices" | grep -c . || true)"
if [ "$count" -ne 1 ]; then
    # Distinguish the three states, because each has a different fix.
    unauth="$("$ADB" devices | awk 'NR>1 && $2=="unauthorized" {print $1}' | wc -l)"
    offline="$("$ADB" devices | awk 'NR>1 && $2=="offline" {print $1}' | wc -l)"
    [ "$unauth" -gt 0 ] && die "device attached but unauthorised — accept the USB-debugging prompt on the phone"
    [ "$offline" -gt 0 ] && die "device attached but offline — unplug and replug it"
    die "need exactly one authorised device, found $count. Check the cable, that the phone is unlocked, and that USB debugging is on ('$ADB' devices)"
fi

abi="$("$ADB" shell getprop ro.product.cpu.abi | tr -d '\r')"
case "$abi" in
    arm64*) ;;
    # The preset ships arm64-v8a only — D-17 accepts the .NET runtime's ~20 MB
    # on that condition. An APK installed on another ABI would fail at load.
    *) die "device reports ABI '$abi'; this preset exports arm64-v8a only" ;;
esac

say "device $devices  ($("$ADB" shell getprop ro.product.model | tr -d '\r'), $abi, API $("$ADB" shell getprop ro.build.version.sdk | tr -d '\r'))"

# --- build -------------------------------------------------------------------
# The export runs its own publish; this is here because `dotnet build` reports
# C# errors readably and the export reports them as a failed export.

say "build"
dotnet build "$ROOT/Coil.sln" --nologo -v minimal

# --- export ------------------------------------------------------------------

say "export ($PRESET → ${APK#"$ROOT"/})"
mkdir -p "$(dirname "$APK")"

# The preset builds through gradle, which is not a preference: the prebuilt
# template hardcodes a required TFM of net9.0 and would reject ARCH §2's net8.0
# outright (godotengine/godot#102627). A gradle build skips that check entirely
# and sources its .jar dependencies from our own build output instead.
#
# The template is a one-time, several-minute download that gradle then caches.
# It lives in android/, which .gitignore already excludes.
if [ ! -d "$ROOT/android/build" ]; then
    say "installing the Android build template (one time, several minutes)"
    "$GODOT" --headless --path "$ROOT" --install-android-build-template \
        --export-debug "$PRESET" "$APK" || die "build template install failed"
else
    "$GODOT" --headless --path "$ROOT" --export-debug "$PRESET" "$APK" \
        || die "export failed — see the errors above"
fi
[ -f "$APK" ] || die "export reported success but produced no APK at $APK"

# --- install and launch ------------------------------------------------------

say "install ($(du -h "$APK" | cut -f1))"
"$ADB" install -r "$APK"

say "launch"
"$ADB" logcat -c
# Through the launcher intent, not `am start -n`: Godot's activity is not
# android:exported, so naming it directly is a SecurityException from the shell.
"$ADB" shell monkey -p "$PACKAGE" -c android.intent.category.LAUNCHER 1 >/dev/null

[ "$tail_logcat" -eq 1 ] || exit 0

# --- tail --------------------------------------------------------------------
# Filtered by tag: GD.Print lands on `godot`, a managed exception on
# `AndroidRuntime` or `DOTNET`, a native crash on `DEBUG`. Everything else is
# silenced, which is what makes a print from the game findable at all.

say "logcat (ctrl-c to stop)"
if [ "$filter" -eq 1 ]; then
    exec "$ADB" logcat -v brief \
        godot:V GodotSharp:V DOTNET:V mono:V AndroidRuntime:E DEBUG:E '*:S'
else
    exec "$ADB" logcat -v brief
fi
