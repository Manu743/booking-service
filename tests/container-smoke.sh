#!/usr/bin/env bash
# Disposable end-to-end assessment check. Requires a running local Docker engine,
# Docker Compose v2, curl, and a free host port 8080. Never reads the user's .env.
set -euo pipefail
cd "$(dirname "$0")/.."
root=$(pwd -P)

project="booking-t1-$$-$RANDOM"
failure_project="$project-failure"
# Default startup must genuinely use the public disposable fallback, not user input.
unset MSSQL_SA_PASSWORD
override_password="Aa1!${RANDOM}${RANDOM}${RANDOM}${RANDOM};${RANDOM}${RANDOM}"
# Prevent Git Bash rewriting container paths in docker exec/run arguments.
export MSYS_NO_PATHCONV=1
# Native Windows Compose cannot open /dev/null. This unique empty project-local
# file prevents implicit discovery of the user's .env and is removed by the trap.
env_file=$(mktemp "$root/.phase8-empty-env.XXXXXX")
compose_env_file="$env_file"
compose() { docker compose --env-file "$compose_env_file" -p "$project" "$@"; }
# Reuse the failure-only SQL instance to prove a semicolon-containing override
# actually authenticates, without building another full application stack.
failure_compose() { MSSQL_SA_PASSWORD="$override_password" docker compose --env-file "$compose_env_file" -p "$failure_project" "$@"; }
cleanup() {
    local result=$?
    trap - EXIT
    if [[ "$project" =~ ^booking-t1-[0-9]+-[0-9]+$ && "$failure_project" == "$project-failure" ]]; then
        compose down --volumes --remove-orphans || result=1
        failure_compose down --volumes --remove-orphans || result=1
        unlink "$env_file" || result=1
        printf 'Cleanup completed for %s and %s, including empty env scratch (exit %s).\n' "$project" "$failure_project" "$result"
    else
        printf '%s\n' 'Unsafe project name; refusing cleanup.' >&2
        result=1
    fi
    exit "$result"
}
trap cleanup EXIT
if command -v cygpath >/dev/null 2>&1; then
    compose_env_file=$(cygpath -m "$env_file")
fi
printf 'Isolated projects: %s, %s\nEmpty env scratch: %s\n' "$project" "$failure_project" "$env_file"

# Read-only config accepts missing/empty input before any resources exist.
for mode in unset empty; do
    (
        if [[ "$mode" == unset ]]; then unset MSSQL_SA_PASSWORD; else export MSSQL_SA_PASSWORD=''; fi
        compose config --quiet
    )
done
for owned_project in "$project" "$failure_project"; do
    [[ -z "$(docker ps -aq --filter "label=com.docker.compose.project=$owned_project")" ]]
    [[ -z "$(docker network ls -q --filter "label=com.docker.compose.project=$owned_project")" ]]
    [[ -z "$(docker volume ls -q --filter "label=com.docker.compose.project=$owned_project")" ]]
done
compose config --quiet
failure_compose config --quiet
printf '%s\n' 'PASS: unset/empty passwords use demo default before resources; synthetic override accepted.'

# Fail-fast proof on a fresh disposable SQL instance: stop before later SQL scripts,
# leave the incomplete database unmarked, and refuse its next initialization.
failure_compose up -d --wait --wait-timeout 180 mssql
failure_compose exec -T mssql /bin/bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -b -V 16 -Q "SELECT 1" -o /dev/null'
printf '%s\n' 'PASS: synthetic semicolon override authenticates to isolated SQL.'
if quote_output=$(failure_compose run --rm --no-deps -e "MSSQL_SA_PASSWORD=${override_password}\"" db-init 2>&1); then
    printf '%s\n' 'Double-quote password unexpectedly accepted.' >&2
    exit 1
fi
[[ "$quote_output" == *'double quote'* || "$quote_output" == *'double-quote'* || "$quote_output" == *'double quotes'* ]]
if failure_output=$(failure_compose run --rm --no-deps --entrypoint /bin/bash db-init -c '
    mkdir /tmp/scripts
    cp /db/*.sql /tmp/scripts/
    printf "THROW 51001, '\''Synthetic initialization failure.'\'', 1;\nGO\n" > /tmp/scripts/003_create_booking_procedure.sql
    bootstrap=$(</db/init.sh)
    bootstrap=${bootstrap//\/db\//\/tmp\/scripts\/}
    bash -c "$bootstrap"
' 2>&1); then
    printf '%s\n' 'Invalid SQL unexpectedly succeeded.' >&2
    exit 1
fi
[[ "$failure_output" == *'Applying 003_create_booking_procedure.sql'* ]]
[[ "$failure_output" != *'Applying 010_utilisation.sql'* ]]
if unmarked_output=$(failure_compose run --rm --no-deps db-init 2>&1); then
    printf '%s\n' 'Incomplete database unexpectedly accepted.' >&2
    exit 1
fi
[[ "$unmarked_output" == *'Refusing to initialize an existing unmarked database'* ]]
printf '%s\n' 'PASS: SQL error stops initialization; incomplete database fails closed.'
failure_compose down --volumes --remove-orphans

# Normal startup must automatically seed, install procedures and become healthy.
compose up -d --build --wait --wait-timeout 180
api_id=$(compose ps -q api)
[[ -n "$api_id" ]]
[[ "$(docker inspect --format '{{range (index .NetworkSettings.Ports "8080/tcp")}}{{.HostIp}}:{{.HostPort}}{{end}}' "$api_id")" == '127.0.0.1:8080' ]]
sql_id=$(compose ps -q mssql)
[[ "$(docker inspect --format '{{range .NetworkSettings.Ports}}{{if .}}published{{end}}{{end}}' "$sql_id")" != *published* ]]
printf '%s\n' 'PASS: runtime API binding is loopback-only; SQL has no published port.'
[[ "$(docker inspect --format '{{.Config.User}}' "$api_id")" != 0 ]]
[[ -n "$(docker inspect --format '{{.Config.User}}' "$api_id")" ]]
[[ "$(docker inspect --format '{{.State.Health.Status}}' "$api_id")" == healthy ]]
health=$(curl --fail --silent --show-error --max-time 10 http://localhost:8080/health/database)
[[ "$health" == *'"status":"Connected"'* && "$health" == *'"database":"BookingServiceDb"'* ]]
page=$(curl --fail --silent --show-error --location --max-time 10 http://localhost:8080/swagger)
[[ "$page" == *swagger-ui* ]]
document=$(curl --fail --silent --show-error --max-time 10 http://localhost:8080/openapi/v1.json)
[[ "$document" == *'/bookings'* ]]
rooms=$(curl --fail --silent --show-error --max-time 10 http://localhost:8080/rooms)
[[ "$rooms" == *'"name":"R1"'* && "$rooms" == *'"name":"R3"'* ]]
windows=$(curl --fail --silent --show-error --max-time 10 'http://localhost:8080/rooms/1/availability?date=2026-03-02&minMinutes=60')
[[ "$windows" == *'2026-03-02T10:15:00'* && "$windows" == *'2026-03-02T15:45:00'* ]]

# Observe database placement directly, not just service health or room writes.
placement=$(compose exec -T mssql /bin/bash -c '
    export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
    exec /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -b -V 16 -d master -h -1 -W -Q "SET NOCOUNT ON;
    SELECT CONCAT('\''app='\'',
        (SELECT COUNT(*) FROM BookingServiceDb.sys.procedures WHERE schema_id = 1 AND name = N'\''usp_CreateBooking'\''),
        '\'',master='\'',
        (SELECT COUNT(*) FROM master.sys.procedures WHERE schema_id = 1 AND name = N'\''usp_CreateBooking'\''));"
')
placement=${placement//$'\r'/}
regression_failed=0
if [[ "$placement" != 'app=1,master=0' ]]; then
    printf 'FAIL: procedure placement expected app=1,master=0; observed %s\n' "$placement" >&2
    regression_failed=1
else
    printf '%s\n' 'PASS: procedure placement app=1,master=0.'
fi

# Fresh API-created IDs avoid depending on seed identities or conflicting golden slots.
created=$(curl --fail --silent --show-error --max-time 10 -H 'Content-Type: application/json' \
    -d '{"name":"Synthetic smoke room","capacity":2,"hourlyRate":10}' http://localhost:8080/rooms)
[[ "$created" == *'"name":"Synthetic smoke room"'* ]]
[[ "$created" =~ \"id\":([0-9]+) ]]
room_id=${BASH_REMATCH[1]}
customer=$(curl --fail --silent --show-error --max-time 10 -H 'Content-Type: application/json' \
    -d '{"name":"Synthetic smoke customer","tier":"None"}' http://localhost:8080/customers)
[[ "$customer" =~ \"id\":([0-9]+) ]]
customer_id=${BASH_REMATCH[1]}
request="{\"roomId\":$room_id,\"customerId\":$customer_id,\"startAt\":\"2026-03-09T08:00:00\",\"endAt\":\"2026-03-09T09:00:00\"}"
booking_result=$(curl --silent --show-error --max-time 10 --write-out '\n%{http_code}' \
    -H 'Content-Type: application/json' -d "$request" http://localhost:8080/bookings)
booking_status=${booking_result##*$'\n'}
booking_body=${booking_result%$'\n'*}
if [[ "$booking_status" != 201 ]]; then
    # Do not print a Development error body: it may contain unnecessary diagnostics.
    printf 'FAIL: POST /bookings expected HTTP 201; observed HTTP %s\n' "$booking_status" >&2
    regression_failed=1
else
    printf '%s\n' 'PASS: POST /bookings returned HTTP 201.'
fi
[[ "$regression_failed" == 0 ]] || exit 1
[[ "$booking_body" =~ \"id\":([0-9]+) ]]
booking_id=${BASH_REMATCH[1]}
[[ "$booking_body" == *"\"roomId\":$room_id,"* && "$booking_body" == *"\"customerId\":$customer_id,"* ]]
[[ "$booking_body" == *'"startAt":"2026-03-09T08:00:00"'* && "$booking_body" == *'"endAt":"2026-03-09T09:00:00"'* ]]
[[ "$booking_body" == *'"status":"A"'* && "$booking_body" == *'"total":10'* ]]
booking_before=$(curl --fail --silent --show-error --max-time 10 "http://localhost:8080/bookings/$booking_id")
# SQL decimals preserve scale (10.00), unlike freshly calculated decimals (10.0).
# This integer-price fixture permits removing zero-only fractional parts without
# weakening the full response comparison or requiring an additional JSON tool.
normalize_booking() { printf '%s' "$1" | sed -E 's/([0-9])\.0+([,}])/\1\2/g'; }
if [[ "$(normalize_booking "$booking_before")" != "$(normalize_booking "$booking_body")" ]]; then
    printf 'FAIL: persisted booking differs from HTTP 201 content.\nPOST: %s\nGET: %s\n' "$booking_body" "$booking_before" >&2
    exit 1
fi
before=$(curl --fail --silent --show-error --max-time 10 http://localhost:8080/rooms)
repeat=$(compose run --rm --no-deps db-init)
[[ "$repeat" == *'Skipping initialized assessment database'* ]]
compose restart api
compose up -d --wait --wait-timeout 90 api
after=$(curl --fail --silent --show-error --max-time 10 http://localhost:8080/rooms)
[[ "$after" == "$before" ]]
booking_after=$(curl --fail --silent --show-error --max-time 10 "http://localhost:8080/bookings/$booking_id")
[[ "$booking_after" == "$booking_before" ]]
printf '%s\n' 'PASS: non-root API, health, Swagger, golden seed, availability, booking POST/GET and data-preserving repeat startup.'
