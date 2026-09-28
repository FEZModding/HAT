#!/bin/bash
# FEZ macOS Launch Script
# Written by zerocker and FEZModding community <3
# Based on original script written by Ethan "flibitijibibo" Lee

cd "$(dirname "$0")" || exit 1
export DYLD_LIBRARY_PATH="${DYLD_LIBRARY_PATH:+$DYLD_LIBRARY_PATH:}."

# Steam preserves its overlay library here when macOS clears DYLD_INSERT_LIBRARIES.
if [ -n "${STEAM_DYLD_INSERT_LIBRARIES:-}" ] && [ -z "${DYLD_INSERT_LIBRARIES:-}" ]; then
    export DYLD_INSERT_LIBRARIES="$STEAM_DYLD_INSERT_LIBRARIES"
fi

exec ./HAT.bin.osx "$@"
