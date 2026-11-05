using System;
using System.Collections.Generic;
using System.Text;
using Booking.Domain.Models;

namespace Booking.Domain.Services
{
    internal class CancellationPolicy
    {
    }
}

public class CancellationPolicy
{
    private readonly TimeProvider _timeProvider;

    public CancellationPolicy(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CancellationResult Calculate(
        DateTime start,
        decimal total)
    {
        if (total < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(total),
                "Total cannot be negative.");
        }
        
        var now = _timeProvider.GetLocalNow().DateTime;

        var remaining = start - now;

        if (remaining <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "A booking cannot be cancelled after it has started.");
        }

        decimal feePercentage;
        
        if (remaining >= TimeSpan.FromHours(48))
        {
            feePercentage = 0m;
        }
        else if (remaining >= TimeSpan.FromHours(24))
        {
            feePercentage = 0.50m;
        }
        else
        {
            feePercentage = 1.00m;
        }

        var fee = Math.Round(
            total * feePercentage,
            2,
            MidpointRounding.AwayFromZero);

        return new CancellationResult(
            total,
            feePercentage,
            fee);
    }
}