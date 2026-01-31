namespace AtlasX.Infrastructure;

public sealed record OutboxRecord(
    Guid Id,
    string TypeName,
    string Payload,
    DateTimeOffset CreatedAt,
    OutboxStatus Status,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset LockedUntil,
    string? LastError);
