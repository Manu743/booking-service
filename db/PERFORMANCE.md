# B7 — Query performance: sargability and covering indexes

## Target query

The query under review is a date-range report over `dbo.Bookings`:

```sql
SELECT *
FROM dbo.Bookings
WHERE CONVERT(date, StartAt) = @d
  AND Status <> 'C';
```

It has three independent problems:

1. `CONVERT(date, StartAt) = @d` applies a function to the indexed column. In
   the general case the optimizer cannot use an index seek on `StartAt` and
   evaluates the expression once per row.
2. `SELECT *` returns every column. Even when the optimizer finds a useful
   nonclustered index, it must perform a key lookup per matching row to fetch
   the columns the index does not cover.
3. `Status <> 'C'` is a residual predicate unless a filtered or status-leading
   index is available.

SQL Server 2022 can sometimes derive a range from `CONVERT(date, ...)` through
the `GetRangeThroughConvert` optimization. That is an implementation detail,
not a guarantee: it depends on the predicate, the available indexes, and the
optimizer's cost model. The explicit range below is deterministic and portable.

## Rewrite

```sql
SELECT
    Id,
    RoomId,
    CustomerId,
    StartAt,
    EndAt,
    Status
FROM dbo.Bookings
WHERE StartAt >= @d
  AND StartAt < DATEADD(DAY, 1, @d)
  AND Status <> 'C';
```

- The half-open range keeps the predicate sargable: no function wraps
  `StartAt`, so an index on `StartAt` can seek the requested day.
- The explicit column list lets a covering index satisfy the query without key
  lookups. It also avoids returning columns the report does not need.
- `Status <> 'C'` remains a residual predicate. It is evaluated only on the
  rows inside the day range, which is cheap when the date range is selective.

## Index analysis

The schema already has two indexes:

```sql
IX_Bookings_Room_StartAt     (RoomId, StartAt)     INCLUDE (EndAt, Status)
IX_Bookings_Customer_StartAt (CustomerId, StartAt) INCLUDE (Status, Total, Fee)
```

Neither one can serve a **global** date-range query efficiently, because
`StartAt` is not the leading key column. They are correct for the application's
hot paths:

- room-scoped conflict detection and availability filter by `RoomId` first;
- customer monthly revenue filters by `CustomerId` first.

For the global report, the proposed index is:

```sql
CREATE INDEX IX_Bookings_StartAt
    ON dbo.Bookings (StartAt)
    INCLUDE (Status, RoomId, CustomerId, EndAt);
```

With the explicit column list, this index covers the query and turns the scan
into a covering seek. If the query must return every column, no reasonably
sized index covers it; keep the explicit list.

Alternatives:

- `(Status, StartAt)` is attractive when most rows are active and queries
  filter `Status = 'A'`. With the schema's `A`/`C` domain, `Status <> 'C'` is
  equivalent to `Status = 'A'`, but the date range is the more selective
  dimension here (one day out of 1,041 days in the benchmark), so leading with
  `StartAt` is the better choice.
- A filtered index `WHERE Status <> 'C'` only helps when cancelled bookings are
  a large fraction of the table. In the benchmark 10% of rows are cancelled, so
  the filtered index would still contain 90% of the rows and would not be
  meaningfully smaller. If cancellations became the majority, the filtered
  index would be worth re-testing.

Trade-off: every additional index makes inserts and cancellations more
expensive. The application does not currently expose a global date-range
report, so adding `IX_Bookings_StartAt` is not justified by the current
workload. Add it only if the report becomes a real, frequent product
requirement, and measure the write cost before shipping it.

## Benchmark

Environment: SQL Server 2022 in a disposable Linux container. The synthetic
table has 1,000,000 rows spread over 1,041 days starting 2024-01-01, three
rooms, four customers, 60-minute bookings, and 10% cancelled rows. The table
keeps the same columns as `dbo.Bookings`, a clustered primary key on `Id`, and
one nonclustered index on `StartAt`. The covering index is added after the
first measurements.

Setup:

```sql
CREATE TABLE dbo.BookingsPerf
(
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    RoomId      INT NOT NULL,
    CustomerId  INT NOT NULL,
    StartAt     DATETIME2(0) NOT NULL,
    EndAt       DATETIME2(0) NOT NULL,
    Status      CHAR(1) NOT NULL,
    Subtotal    DECIMAL(18,2) NOT NULL,
    Discount    DECIMAL(18,2) NOT NULL,
    Total       DECIMAL(18,2) NOT NULL,
    Fee         DECIMAL(18,2) NULL,
    CancelledAt DATETIME2(0) NULL
);

INSERT INTO dbo.BookingsPerf
(
    RoomId, CustomerId, StartAt, EndAt, Status,
    Subtotal, Discount, Total, Fee, CancelledAt
)
SELECT
    (g.value % 3) + 1,
    (g.value % 4) + 1,
    DATEADD(MINUTE, (g.value % 100000) * 15, '2024-01-01T00:00:00'),
    DATEADD(MINUTE, (g.value % 100000) * 15 + 60, '2024-01-01T00:00:00'),
    CASE WHEN g.value % 10 = 0 THEN 'C' ELSE 'A' END,
    40.00,
    0.00,
    40.00,
    CASE WHEN g.value % 10 = 0 THEN 20.00 ELSE NULL END,
    CASE
        WHEN g.value % 10 = 0
        THEN DATEADD(MINUTE, (g.value % 100000) * 15, '2024-01-01T00:00:00')
        ELSE NULL
    END
FROM GENERATE_SERIES(1, 1000000) AS g;

CREATE INDEX IX_BookingsPerf_StartAt
    ON dbo.BookingsPerf (StartAt);

-- Added before the covering-index measurements.
CREATE INDEX IX_BookingsPerf_StartAt_Covering
    ON dbo.BookingsPerf (StartAt)
    INCLUDE (Status, RoomId, CustomerId, EndAt);
```

Each measurement used `CHECKPOINT`, `DBCC DROPCLEANBUFFERS`, and
`SET STATISTICS IO, TIME ON` so the logical reads are comparable. The reported
CPU and elapsed times are the execution times of the query, not the batch
compile time.

| Query | Index available | Plan | Logical reads | CPU | Elapsed |
| --- | --- | --- | ---: | ---: | ---: |
| A1: `CONVERT(date, StartAt)` + `SELECT *` | `IX_BookingsPerf_StartAt` | Index Seek (`GetRangeThroughConvert`) + Key Lookup | 2,888 | 1 ms | 5 ms |
| B1: range + explicit columns | `IX_BookingsPerf_StartAt` | Index Seek + Key Lookup | 3,121 | 3 ms | 4 ms |
| B2: range + explicit columns | covering index | Covering Index Seek | 8 | 2 ms | 1 ms |
| A2: `CONVERT(date, StartAt)` + `SELECT *` | covering index | Covering Index Seek + Key Lookup | 2,621 | 1 ms | 9 ms |

Observations:

- Without a covering index, the explicit range alone did not help. Both forms
  sought `IX_BookingsPerf_StartAt` and then performed a key lookup for each
  row: roughly 2,900–3,100 logical reads for a single day.
- The covering index reduced the explicit-column query to 8 logical reads.
  That is a covering seek with no key lookups.
- `SELECT *` still performed key lookups with the covering index (2,621 logical
  reads), because the index does not include every column. The explicit column
  list is part of the fix, not a cosmetic detail.
- `CONVERT(date, StartAt)` used `GetRangeThroughConvert` in this SQL Server
  2022 test. The plan is not a full scan here, but relying on that
  transformation is fragile. The explicit range is the safe rewrite and the one
  the report should keep.
- The golden dataset has only 11 bookings, so a plan on that data proves
  correctness, not performance. The synthetic table is intentionally larger;
  scale the `GENERATE_SERIES` upper bound to 5,000,000 to reproduce the guide's
  stated scale.

## Decision

- Keep the explicit range and explicit column list in report queries.
- Do not add `IX_Bookings_StartAt` to the production schema yet: the current
  API workload is room-scoped or customer-scoped, and the existing indexes
  already serve those paths. A global date-range report is not an application
  endpoint.
- If the report becomes a product requirement, add the covering index and
  measure write overhead on the booking insert and cancellation paths before
  shipping it.
- Re-run the benchmark with production-like cardinality and data distribution
  before making a final index decision.
