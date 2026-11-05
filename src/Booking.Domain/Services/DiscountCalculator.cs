using System;
using System.Collections.Generic;
using System.Text;
using Booking.Domain.Models;

namespace Booking.Domain.Services
{
    internal class DiscountCalculator
    {
    }
}

public class DiscountCalculator
{
    private const decimal MaximumDiscount = 50m;

    public PricingSumary Calculate(
        decimal subtotal,
        decimal discountPercentage)
    {
        if (subtotal < 0)
            throw new ArgumentOutOfRangeException(
                nameof(subtotal),
                "Subtotal cannot be negative.");

        if (discountPercentage < 0 ||
            discountPercentage > 1)
            throw new ArgumentOutOfRangeException(
                nameof(discountPercentage),
                "Discount percentage must be between 0 and 1.");

        var calculatedDiscount =
            subtotal * discountPercentage;

        var discountAmount =
            Math.Min(calculatedDiscount, MaximumDiscount);

        var total = Math.Round(
            subtotal - discountAmount,
            2,
            MidpointRounding.AwayFromZero);

        return new PricingSumary(
            subtotal,
            discountPercentage,
            discountAmount,
            total);
    }
}