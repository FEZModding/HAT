#!/bin/bash

# Move to script's directory
cd "`dirname "$0"`"

# Copy all files from HATDependencies for the duration of patching
temp_files=()
while IFS= read -r -d '' file; do
    cp "$file" . && copied_files+=("$(basename "$file")")
done < <(find HATDependencies -type f -print0)

# Patching
mono MonoMod.exe FEZ.exe

# Cleanup
for f in "${copied_files[@]}"; do
    rm -f -- "$f"
done
