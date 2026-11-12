using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace OrderService.SyncDataServices.Grpc;

internal static class GrpcTimeout
{
    // GrpcTimeoutInSeconds, 5 by default.
    public static TimeSpan From(IConfiguration configuration) =>
        TimeSpan.FromSeconds(
            double.TryParse(
                configuration["GrpcTimeoutInSeconds"],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds
            )
                ? seconds
                : 5
        );
}
