#!/usr/bin/env bash
# Installs the sample's APK on the connected emulator or device, starts the smoke run (the intent extra "smoke") and waits for its
# verdict in logcat: BROADSIDE_SMOKE_OK passes; BROADSIDE_SMOKE_FAILED, a native abort, or the process dying first fails (#368).
# Usage: smoke-android.sh <path to .apk> [timeout in seconds, default 300]
set -euo pipefail

apk=${1:?usage: smoke-android.sh <path to .apk> [timeout in seconds]}
timeout=${2:-300}
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

dump_log() {
  echo "--- logcat (runtime, crash and smoke lines) ---"
  adb logcat -d | grep -E "BROADSIDE_SMOKE|monodroid|DOTNET|AndroidRuntime|libc|DEBUG|$package" | tail -n 200 || true
}

adb wait-for-device
adb install -r "$apk"
adb logcat -c
start=$SECONDS
adb shell am start -W -n "$package/.MainActivity" --ez smoke true
# pidof fails when the process is already gone; let the loop below report that with the log instead of set -e aborting here.
pid=$(adb shell pidof "$package" | tr -d '\r' || true)

while (( SECONDS - start < timeout )); do
  # adb can fail for a moment (exit 255) while the device is busy; poll again rather than let set -e end the run.
  log=$(adb logcat -d || true)
  if grep -q "BROADSIDE_SMOKE_OK" <<<"$log"; then
    grep -m 1 "BROADSIDE_SMOKE_OK" <<<"$log"
    echo "Smoke run passed in $(( SECONDS - start )) s."
    exit 0
  fi

  # monodroid only logs for .NET apps, and this emulator runs one; a fatal signal counts only in the app's process.
  if grep -qE "BROADSIDE_SMOKE_FAILED|F monodroid" <<<"$log" || { [[ -n "$pid" ]] && grep -qE "Fatal signal.* pid $pid " <<<"$log"; }; then
    echo "Smoke run failed after $(( SECONDS - start )) s."
    dump_log
    exit 1
  fi

  set +e; app_state; state=$?; set -e
  if (( state == 1 )); then
    echo "The app exited after $(( SECONDS - start )) s without a smoke verdict."
    dump_log
    exit 1
  fi

  sleep 2
done

echo "No smoke verdict within $timeout s."
dump_log
exit 1
