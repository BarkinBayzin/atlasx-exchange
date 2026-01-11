using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AtlasX.Api;

internal static class Observability
{
    internal const string ActivitySourceName = "AtlasX.Matching";
    internal const string MeterName = "AtlasX.Api";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    internal static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> OrdersPlaced =
        Meter.CreateCounter<long>("orders_placed_total");

    internal static readonly Counter<long> TradesExecuted =
        Meter.CreateCounter<long>("trades_executed_total");

    internal static readonly Histogram<double> OrderProcessingMs =
        Meter.CreateHistogram<double>("order_processing_ms");
}
