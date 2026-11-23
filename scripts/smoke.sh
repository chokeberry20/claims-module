#!/usr/bin/env bash
# Smoke test for a deployed (or locally running) Claims Module (P2.5 task 17e).
#
#   scripts/smoke.sh <api-base-url> [frontend-url] [expected-commit-sha]
#   scripts/smoke.sh https://claimsdcs-api-xxxxxx.azurewebsites.net https://claimsdcs-web.azurestaticapps.net "$GITHUB_SHA"
#   scripts/smoke.sh http://localhost:5180 http://localhost:4200
#
# Needs only bash, curl and grep. Waits up to SMOKE_WAIT_SECONDS (default 300) for the API to come
# up, because App Service restarts after a deploy. With an expected commit SHA it also waits until
# /health/live reports that build ("version": "1.0.0+<sha>"): right after a deploy the previous
# process can still answer, and checking it gave false failures (P3 deploy-timing incident).
# Exits non-zero if any check fails. Later phases extend it with their endpoints.
set -euo pipefail

api="${1:?usage: smoke.sh <api-base-url> [frontend-url] [expected-commit-sha]}"
web="${2:-}"
expected_sha="${3:-}"
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

live_version() { grep -o '"version":"[^"]*"' "$body_file" | sed 's/"version":"\(.*\)"/\1/' || true; }

# Ready = /health/live answers 200 and, if a SHA is given, reports that build.
ready() {
  [ "$(fetch "$api/health/live")" = 200 ] || return 1
  [ -z "$expected_sha" ] && return 0
  # Match the SHA alone: the JSON encoder may escape the plus sign in "1.0.0+<sha>".
  case "$(live_version)" in
    *"$expected_sha"*) return 0 ;;
    *) return 1 ;;
  esac
}

echo "API: $api"
if [ -n "$expected_sha" ]; then
  echo "== waiting for /health/live to report build +$expected_sha (up to ${wait_seconds}s)"
else
  echo "== waiting for /health/live (up to ${wait_seconds}s)"
fi
deadline=$((SECONDS + wait_seconds))
until ready; do
  if [ $SECONDS -ge $deadline ]; then
    fail "/health/live not ready within ${wait_seconds}s (last version: $(live_version))"
    exit 1
  fi
  sleep 10
done
pass "/health/live 200, version $(live_version)"

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
# Document storage (P4): in Azure this also proves the Key Vault storage connection string resolved.
if grep -q '"storage":{"status":"Healthy"' "$body_file"; then
  pass "/health storage Healthy"
else
  fail "/health storage is not Healthy"
fi

# Hangfire dashboard (D-41): in Azure it must exist and ask for Basic credentials.
if [ "${SMOKE_EXPECT_DASHBOARD_AUTH:-0}" = 1 ]; then
  headers=$(curl -sS -o /dev/null -D - --max-time 30 "$api/hangfire" || true)
  if printf '%s' "$headers" | tr -d '\r' | grep -qE '^HTTP/[0-9.]+ 401' \
     && printf '%s' "$headers" | tr -d '\r' | grep -qi '^www-authenticate: basic'; then
    pass "/hangfire asks for Basic credentials (401)"
  else
    fail "/hangfire does not challenge with Basic auth: $(printf '%s' "$headers" | head -1)"
  fi
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
