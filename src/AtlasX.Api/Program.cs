using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { service = "AtlasX", status = "OK" }));

app.MapPost("/api/orders", ([FromBody] PlaceOrderRequest req) =>
{
    var orderId = Guid.NewGuid();

    return Results.Accepted($"/api/orders/{orderId}", new
    {
        orderId,
        req.symbol,
        req.side,
        req.type,
        req.quantity,
        req.price,
        receivedAtUtc = DateTime.UtcNow
    });
});

app.Run();

public record PlaceOrderRequest(
    string symbol,
    string side,
    string type,
    decimal quantity,
    decimal? price
);
