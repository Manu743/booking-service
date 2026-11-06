USE master;
GO

-- A previous/partial database must never be mistaken for a fresh assessment.
IF DB_ID(N'BookingServiceDb') IS NOT NULL
    THROW 51000, 'Refusing to initialize an existing database.', 1;
GO

CREATE DATABASE BookingServiceDb;
GO
