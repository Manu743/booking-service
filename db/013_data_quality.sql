USE BookingServiceDb;
GO

-- B6: List every pair of active bookings in the same room that conflict under R4, 
-- each pair once (lower Id first). Golden data is loaded directly with SQL, so it can contain conflicts.

CREATE OR ALTER PROCEDURE dbo.usp_ConflictingBookings
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        a.Id AS LowerId,
        b.Id AS HigherId
    FROM dbo.Bookings a
    JOIN dbo.Bookings b
        ON a.RoomId = b.RoomId
       AND a.Id < b.Id
       AND a.Status = 'A'
       AND b.Status = 'A'
       AND b.StartAt < DATEADD(MINUTE, 15, a.EndAt)
       AND a.StartAt < DATEADD(MINUTE, 15, b.EndAt)
    ORDER BY a.Id, b.Id;
END;
GO

EXEC dbo.usp_ConflictingBookings;
GO
