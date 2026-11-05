CREATE OR ALTER PROCEDURE dbo.usp_CreateBooking
    @RoomId INT,
    @CustomerId INT,
    @StartAt DATETIME2(0),
    @EndAt DATETIME2(0),
    @Subtotal DECIMAL(18,2),
    @Discount DECIMAL(18,2),
    @Total DECIMAL(18,2),
    @BookingId INT OUTPUT,
    @ResultCode INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    SET @BookingId = NULL;
    SET @ResultCode = 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.Bookings WITH
            (
                UPDLOCK,
                HOLDLOCK,
                INDEX(IX_Bookings_Room_StartAt)
            )
            WHERE RoomId = @RoomId
              AND Status = 'A'
              AND StartAt < DATEADD(MINUTE, 15, @EndAt)
              AND EndAt > DATEADD(MINUTE, -15, @StartAt)
        )
        BEGIN
            SET @ResultCode = 1;

            COMMIT TRANSACTION;
            RETURN;
        END;

        INSERT INTO dbo.Bookings
        (
            RoomId,
            CustomerId,
            StartAt,
            EndAt,
            Status,
            Subtotal,
            Discount,
            Total,
            Fee,
            CancelledAt
        )
        VALUES
        (
            @RoomId,
            @CustomerId,
            @StartAt,
            @EndAt,
            'A',
            @Subtotal,
            @Discount,
            @Total,
            0,
            NULL
        );

        SET @BookingId = CONVERT(INT, SCOPE_IDENTITY());
        SET @ResultCode = 0;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH;
END;
GO