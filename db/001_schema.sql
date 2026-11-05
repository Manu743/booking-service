
USE master;
GO

IF DB_ID('BookingServiceDb') IS NULL
BEGIN
    CREATE DATABASE BookingServiceDb;
END;
GO

USE BookingServiceDb;
GO

-- Rooms: rooms available for booking.
IF OBJECT_ID('dbo.Rooms', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Rooms (
        Id          INT IDENTITY(1,1) PRIMARY KEY,
        Name        NVARCHAR(100) NOT NULL,
        Capacity    INT NOT NULL,
        HourlyRate  DECIMAL(18,2) NOT NULL,

        CONSTRAINT CK_Rooms_Capacity
            CHECK (Capacity > 0),

        CONSTRAINT CK_Rooms_HourlyRate
            CHECK (HourlyRate > 0)
    );
END;
GO

-- Customers: customers and their membership tier.
IF OBJECT_ID('dbo.Customers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Customers (
        Id          INT IDENTITY(1,1) PRIMARY KEY,
        Name        NVARCHAR(100) NOT NULL,
        Tier        VARCHAR(10) NOT NULL
            CONSTRAINT DF_Customers_Tier DEFAULT 'None',

        CONSTRAINT CK_Customers_Tier
            CHECK (Tier IN ('None', 'Silver', 'Gold'))
    );
END;
GO

-- Bookings: persisted reservations and their price breakdown.
IF OBJECT_ID('dbo.Bookings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Bookings (
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
        CancelledAt DATETIME2(0) NULL,

        CONSTRAINT FK_Bookings_Rooms
            FOREIGN KEY (RoomId) REFERENCES dbo.Rooms(Id),

        CONSTRAINT FK_Bookings_Customers
            FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),

        CONSTRAINT CK_Bookings_EndAfterStart
            CHECK (EndAt > StartAt),

        CONSTRAINT CK_Bookings_Duration
            CHECK (DATEDIFF(MINUTE, StartAt, EndAt)
                   BETWEEN 30 AND 480),

        CONSTRAINT CK_Bookings_Alignment
            CHECK (
                DATEDIFF(MINUTE, '20000101', StartAt) % 15 = 0
                AND DATEDIFF(MINUTE, '20000101', EndAt) % 15 = 0
                AND DATEPART(SECOND, StartAt) = 0
                AND DATEPART(SECOND, EndAt) = 0
            ),

        CONSTRAINT CK_Bookings_Status
            CHECK (Status IN ('A', 'C')),

        CONSTRAINT CK_Bookings_Prices
            CHECK (
                Subtotal >= 0
                AND Discount >= 0
                AND Total >= 0
                AND (Fee IS NULL OR Fee >= 0)
            )
    );
END;
GO

-- Conflict detection (R4) and availability (B5).
-- Supports range scans by room and booking start time.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_Bookings_Room_StartAt'
      AND object_id = OBJECT_ID('dbo.Bookings')
)
BEGIN
    CREATE INDEX IX_Bookings_Room_StartAt
        ON dbo.Bookings (RoomId, StartAt)
        INCLUDE (EndAt, Status);
END;
GO

-- Customer monthly revenue reports (B4).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_Bookings_Customer_StartAt'
      AND object_id = OBJECT_ID('dbo.Bookings')
)
BEGIN
    CREATE INDEX IX_Bookings_Customer_StartAt
        ON dbo.Bookings (CustomerId, StartAt)
        INCLUDE (Status, Total, Fee);
END;
GO
