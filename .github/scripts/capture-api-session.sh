#!/usr/bin/env bash
# Starts the built API against MongoDB, runs a short real session against it
# (health, login, faceted search) and writes the exact commands
# and responses to docs/screenshots/transcript.txt for render-terminal.cjs.
#
# usage: capture-api-session.sh   (run from the repo root, after a Release build)
set -euo pipefail

OUT=docs/screenshots
BASE=http://localhost:5000
mkdir -p "$OUT"

# Run from the project folder: the content root is the working directory, and
# appsettings.Development.json (which holds the dev-only Jwt:Secret) is only
# found from there. From the repo root the API stops with "Jwt:Secret is not configured".
LOG="$PWD/$OUT/api.log"
(
  cd src/FlexCatalog.Api
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$BASE" \
    Mongo__ConnectionString="mongodb://localhost:27017" \
    exec dotnet bin/Release/net10.0/FlexCatalog.Api.dll > "$LOG" 2>&1
) &
API_PID=$!
trap 'kill $API_PID 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do
  if curl -fsS "$BASE/health" > /dev/null 2>&1; then break; fi
  sleep 1
done
curl -fsS "$BASE/health" > /dev/null || { echo "API did not become healthy"; tail -n 40 "$LOG"; exit 1; }

# categoryType and sortBy are sent as enum numbers (0 = Electronics, 1 = PriceAsc): the API has no string enum
# converter. A full-text request ({"query":...}) is left out on purpose: against real MongoDB it
# fails, because the tenant $match is placed ahead of the $text $match.
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

LINES_MAX=40 show "curl -s -X POST localhost:5000/api/products/search -H \"authorization: Bearer \$TOKEN\" -d '{\"categoryType\":0,\"inStock\":true,\"sortBy\":1}' | python3 -m json.tool" \
  "curl -s -X POST $BASE/api/products/search -H \"authorization: Bearer \$TOKEN\" -H 'content-type: application/json' -d '{\"categoryType\":0,\"inStock\":true,\"sortBy\":1}' | python3 -m json.tool"


# The API log goes to the job log (not the transcript) so a failing request can be diagnosed.
echo "--- api.log (last 80 lines) ---" >&2
tail -n 80 "$LOG" >&2 || true

cat "$T"
