#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
TEST_DIR="$(mktemp -d "${TMPDIR:-/tmp}/bubu-diff-engine.XXXXXX")"
trap 'rm -rf "$TEST_DIR"' EXIT
swiftc NotchBuddy/Sources/BubuKit/DiffEngine.swift \
    tests/DiffEngineTests.swift -o "$TEST_DIR/diff-engine-tests"
"$TEST_DIR/diff-engine-tests"
