USE BookingServiceDb;
GO

-- B5: For a room and a date, the windows [From, To) in which a booking of 
-- at least N minutes could be placed without breaking R3 or R4. Remember 
-- that the new booking needs its own buffer too.

CREATE OR ALTER PROCEDURE dbo.usp_FreeWindows
    @RoomId INT,
    @Date DATE,
    @MinMinutes INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @RoomId <= 0
    BEGIN
        THROW 50011, 'RoomId must be greater than zero.', 1;
    END;

    IF @Date IS NULL
    BEGIN
        THROW 50012, 'Date is required.', 1;
    END;

    IF @MinMinutes <= 0
    BEGIN
        THROW 50013, 'MinMinutes must be greater than zero.', 1;
    END;

    DECLARE @DayStart DATETIME2(0) = CONVERT(DATETIME2(0), @Date);
    DECLARE @DayEnd DATETIME2(0) = DATEADD(DAY, 1, @DayStart);
    DECLARE @DayOfWeek INT =
        (DATEDIFF(DAY, '19000107', @DayStart) % 7 + 7) % 7;
    DECLARE @OpenStart DATETIME2(0);
    DECLARE @OpenEnd DATETIME2(0);

    IF @DayOfWeek = 0
    BEGIN
        -- Sunday: the room is closed.
        RETURN;
    END;

    IF @DayOfWeek = 6
    BEGIN
        SET @OpenStart = DATEADD(HOUR, 8, @DayStart);
        SET @OpenEnd = DATEADD(HOUR, 18, @DayStart);
    END
    ELSE
    BEGIN
        SET @OpenStart = DATEADD(HOUR, 7, @DayStart);
        SET @OpenEnd = DATEADD(HOUR, 22, @DayStart);
    END;

    ;WITH Blocked AS
    (
        SELECT
            DATEADD(MINUTE, -15, StartAt) AS StartAt,
            DATEADD(MINUTE, 15, EndAt) AS EndAt,
            ROW_NUMBER() OVER (ORDER BY StartAt, Id) AS RowNumber
        FROM dbo.Bookings
        WHERE RoomId = @RoomId
          AND Status = 'A'
          AND StartAt < @DayEnd
          AND EndAt > @DayStart
    ),
    WithPreviousMax AS
    (
        SELECT
            StartAt,
            EndAt,
            RowNumber,
            MAX(EndAt) OVER (
                ORDER BY RowNumber
                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS PreviousMaxEnd
        FROM Blocked
    ),
    Islands AS
    (
        SELECT
            StartAt,
            EndAt,
            RowNumber,
            CASE
                WHEN PreviousMaxEnd IS NULL OR StartAt > PreviousMaxEnd
                THEN 1
                ELSE 0
            END AS IsNewIsland
        FROM WithPreviousMax
    ),
    IslandGroups AS
    (
        SELECT
            StartAt,
            EndAt,
            SUM(IsNewIsland) OVER (
                ORDER BY RowNumber
                ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS IslandId
        FROM Islands
    ),
    Merged AS
    (
        SELECT
            MIN(StartAt) AS StartAt,
            MAX(EndAt) AS EndAt
        FROM IslandGroups
        GROUP BY IslandId
    ),
    Clipped AS
    (
        SELECT
            CASE
                WHEN StartAt < @OpenStart THEN @OpenStart
                ELSE StartAt
            END AS StartAt,
            CASE
                WHEN EndAt > @OpenEnd THEN @OpenEnd
                ELSE EndAt
            END AS EndAt
        FROM Merged
        WHERE EndAt > @OpenStart
          AND StartAt < @OpenEnd
    ),
    Gaps AS
    (
        SELECT
            @OpenStart AS GapStart,
            ISNULL(MIN(StartAt), @OpenEnd) AS GapEnd
        FROM Clipped

        UNION ALL

        SELECT
            PreviousEnd,
            StartAt
        FROM
        (
            SELECT
                StartAt,
                LAG(EndAt) OVER (ORDER BY StartAt) AS PreviousEnd
            FROM Clipped
        ) InternalGaps
        WHERE PreviousEnd IS NOT NULL
          AND StartAt > PreviousEnd

        UNION ALL

        SELECT
            MAX(EndAt),
            @OpenEnd
        FROM Clipped
        HAVING MAX(EndAt) < @OpenEnd
    )
    SELECT
        CONVERT(TIME(0), GapStart) AS [From],
        CONVERT(TIME(0), GapEnd) AS [To]
    FROM Gaps
    WHERE GapStart IS NOT NULL
      AND GapEnd IS NOT NULL
      AND GapEnd > GapStart
      AND DATEDIFF(MINUTE, GapStart, GapEnd) >= @MinMinutes
    ORDER BY GapStart;
END;
GO

-- Golden example from the guide.
EXEC dbo.usp_FreeWindows
    @RoomId = 1,
    @Date = '2026-03-02',
    @MinMinutes = 60;
GO
