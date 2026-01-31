namespace AtlasX.Infrastructure;

public enum OutboxStatus
{
    Pending = 0,
    InFlight = 1,
    Published = 2,
    Failed = 3
}
