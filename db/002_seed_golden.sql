
USE BookingServiceDb;
GO

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

-- Remove existing golden data.
DELETE FROM dbo.Bookings;
DELETE FROM dbo.Customers;
DELETE FROM dbo.Rooms;

-- Insert rooms.
SET IDENTITY_INSERT dbo.Rooms ON;

INSERT INTO dbo.Rooms (Id, Name, Capacity, HourlyRate)
VALUES
    (1, 'R1', 10, 40.00),
    (2, 'R2',  8, 25.00),
    (3, 'R3', 12, 60.00);

SET IDENTITY_INSERT dbo.Rooms OFF;

-- Insert customers.
SET IDENTITY_INSERT dbo.Customers ON;

INSERT INTO dbo.Customers (Id, Name, Tier)
VALUES
    (1, 'C1', 'None'),
    (2, 'C2', 'None'),
    (3, 'C3', 'None'),
    (4, 'C4', 'None');

SET IDENTITY_INSERT dbo.Customers OFF;

-- Insert bookings.
SET IDENTITY_INSERT dbo.Bookings ON;

INSERT INTO dbo.Bookings
(
    Id, RoomId, CustomerId, StartAt, EndAt,
    Status, Subtotal, Discount, Total, Fee, CancelledAt
)
VALUES
    (1, 1, 1, '2026-03-02T08:00:00', '2026-03-02T10:00:00', 'A', 100.00, 0.00, 100.00, NULL, NULL),
    (2, 1, 2, '2026-03-02T11:30:00', '2026-03-02T14:30:00', 'A', 140.00, 0.00, 140.00, NULL, NULL),
    (3, 1, 3, '2026-03-02T14:30:00', '2026-03-02T15:30:00', 'A',  60.00, 0.00,  60.00, NULL, NULL),
    (4, 2, 1, '2026-03-02T09:00:00', '2026-03-02T12:00:00', 'C', 112.50, 0.00, 112.50, 56.25, '2026-03-02T08:00:00'),
    (5, 2, 4, '2026-03-02T10:00:00', '2026-03-02T11:00:00', 'A',  37.50, 0.00,  37.50, NULL, NULL),
    (6, 3, 2, '2026-03-07T10:00:00', '2026-03-07T11:45:00', 'A', 131.25, 0.00, 131.25, NULL, NULL),
    (7, 3, 3, '2026-03-07T11:30:00', '2026-03-07T12:30:00', 'A',  75.00, 0.00,  75.00, NULL, NULL),
    (8, 1, 4, '2026-04-06T09:00:00', '2026-04-06T12:00:00', 'A', 180.00, 0.00, 180.00, NULL, NULL),
    (9, 2, 3, '2026-04-06T13:00:00', '2026-04-06T14:00:00', 'A',  25.00, 0.00,  25.00, NULL, NULL),
    (10, 2, 1, '2026-04-06T14:00:00', '2026-04-06T15:00:00', 'A',  37.50, 0.00,  37.50, NULL, NULL),
    (11, 3, 4, '2026-03-03T16:45:00', '2026-03-03T18:15:00', 'A',  97.50, 0.00,  97.50, NULL, NULL);

SET IDENTITY_INSERT dbo.Bookings OFF;

COMMIT TRANSACTION;
GO

