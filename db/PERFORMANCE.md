# B7 - Query performance

## The slow query

```sql
SELECT * FROM Bookings
WHERE CONVERT(date, StartAt) = @d
  AND Status <> 'Cancelled';
```

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
## Proposed index



## What changes in the execution plan

<!-- TODO: scan -> seek, key lookups, estimated rows, residual predicate -->

## Trade-offs

<!-- TODO: write amplification, filtered index caveats, covering width -->
