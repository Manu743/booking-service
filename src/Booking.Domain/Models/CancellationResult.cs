using System;
using System.Collections.Generic;
using System.Text;

namespace Booking.Domain.Models
{
    internal class CancellationResult
    {
    }
}
public record CancellationResult(
    decimal Total,
    decimal FeePercentage,
    decimal CancellationFee);