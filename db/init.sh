#!/usr/bin/env bash
# Assessment-only bootstrap. Never reset or seed an existing database.
set -euo pipefail

: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD is required}"
# Compose embeds the password as a quoted SqlClient value; reject its delimiter.
if [[ "$MSSQL_SA_PASSWORD" == *'"'* ]]; then
    printf '%s\n' 'MSSQL_SA_PASSWORD must not contain double quotes.' >&2
    exit 1
fi
export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
sqlcmd=(/opt/mssql-tools18/bin/sqlcmd -S mssql -U sa -C -b -V 16 -l 15)

state=$("${sqlcmd[@]}" -d master -h -1 -W -Q "SET NOCOUNT ON;
IF DB_ID(N'BookingServiceDb') IS NULL SELECT 'fresh';
ELSE EXEC(N'USE BookingServiceDb;
    IF EXISTS (SELECT 1 FROM sys.extended_properties
        WHERE class = 0 AND name = N''Booking.ContainerInitialization'' AND CONVERT(nvarchar(128), value) = N''v1'')
        SELECT ''initialized'';
    ELSE SELECT ''unmarked'';');")
state=${state//$'\r'/}
case "$state" in
    initialized)
        printf '%s\n' 'Skipping initialized assessment database; existing data is preserved.'
        exit 0
        ;;
    fresh) ;;
    *)
        printf '%s\n' 'Refusing to initialize an existing unmarked database; use a fresh assessment volume.' >&2
        exit 1
        ;;
esac

# Only database creation runs in master. Each subsequent sqlcmd starts in the
# application database, including scripts such as 003 without their own USE.
printf '%s\n' 'Applying 000_create_database.sql'
"${sqlcmd[@]}" -d master -i /db/000_create_database.sql -o /dev/null

# Explicit numeric order, including the destructive golden seed ONLY on the fresh path.
for script in \
    001_schema.sql \
    002_seed_golden.sql \
    003_create_booking_procedure.sql \
    010_utilisation.sql \
    011_top_customers.sql \
    012_free_windows.sql \
    013_data_quality.sql; do
    printf 'Applying %s\n' "$script"
    "${sqlcmd[@]}" -d BookingServiceDb -i "/db/$script" -o /dev/null
done

# Written only after every script succeeds. Partial initialization fails closed on restart.
"${sqlcmd[@]}" -d BookingServiceDb -Q "EXEC sys.sp_addextendedproperty
    @name=N'Booking.ContainerInitialization', @value=N'v1';" -o /dev/null
printf '%s\n' 'Assessment database initialization completed.'
