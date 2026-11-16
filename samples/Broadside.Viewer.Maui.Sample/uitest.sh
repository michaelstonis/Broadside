#!/usr/bin/env bash
# Runs the sample's UI tests (UiTests.cs, #358) and waits for their verdict: BROADSIDE_UITEST_OK passes; BROADSIDE_UITEST_FAILED, a
# native abort, the app exiting first or no verdict in time fails. Every BROADSIDE_UITEST line is printed.
#   uitest.sh android <path to .apk> [timeout s]   the connected emulator or device (adb), intent extra "uitest"
#   uitest.sh catalyst <path to .app> [timeout s]   runs the Mac Catalyst app with BROADSIDE_UITEST=1
#   uitest.sh ios <path to .app> [timeout s]        installs on the booted iOS simulator and launches it with SIMCTL_CHILD_BROADSIDE_UITEST=1
# CI runs it from .github/workflows/maui-ui.yml (Apple) and maui-android.yml (Android). It never boots a simulator or emulator itself.
set -euo pipefail

platform=${1:?usage: uitest.sh android|catalyst|ios <app or apk> [timeout in seconds]}
app=${2:?usage: uitest.sh android|catalyst|ios <app or apk> [timeout in seconds]}
timeout=${3:-900}
package=dev.broadside.viewer.sample

# Whether the app's process runs: 0 it runs, 1 it is gone, 2 adb could not tell (the device went offline for a moment; adb exits
# 255 then). A failed adb is never taken for an app that exited.
app_state() {
  local out rc
  out=$(adb shell pidof "$package" 2>/dev/null) && rc=0 || rc=$?
  out=${out//$'\r'/}
  if (( rc == 0 )) && [[ -n "$out" ]]; then return 0; fi
  if (( rc == 0 || rc == 1 )); then return 1; fi # older adb exits 0 with no output when pidof finds nothing
  adb wait-for-device || true
  return 2
}
log=$(mktemp -t broadside-uitest.XXXXXX)
start=$SECONDS

verdict() {
  # 0: passed, 1: failed, 2: no verdict yet
  if grep -q "BROADSIDE_UITEST_OK" "$log"; then return 0; fi
  if grep -qE "BROADSIDE_UITEST_FAILED|F monodroid|Unhandled managed exception" "$log"; then return 1; fi
  return 2
}

report() {
  grep -E "BROADSIDE_UITEST" "$log" || true
}

case "$platform" in
  android)
    adb wait-for-device
    adb install -r "$app"
    adb logcat -c
    adb shell am start -W -n "$package/.MainActivity" --ez uitest true
    while (( SECONDS - start < timeout )); do
      adb logcat -d > "$log" || true
      set +e; verdict; v=$?; set -e
      if (( v != 2 )); then
        report
        if (( v == 1 )); then
          adb logcat -d | grep -E "monodroid|DOTNET|AndroidRuntime|$package" | tail -n 200 || true
        fi
        exit $v
      fi
      set +e; app_state; state=$?; set -e
      if (( state == 1 )); then
        report
        echo "The app exited after $(( SECONDS - start )) s without a verdict."
        adb logcat -d | grep -E "monodroid|DOTNET|AndroidRuntime|libc|DEBUG|$package" | tail -n 200 || true
        exit 1
      fi
      sleep 3
    done
    ;;
  catalyst)
    executable=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app/Contents/Info.plist")
    BROADSIDE_UITEST=1 "$app/Contents/MacOS/$executable" > "$log" 2>&1 &
    pid=$!
    while (( SECONDS - start < timeout )); do
      set +e; verdict; v=$?; set -e
      if (( v != 2 )); then report; kill "$pid" 2>/dev/null || true; exit $v; fi
      if ! kill -0 "$pid" 2>/dev/null; then
        report
        echo "The app exited after $(( SECONDS - start )) s without a verdict."
        tail -n 100 "$log"
        exit 1
      fi
      sleep 2
    done
    kill "$pid" 2>/dev/null || true
    ;;
  ios)
    xcrun simctl install booted "$app"
    SIMCTL_CHILD_BROADSIDE_UITEST=1 xcrun simctl launch --console-pty --terminate-running-process booted "$package" > "$log" 2>&1 &
    pid=$!
    while (( SECONDS - start < timeout )); do
      set +e; verdict; v=$?; set -e
      if (( v != 2 )); then report; kill "$pid" 2>/dev/null || true; exit $v; fi
      if ! kill -0 "$pid" 2>/dev/null; then
        report
        echo "The app exited after $(( SECONDS - start )) s without a verdict."
        tail -n 100 "$log"
        exit 1
      fi
      sleep 2
    done
    kill "$pid" 2>/dev/null || true
    ;;
  *)
    echo "Unknown platform: $platform (android, catalyst or ios)"
    exit 2
    ;;
esac

report
echo "No UI test verdict within $timeout s."
tail -n 100 "$log"
exit 1
