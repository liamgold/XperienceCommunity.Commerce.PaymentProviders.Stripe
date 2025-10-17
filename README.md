# Xperience Community: Commerce Payment Providers - Stripe

[![NuGet](https://img.shields.io/nuget/v/XperienceCommunity.Commerce.PaymentProviders.Stripe.svg)](https://www.nuget.org/packages/XperienceCommunity.Commerce.PaymentProviders.Stripe)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Stripe Checkout integration for Xperience by Kentico commerce solutions using the [Commerce Payment Providers Core](https://www.nuget.org/packages/XperienceCommunity.Commerce.PaymentProviders.Core) abstractions.

## Overview

This package provides a production-ready integration with Stripe's Hosted Checkout, allowing you to accept payments in your Xperience by Kentico commerce site. It handles session creation, webhook verification, and payment state management.

**Features:**
- ✅ Stripe Hosted Checkout session creation
- ✅ Secure webhook signature verification
- ✅ Support for multiple payment events (checkout.session.completed, payment_intent.succeeded, etc.)
- ✅ Thread-safe per-request API key handling
- ✅ Structured logging with `ILogger`
- ✅ Configuration validation at startup
- ✅ Built on Stripe.net SDK v49.0.0

## Installation

```bash
dotnet add package XperienceCommunity.Commerce.PaymentProviders.Stripe
dotnet add package XperienceCommunity.Commerce.PaymentProviders.Core
```

## Quick Start

### 1. Configure Services

Add Stripe to your `Program.cs`:

```csharp
using XperienceCommunity.Commerce.PaymentProviders.Stripe;

builder.Services.AddXperienceStripeCheckout(options =>
{
    options.ApiKey = builder.Configuration["Stripe:ApiKey"]
        ?? Environment.GetEnvironmentVariable("STRIPE_API_KEY")
        ?? throw new InvalidOperationException("Stripe API key not configured");

    options.WebhookSecret = builder.Configuration["Stripe:WebhookSecret"]
        ?? Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
});

// Register your IOrderPayments implementation
builder.Services.AddScoped<IOrderPayments, OrderPaymentsService>();
```

### 2. Add Configuration

In `appsettings.json`:

```json
{
  "Stripe": {
    "ApiKey": "sk_test_...",
    "WebhookSecret": "whsec_..."
  }
}
```

**Security:** Never commit API keys to source control. Use:
- User Secrets for development
- Environment variables for production
- Azure Key Vault or similar for enterprise deployments

### 3. Create Checkout Sessions

Inject `IPaymentGateway` in your controller:

```csharp
using XperienceCommunity.Commerce.PaymentProviders.Core;

public class CheckoutController : Controller
{
    private readonly IPaymentGateway paymentGateway;

    public CheckoutController(IPaymentGateway paymentGateway)
    {
        this.paymentGateway = paymentGateway;
    }

    [HttpPost]
    public async Task<IActionResult> Pay(string orderNumber, decimal totalAmount)
    {
        var order = new OrderSnapshot(
            OrderNumber: orderNumber,
            AmountMinor: (long)(totalAmount * 100), // Convert to cents
            Currency: "GBP",
            CustomerEmail: "customer@example.com",
            SuccessUrl: new Uri($"{Request.Scheme}://{Request.Host}/payment-success"),
            CancelUrl: new Uri($"{Request.Scheme}://{Request.Host}/payment-cancelled")
        );

        var result = await paymentGateway.CreateOrReuseSessionAsync(order);

        return Redirect(result.RedirectUrl.ToString());
    }
}
```

### 4. Handle Webhooks

Create a webhook controller:

```csharp
using XperienceCommunity.Commerce.PaymentProviders.Core;

[ApiController]
[Route("api/webhooks/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly IPaymentGateway paymentGateway;
    private readonly IOrderPayments orderPayments;
    private readonly ILogger<StripeWebhookController> logger;

    public StripeWebhookController(
        IPaymentGateway paymentGateway,
        IOrderPayments orderPayments,
        ILogger<StripeWebhookController> logger)
    {
        this.paymentGateway = paymentGateway;
        this.orderPayments = orderPayments;
        this.logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook(CancellationToken cancellationToken)
    {
        var result = await paymentGateway.HandleWebhookAsync(Request, cancellationToken);

        if (!result.Handled)
        {
            logger.LogWarning("Webhook not handled");
            return BadRequest("Webhook not handled");
        }

        if (result.OrderNumber != null)
        {
            await orderPayments.SetStateAsync(
                result.OrderNumber,
                PaymentState.Succeeded,
                cancellationToken: cancellationToken);
        }

        return Ok();
    }
}
```

### 5. Implement IOrderPayments

Create a service to update order status in Kentico:

```csharp
using CMS.Commerce;
using CMS.DataEngine;
using XperienceCommunity.Commerce.PaymentProviders.Core;

public class OrderPaymentsService : IOrderPayments
{
    private readonly IInfoProvider<OrderInfo> orderInfoProvider;
    private readonly IInfoProvider<OrderStatusInfo> orderStatusInfoProvider;
    private readonly ILogger<OrderPaymentsService> logger;

    public OrderPaymentsService(
        IInfoProvider<OrderInfo> orderInfoProvider,
        IInfoProvider<OrderStatusInfo> orderStatusInfoProvider,
        ILogger<OrderPaymentsService> logger)
    {
        this.orderInfoProvider = orderInfoProvider;
        this.orderStatusInfoProvider = orderStatusInfoProvider;
        this.logger = logger;
    }

    public async Task SetStateAsync(
        string orderNumber,
        PaymentState state,
        string? providerRef = null,
        CancellationToken ct = default)
    {
        // Find order
        var order = (await orderInfoProvider
            .Get()
            .WhereEquals(nameof(OrderInfo.OrderNumber), orderNumber)
            .TopN(1)
            .GetEnumerableTypedResultAsync(ct))
            .FirstOrDefault();

        if (order == null)
        {
            logger.LogWarning("Order {OrderNumber} not found", orderNumber);
            return;
        }

        // Map payment state to order status
        var statusCodeName = state switch
        {
            PaymentState.Succeeded => "PaymentReceived",
            PaymentState.Failed => "PaymentFailed",
            _ => "Pending"
        };

        var orderStatus = await orderStatusInfoProvider.GetAsync(statusCodeName, ct);

        if (orderStatus != null)
        {
            order.OrderOrderStatusID = orderStatus.OrderStatusID;
            await orderInfoProvider.SetAsync(order, ct);

            logger.LogInformation(
                "Order {OrderNumber} updated to status {Status}",
                orderNumber,
                statusCodeName);
        }
    }
}
```

## Configuration

### Stripe Options

| Property | Required | Description |
|----------|----------|-------------|
| `ApiKey` | Yes | Your Stripe secret API key (starts with `sk_`) |
| `WebhookSecret` | Recommended | Webhook signing secret (starts with `whsec_`) for signature verification |

**Note:** If `WebhookSecret` is not provided, webhook signature validation will be skipped. This is **not recommended** for production.

### Getting Your API Keys

1. **API Key:**
   - Go to https://dashboard.stripe.com/test/apikeys
   - Copy your "Secret key" (starts with `sk_test_` for test mode)

2. **Webhook Secret:**
   - Go to https://dashboard.stripe.com/test/webhooks
   - Click "Add endpoint"
   - URL: `https://yoursite.com/api/webhooks/stripe`
   - Select events: `checkout.session.completed`, `payment_intent.succeeded`, `payment_intent.payment_failed`
   - Click "Add endpoint"
   - Copy the "Signing secret" (starts with `whsec_`)

## Supported Webhook Events

The gateway handles the following Stripe events:

| Event | Description | PaymentState |
|-------|-------------|--------------|
| `checkout.session.completed` | Checkout session completed | Succeeded |
| `payment_intent.succeeded` | Payment succeeded | Succeeded |
| `payment_intent.payment_failed` | Payment failed | Failed |
| `charge.refunded` | Charge was refunded | Refunded |
| `refund.updated` | Refund was updated | Refunded |

Order numbers are extracted from:
1. `ClientReferenceId` (for checkout sessions)
2. Metadata `orderNumber` field (fallback)

## Security Best Practices

### 1. Webhook Signature Verification

Always configure `WebhookSecret` to verify webhook authenticity:

```csharp
options.WebhookSecret = builder.Configuration["Stripe:WebhookSecret"]; // Required!
```

Without this, **anyone** could send fake webhook requests to your endpoint.

### 2. API Key Management

**Never** hard-code API keys:

```csharp
// ❌ BAD - Don't do this!
options.ApiKey = "sk_test_abc123...";

// ✅ GOOD - Use configuration
options.ApiKey = builder.Configuration["Stripe:ApiKey"];

// ✅ BETTER - Use environment variables
options.ApiKey = Environment.GetEnvironmentVariable("STRIPE_API_KEY");

// ✅ BEST - Use Azure Key Vault or similar
options.ApiKey = await keyVaultClient.GetSecretAsync("stripe-api-key");
```

### 3. Idempotency

Stripe may send the same webhook multiple times. Implement idempotency in your `IOrderPayments`:

```csharp
public async Task SetStateAsync(string orderNumber, PaymentState state, ...)
{
    // Check if already processed using webhook event ID
    // Store event IDs in database with UNIQUE constraint
    // Only process if not seen before
}
```

### 4. HTTPS Only

**Always** use HTTPS for webhooks in production. Stripe requires HTTPS endpoints.

## Sample Application

See the complete working example in the `/examples/DancingGoat` directory:
- Full Xperience by Kentico Dancing Goat site with Stripe integration
- Complete checkout flow implementation
- Webhook handling with signature verification
- Order status updates using Kentico's OrderInfo
- Real-world IOrderPayments implementation

## Troubleshooting

### "Webhook not handled" errors

**Causes:**
1. `WebhookSecret` not configured
2. Invalid webhook signature (wrong secret)
3. Unsupported event type

**Solution:**
- Check logs for specific error messages
- Verify `WebhookSecret` matches your Stripe dashboard
- Ensure you're sending supported event types

### "Order not found" warnings

**Causes:**
1. Order number mismatch
2. Order not created before webhook received
3. Incorrect metadata configuration

**Solution:**
- Ensure `orderNumber` is included in session metadata
- Verify order exists in database before Stripe redirect

### Configuration validation errors

**Causes:**
- Missing or empty `ApiKey`

**Solution:**
- Application will fail fast at startup with clear error message
- Check configuration sources (appsettings.json, environment variables)

## Migration from Previous Versions

This is the initial release. No migration needed!

## Related Packages

- [XperienceCommunity.Commerce.PaymentProviders.Core](https://www.nuget.org/packages/XperienceCommunity.Commerce.PaymentProviders.Core) - Core abstractions (required)

## Contributing

This is a community package. Contributions are welcome!

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Resources

- [Stripe API Documentation](https://stripe.com/docs/api)
- [Stripe Checkout Guide](https://stripe.com/docs/payments/checkout)
- [Stripe Webhooks Guide](https://stripe.com/docs/webhooks)
- [Xperience by Kentico Commerce Documentation](https://docs.kentico.com/documentation/developers-and-admins/digital-commerce-setup)
- [GitHub Repository](https://github.com/liamgold/XperienceCommunity.Commerce.PaymentProviders.Stripe)
- [Report Issues](https://github.com/liamgold/XperienceCommunity.Commerce.PaymentProviders.Stripe/issues)
