#!/usr/bin/env bash
# Builds the libfuzzer-dotnet driver (https://github.com/Metalnem/libfuzzer-dotnet) from its source at a pinned commit, after
# checking the source's SHA-256, into artifacts/fuzz/libfuzzer-dotnet. Linux only: it needs clang with libFuzzer
# (Ubuntu: apt install clang libclang-rt-dev). Building from source rather than downloading the release binary keeps the
# driver auditable and lets it run on arm64 too. Override the compiler with CXX.
set -euo pipefail

commit=bd39d4e88d715ab460a929943645be2a186cde52
sha256=90f019e2e9ad3a0b93c7ecc2c5afb2fbfc8b5aab6aac51c7e0d349ec79354f36
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="${root}/artifacts/fuzz"
mkdir -p "${out}"

curl --fail --silent --show-error --location \
  --output "${out}/libfuzzer-dotnet.cc" \
  "https://raw.githubusercontent.com/Metalnem/libfuzzer-dotnet/${commit}/libfuzzer-dotnet.cc"
echo "${sha256}  ${out}/libfuzzer-dotnet.cc" | sha256sum --check --strict

# The driver reports a .NET exception with __builtin_trap. That is SIGILL on x86-64, which libFuzzer catches and saves the input
# for, but SIGTRAP on arm64, which libFuzzer does not handle (the run dies and the input is lost); calling abort instead raises
# SIGABRT, which libFuzzer handles everywhere.
"${CXX:-clang++}" -O2 -fsanitize=fuzzer -ftrap-function=abort "${out}/libfuzzer-dotnet.cc" -o "${out}/libfuzzer-dotnet"
echo "built ${out}/libfuzzer-dotnet"
