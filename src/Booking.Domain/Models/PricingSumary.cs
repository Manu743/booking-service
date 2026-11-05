using System;
using System.Collections.Generic;
using System.Text;

namespace Booking.Domain.Models
{
    internal class PricingSumary
    {
    }
}
public record PricingSumary(
    decimal Subtotal,
    decimal DiscountPercentage,
    decimal DiscountAmount,
    decimal Total);