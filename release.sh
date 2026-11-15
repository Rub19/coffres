#!/usr/bin/env bash
# Usage: bash release.sh "notes fr" "notes en" "notes es"
set -euo pipefail
W="$(cd "$(dirname "$0")" && pwd)"
export PATH="/c/Program Files/dotnet:$PATH"
bash "$W/build.sh"
dotnet publish "$W/app/Coffres.csproj" -c Release -o "$W/dist" | grep -E " error |-> .*dist" || true
cp "$W/dist/Coffres.exe" "$W/site/Coffres.exe"
VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$W/app/Coffres.csproj")
python - "$W/site" "$VERSION" "${1:-}" "${2:-}" "${3:-}" <<'EOF'
import hashlib, json, sys
site, version, fr, en, es = sys.argv[1:6]
sha = hashlib.sha256(open(site + '/Coffres.exe', 'rb').read()).hexdigest()
json.dump({'version': version, 'url': 'Coffres.exe', 'sha256': sha, 'notes': {'fr': fr, 'en': en, 'es': es}}, open(site + '/version.json', 'w', encoding='utf-8'), ensure_ascii=False)
print('version.json', version, sha[:12])
EOF
cd "$W" && npx wrangler deploy | tail -3
