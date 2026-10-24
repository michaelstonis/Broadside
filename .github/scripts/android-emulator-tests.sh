#!/usr/bin/env bash
# Runs tests/Broadside.Rendering.Android.Tests against the emulator android.yml booted (issue #344).
#
# 1. Logs the runner's memory, CPUs and KVM, then waits until the emulator is attached and booted.
# 2. Runs the tests. The harness checks the device before every phase and retries transport-only adb steps itself.
# 3. Only when the run failed AND its output carries the harness's "adb transport lost" marker (the emulator's adb transport stayed
#    down through the harness's own retries) does it reconnect and run the tests once more. A real test failure never carries the
#    marker, so it is never retried.
# On failure it saves the emulator's logcat and the device list next to the TRX files.
set -uo pipefail

results=artifacts/test-results
adb="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}/platform-tools/adb"
command -v "$adb" >/dev/null 2>&1 || adb=adb
marker='adb transport lost'
mkdir -p "$results"

health() {
  echo "::group::Runner and device health ($1)"
  free -m
  nproc
  uptime
  ls -l /dev/kvm || true
  "$adb" devices -l
  echo "::endgroup::"
}

wait_ready() {
  "$adb" reconnect offline >/dev/null 2>&1 || true
  timeout 180 "$adb" wait-for-device || return 1
  for _ in $(seq 1 90); do
    if [ "$("$adb" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; then
      return 0
    fi
    sleep 2
  done
  return 1
}

run_tests() {
  dotnet test tests/Broadside.Rendering.Android.Tests -c Release --no-build --results-directory "$results" -- --report-xunit-trx \
    2>&1 | tee "$results/dotnet-test-$1.log"
  return "${PIPESTATUS[0]}"
}

save_diagnostics() {
  "$adb" devices -l > "$results/adb-devices-$1.txt" 2>&1 || true
  timeout 60 "$adb" logcat -d > "$results/logcat-$1.txt" 2>&1 || true
}

health before
if ! wait_ready; then
  echo "::error::The emulator was not attached and booted within the wait."
  save_diagnostics boot
  exit 1
fi

run_tests 1
status=$?
if [ "$status" -ne 0 ] && grep -q "$marker" "$results/dotnet-test-1.log"; then
  echo "::warning::The emulator's adb transport was lost during the run (issue #344); reconnecting and running the tests once more."
  save_diagnostics attempt-1
  health "after attempt 1"
  mkdir -p "$results/attempt-1"
  mv "$results"/*.trx "$results/android-adb.log" "$results/attempt-1/" 2>/dev/null || true
  if wait_ready; then
    run_tests 2
    status=$?
  fi
fi

if [ "$status" -ne 0 ]; then
  save_diagnostics failure
  health "after failure"
fi

exit "$status"
