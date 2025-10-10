using Core = XperienceCommunity.Commerce.PaymentProviders.Core;
using XperienceCommunity.Commerce.PaymentProviders.Stripe;

var builder = WebApplication.CreateBuilder(args);

// Configure Stripe
builder.Services.AddXperienceStripeCheckout(opts =>
{
    opts.ApiKey = Environment.GetEnvironmentVariable("STRIPE_API_KEY") ?? "";
    opts.WebhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
});

// Demo IOrderPayments implementation (logs only)
builder.Services.AddSingleton<Core.IOrderPayments, ConsoleOrderPayments>();

var app = builder.Build();

// Start checkout: create session and redirect
app.MapPost("/checkout/start", async (Core.IPaymentGateway gateway) =>
{
    var order = new Core.OrderSnapshot(
        OrderNumber: "ORD-1001",
        AmountMinor: 1299,
        Currency: "GBP",
        CustomerEmail: "user@example.com",
        SuccessUrl: new Uri("https://example.com/success"),
        CancelUrl: new Uri("https://example.com/cancel"));

    var result = await gateway.CreateOrReuseSessionAsync(order);
    return Results.Redirect(result.RedirectUrl.ToString());
});

// Webhooks endpoint: parse & verify only; map & persist in host app
app.MapPost("/webhooks/stripe", async (HttpRequest req, Core.IPaymentGateway gateway, Core.IOrderPayments orders) =>
{
    var parsed = await gateway.HandleWebhookAsync(req);
    if (!parsed.Handled) return Results.BadRequest();

    // Example mapping (host app responsibility in MVP):
    // In real app, perform idempotency gate here (UNIQUE(provider, eventId)).
    // Then map event type by inspecting Stripe-Signature + raw body if needed.
    // For demo, assume success if handled.
    if (!string.IsNullOrEmpty(parsed.OrderNumber))
    {
        await orders.SetStateAsync(parsed.OrderNumber, Core.PaymentState.Processing, providerRef: null);
    }
    return Results.Ok();
});

app.Run();

// Demo IOrderPayments
public sealed class ConsoleOrderPayments : Core.IOrderPayments
{
    public Task SetStateAsync(string orderNumber, Core.PaymentState state, string? providerRef = null, CancellationToken ct = default)
    {
        Console.WriteLine($"[OrderPayments] {orderNumber} -> {state} (ref: {providerRef ?? "-"})");
        return Task.CompletedTask;
    }
}
