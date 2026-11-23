#!/usr/bin/env bash
# Smoke test for a deployed (or locally running) Claims Module (P2.5 task 17e).
#
#   scripts/smoke.sh <api-base-url> [frontend-url]
#   scripts/smoke.sh https://claimsdcs-api-xxxxxx.azurewebsites.net https://claimsdcs-web.azurestaticapps.net
#   scripts/smoke.sh http://localhost:5180 http://localhost:4200
#
# Needs only bash, curl and grep. Waits up to SMOKE_WAIT_SECONDS (default 300) for the API to come
# up, because App Service restarts after a deploy. Exits non-zero on the first failed check.
# Later phases extend it with their endpoints (incremental deployment rule, PLAN P3+).
set -euo pipefail

api="${1:?usage: smoke.sh <api-base-url> [frontend-url]}"
web="${2:-}"
api="${api%/}"
web="${web%/}"
wait_seconds="${SMOKE_WAIT_SECONDS:-300}"
failures=0

pass() { printf '  PASS  %s\n' "$1"; }
fail() { printf '  FAIL  %s\n' "$1"; failures=$((failures + 1)); }

# Prints the HTTP status, writes the body to $body_file.
body_file=$(mktemp)
trap 'rm -f "$body_file"' EXIT
# On a connection failure curl itself prints 000; the body file is truncated first so a failed
# request never reports the previous response's body.
fetch() {
  : > "$body_file"
  curl -sS -o "$body_file" -w '%{http_code}' --max-time 30 "$@" 2>/dev/null || true
}

echo "API: $api"
echo "== waiting for /health/live (up to ${wait_seconds}s)"
deadline=$((SECONDS + wait_seconds))
until [ "$(fetch "$api/health/live")" = 200 ]; do
  if [ $SECONDS -ge $deadline ]; then
    fail "/health/live did not return 200 within ${wait_seconds}s"
    exit 1
  fi
  sleep 10
done
pass "/health/live 200"

status=$(fetch "$api/health")
if [ "$status" = 200 ] && grep -q '"status":"Healthy"' "$body_file"; then
  pass "/health 200 Healthy"
else
  fail "/health returned $status: $(head -c 400 "$body_file")"
fi
if grep -q '"pending":0' "$body_file"; then
  pass "/health reports 0 pending migrations"
else
  fail "/health does not report 0 pending migrations"
fi

status=$(fetch "$api/swagger/v1/swagger.json")
if [ "$status" = 200 ] && grep -q 'Claims Module API' "$body_file"; then
  pass "Swagger document served"
else
  fail "Swagger document returned $status"
fi

if [ -n "$web" ]; then
  echo "Frontend: $web"

  status=$(fetch "$web/")
  if [ "$status" = 200 ] && grep -q '<app-root' "$body_file"; then
    pass "frontend index.html served"
  else
    fail "frontend returned $status"
  fi

  status=$(fetch "$web/app-config.json")
  if [ "$status" = 200 ] && grep -q "\"apiBaseUrl\": *\"$api\"" "$body_file"; then
    pass "app-config.json points at $api"
  else
    fail "app-config.json returned $status: $(head -c 200 "$body_file")"
  fi

  # The browser calls the API cross-origin: the API must allow the frontend's origin.
  headers=$(curl -sS -o /dev/null -D - --max-time 30 -X OPTIONS "$api/health" \
    -H "Origin: $web" -H 'Access-Control-Request-Method: GET' || true)
  if printf '%s' "$headers" | tr -d '\r' | grep -qi "^access-control-allow-origin: $web$"; then
    pass "CORS preflight allows $web"
  else
    fail "CORS preflight does not allow $web"
  fi
fi

if [ $failures -gt 0 ]; then
  echo "== smoke test FAILED ($failures check(s))"
  exit 1
fi
echo "== smoke test passed"
