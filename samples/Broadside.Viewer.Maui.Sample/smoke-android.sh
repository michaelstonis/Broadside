#!/usr/bin/env bash
# Installs the sample's APK on the connected emulator or device, starts the smoke run (the intent extra "smoke") and waits for its
# verdict in logcat: BROADSIDE_SMOKE_OK passes; BROADSIDE_SMOKE_FAILED, a native abort, or the process dying first fails (#368).
# Usage: smoke-android.sh <path to .apk> [timeout in seconds, default 300]
set -euo pipefail

apk=${1:?usage: smoke-android.sh <path to .apk> [timeout in seconds]}
timeout=${2:-300}
package=dev.broadside.viewer.sample

dump_log() {
  echo "--- logcat (runtime, crash and smoke lines) ---"
  adb logcat -d | grep -E "BROADSIDE_SMOKE|monodroid|DOTNET|AndroidRuntime|libc|DEBUG|$package" | tail -n 200 || true
}

adb wait-for-device
adb install -r "$apk"
adb logcat -c
start=$SECONDS
adb shell am start -W -n "$package/.MainActivity" --ez smoke true
pid=$(adb shell pidof "$package" | tr -d '\r')

while (( SECONDS - start < timeout )); do
  log=$(adb logcat -d)
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

  if [[ -z "$(adb shell pidof "$package" | tr -d '\r')" ]]; then
    echo "The app exited after $(( SECONDS - start )) s without a smoke verdict."
    dump_log
    exit 1
  fi

  sleep 2
done

echo "No smoke verdict within $timeout s."
dump_log
exit 1
