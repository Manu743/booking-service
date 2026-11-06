USE BookingServiceDb;
GO

-- B4: Top three customers by revenue for each month.
-- Revenue: Total for active bookings plus Fee for cancelled bookings.
-- DENSE_RANK keeps ties at the cutoff.
CREATE OR ALTER PROCEDURE dbo.usp_TopCustomersByMonth
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Revenue AS
    (
        SELECT
            DATEFROMPARTS(YEAR(b.StartAt), MONTH(b.StartAt), 1) AS MonthStart,
            c.Id AS CustomerId,
            c.Name AS Customer,
            SUM(
                CASE
                    WHEN b.Status = 'A' THEN b.Total
                    ELSE ISNULL(b.Fee, 0)
                END) AS Revenue
        FROM dbo.Bookings b
        JOIN dbo.Customers c
            ON c.Id = b.CustomerId
        GROUP BY
            DATEFROMPARTS(YEAR(b.StartAt), MONTH(b.StartAt), 1),
            c.Id,
            c.Name
    ),
    Ranked AS
    (
        SELECT
            MonthStart,
            CustomerId,
            Customer,
            Revenue,
            DENSE_RANK() OVER (
                PARTITION BY MonthStart
                ORDER BY Revenue DESC) AS [Rank]
        FROM Revenue
    )
    SELECT
        CONVERT(CHAR(7), MonthStart, 126) AS [Month],
        Customer,
        Revenue,
        [Rank]
    FROM Ranked
    WHERE [Rank] <= 3
    ORDER BY
        MonthStart,
        [Rank],
        Revenue DESC,
        CustomerId;
END;
GO

EXEC dbo.usp_TopCustomersByMonth;
GO
