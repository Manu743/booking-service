using System;
using System.Collections.Generic;
using System.Text;

namespace Booking.Domain.Models;

public record PricingResult(
    IReadOnlyList<PricingSegment> Segments,
    decimal Subtotal);
