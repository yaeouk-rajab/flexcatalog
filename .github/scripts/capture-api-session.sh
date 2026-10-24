#!/usr/bin/env bash
# Starts the built API against MongoDB, runs a short real session against it
# (health, login, faceted and full-text search) and writes the exact commands
# and responses to docs/screenshots/transcript.txt for render-terminal.cjs.
#
# usage: capture-api-session.sh   (run from the repo root, after a Release build)
set -euo pipefail

OUT=docs/screenshots
BASE=http://localhost:5000
mkdir -p "$OUT"

ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$BASE" \
  Mongo__ConnectionString="mongodb://localhost:27017" \
  dotnet src/FlexCatalog.Api/bin/Release/net10.0/FlexCatalog.Api.dll > "$OUT/api.log" 2>&1 &
API_PID=$!
trap 'kill $API_PID 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do
  if curl -fsS "$BASE/health" > /dev/null 2>&1; then break; fi
  sleep 1
done
curl -fsS "$BASE/health" > /dev/null || { echo "API did not become healthy"; tail -n 40 "$OUT/api.log"; exit 1; }

T="$OUT/transcript.txt"
: > "$T"

show() { # show <command text to display> <command to run>
  printf '$ %s\n' "$1" >> "$T"
  eval "$2" 2>&1 | head -n "${LINES_MAX:-14}" | cut -c1-200 >> "$T"
  printf '\n' >> "$T"
}

TOKEN=$(curl -fsS -X POST "$BASE/api/auth/login" -H 'content-type: application/json' \
  -d '{"username":"admin@acme.test","password":"Passw0rd!"}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])')
export TOKEN

show 'curl -s localhost:5000/health' "curl -s $BASE/health"

printf '$ %s\n' "curl -s -X POST localhost:5000/api/auth/login -d '{\"username\":\"admin@acme.test\",\"password\":\"…\"}'" >> "$T"
curl -fsS -X POST "$BASE/api/auth/login" -H 'content-type: application/json' \
  -d '{"username":"admin@acme.test","password":"Passw0rd!"}' \
  | python3 -c 'import sys,json;d=json.load(sys.stdin);d["token"]=d["token"][:20]+"…";print(json.dumps(d,ensure_ascii=False))' >> "$T"
printf '\n' >> "$T"

LINES_MAX=40 show "curl -s -X POST localhost:5000/api/products/search -H \"authorization: Bearer \$TOKEN\" -d '{\"categoryType\":\"Electronics\",\"inStock\":true,\"sortBy\":\"PriceAsc\"}' | python3 -m json.tool" \
  "curl -s -X POST $BASE/api/products/search -H \"authorization: Bearer \$TOKEN\" -H 'content-type: application/json' -d '{\"categoryType\":\"Electronics\",\"inStock\":true,\"sortBy\":\"PriceAsc\"}' | python3 -m json.tool"

LINES_MAX=24 show "curl -s -X POST localhost:5000/api/products/search -H \"authorization: Bearer \$TOKEN\" -d '{\"query\":\"wireless\"}' | python3 -m json.tool" \
  "curl -s -X POST $BASE/api/products/search -H \"authorization: Bearer \$TOKEN\" -H 'content-type: application/json' -d '{\"query\":\"wireless\"}' | python3 -m json.tool"

cat "$T"
