#!/usr/bin/env bash
set -euo pipefail
W="$(cd "$(dirname "$0")" && pwd)"
export PATH="/c/Program Files/dotnet:$PATH"
[ -f "$W/vendor/LICENSE" ] || git -C "$W" submodule update --init
git -C "$W/vendor" apply -R --check "$W/patches/browser-aes.patch" 2>/dev/null || git -C "$W/vendor" apply "$W/patches/browser-aes.patch"
/usr/bin/find "$W/build" -mindepth 1 -delete 2>/dev/null || true
dotnet publish "$W/wasm/ChestWasm.csproj" -c Release -o "$W/build" | grep -E " error |-> .*build" || true
/usr/bin/find "$W/site/_framework" -type f -delete 2>/dev/null || mkdir -p "$W/site/_framework"
cp "$W"/build/wwwroot/_framework/*.js "$W"/build/wwwroot/_framework/*.wasm "$W/site/_framework/"
echo "framework: $(ls "$W/site/_framework" | wc -l) files"
