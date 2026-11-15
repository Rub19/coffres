#!/usr/bin/env bash
# Usage: bash release.sh "notes fr" "notes en" "notes es"
set -euo pipefail
W="$(cd "$(dirname "$0")" && pwd)"
export PATH="/c/Program Files/dotnet:$PATH"
bash "$W/build.sh"
/usr/bin/find "$W/build-exe" -mindepth 1 -delete 2>/dev/null || true
dotnet publish "$W/app/Coffres.csproj" -c Release -o "$W/build-exe" -nologo -v:q
VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$W/app/Coffres.csproj")
grep -aq "$VERSION" "$W/build-exe/Coffres.exe" || { echo "build-exe/Coffres.exe is not $VERSION"; exit 1; }
cp "$W/build-exe/Coffres.exe" "$W/site/Coffres.exe"
cp "$W/build-exe/Coffres.exe" "$W/dist/Coffres.exe" 2>/dev/null || echo "dist/Coffres.exe is running: it will update itself"
python - "$W/site" "$VERSION" "${1:-}" "${2:-}" "${3:-}" <<'EOF'
import hashlib, json, sys
site, version, fr, en, es = sys.argv[1:6]
sha = hashlib.sha256(open(site + '/Coffres.exe', 'rb').read()).hexdigest()
json.dump({'version': version, 'url': 'Coffres.exe', 'sha256': sha, 'notes': {'fr': fr, 'en': en, 'es': es}}, open(site + '/version.json', 'w', encoding='utf-8'), ensure_ascii=False)
print('version.json', version, sha[:12])
EOF
cd "$W" && npx wrangler deploy | tail -3
