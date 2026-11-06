USE BookingServiceDb;
GO

-- B3: For a room and a date range, one row per calendar day, 
-- including days with no bookings: Date,OpenMinutes, BookedMinutes 
-- (active only, buffer excluded), Utilisation % (2 decimals). Closed days 
-- show OpenMinutes 0 and Utilisation NULL
CREATE OR ALTER PROCEDURE dbo.usp_RoomUtilisation
    @RoomId INT,
    @FromDate DATE,
    @ToDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    IF @RoomId <= 0
    BEGIN
        THROW 50001, 'RoomId must be greater than zero.', 1;
    END;

    IF @FromDate IS NULL OR @ToDate IS NULL OR @ToDate < @FromDate
    BEGIN
        THROW 50002, 'The date range is invalid.', 1;
    END;

    ;WITH Days AS
    (
        SELECT DATEADD(DAY, value, @FromDate) AS [Date]
        FROM GENERATE_SERIES(
            0,
            DATEDIFF(DAY, @FromDate, @ToDate),
            1)
    ),
    DayInfo AS
    (
        SELECT
            d.[Date],
            -- 0 = Sunday, 1 = Monday, ..., 6 = Saturday.
            (DATEDIFF(DAY, '19000107', d.[Date]) % 7 + 7) % 7 AS DayOfWeek,
            DATEADD(DAY, 1, d.[Date]) AS DayEnd
        FROM Days d
    ),
    DayOpening AS
    (
        SELECT
            di.[Date],
            di.DayEnd,
            CASE
                WHEN di.DayOfWeek = 0 THEN 0
                WHEN di.DayOfWeek = 6 THEN 600
                ELSE 900
            END AS OpenMinutes
        FROM DayInfo di
    ),
    Booked AS
    (
        SELECT
            dop.[Date],
            SUM(DATEDIFF(
                MINUTE,
                CASE
                    WHEN b.StartAt < dop.[Date] THEN dop.[Date]
                    ELSE b.StartAt
                END,
                CASE
                    WHEN b.EndAt > dop.DayEnd THEN dop.DayEnd
                    ELSE b.EndAt
                END)) AS BookedMinutes
        FROM DayOpening dop
        JOIN dbo.Bookings b
            ON b.RoomId = @RoomId
           AND b.Status = 'A'
           AND b.StartAt < dop.DayEnd
           AND b.EndAt > dop.[Date]
        GROUP BY dop.[Date]
    )
    SELECT
        dop.[Date],
        dop.OpenMinutes,
        ISNULL(bk.BookedMinutes, 0) AS BookedMinutes,
        CAST(
            ROUND(
                ISNULL(bk.BookedMinutes, 0) * 100.0
                    / NULLIF(dop.OpenMinutes, 0),
                2)
            AS DECIMAL(9, 2)) AS [Utilisation %]
    FROM DayOpening dop
    LEFT JOIN Booked bk
        ON bk.[Date] = dop.[Date]
    ORDER BY dop.[Date];
END;
GO

-- Golden example from the guide.
EXEC dbo.usp_RoomUtilisation
    @RoomId = 1,
    @FromDate = '2026-03-02',
    @ToDate = '2026-03-08';
GO
