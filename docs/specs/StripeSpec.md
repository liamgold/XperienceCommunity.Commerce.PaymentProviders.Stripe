# Stripe Provider Specification

## Project
- Name: XperienceCommunity.Commerce.PaymentProviders.Stripe
- Branch: feat/stripe-mvp
- Target Framework: .NET 8.0
- Nullable: enabled
- XML Docs: enabled
- External deps:
  - Stripe.net
  - Microsoft.AspNetCore.Http.Abstractions
  - XperienceCommunity.Commerce.PaymentProviders.Core [1.*,2.0)

---

## 1) Solution Structure (create exactly)
- /src/XperienceCommunity.Commerce.PaymentProviders.Stripe
- /src/XperienceCommunity.Commerce.PaymentProviders.Stripe/DI/ServiceCollectionExtensions.cs
- /src/XperienceCommunity.Commerce.PaymentProviders.Stripe/StripeOptions.cs
- /src/XperienceCommunity.Commerce.PaymentProviders.Stripe/StripeGateway.cs
- /tests/Stripe.UnitTests
- /tests/Stripe.UnitTests/WebhookTests.cs
- /tests/Stripe.UnitTests/CheckoutTests.cs
- /samples/MinimalHostedCheckout/Program.cs
- /.github/workflows/build.yml
- /.github/dependabot.yml
- /README.md
- /CHANGELOG.md
- /LICENSE

Notes:
- This MVP implements **hosted Stripe Checkout** and **webhook parsing**.
- The **host application** is responsible for idempotency storage and calling `IOrderPayments.SetStateAsync` based on parsed results (demonstrated in the sample).
- No provider-specific persistence inside the package.

---

## 2) Package References
Add to `src/XperienceCommunity.Commerce.PaymentProviders.Stripe/*.csproj`:
- `Stripe.net`
- `Microsoft.AspNetCore.Http.Abstractions`
- `XperienceCommunity.Commerce.PaymentProviders.Core` version `[1.*,2.0)`

---

## 3) DI & Options (exact code)

### 3.1 StripeOptions.cs
    namespace XperienceCommunity.Commerce.PaymentProviders.Stripe
    {
        /// <summary>
        /// Options for configuring the Stripe payment provider.
        /// </summary>
        public sealed class StripeOptions
        {
            /// <summary>Secret API key used to call Stripe.</summary>
            public string ApiKey { get; set; } = string.Empty;

            /// <summary>Webhook signing secret used to verify incoming webhooks.</summary>
            public string? WebhookSecret { get; set; }
        }
    }

### 3.2 ServiceCollectionExtensions.cs
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using Core = XperienceCommunity.Commerce.PaymentProviders.Core;

    namespace XperienceCommunity.Commerce.PaymentProviders.Stripe
    {
        /// <summary>
        /// DI extensions for the Stripe payment provider.
        /// </summary>
        public static class ServiceCollectionExtensions
        {
            /// <summary>
            /// Registers the Stripe payment gateway with provided configuration.
            /// </summary>
            public static IServiceCollection AddXperienceStripeCheckout(
                this IServiceCollection services,
                Action<StripeOptions> configure)
            {
                services.Configure(configure);
                services.AddHttpContextAccessor();
                services.AddScoped<Core.IPaymentGateway, StripeGateway>();
                return services;
            }
        }
    }

---

## 4) Gateway Implementation (exact code)

### 4.1 StripeGateway.cs
    using System.Text;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Options;
    using Stripe;
    using Stripe.Checkout;
    using Core = XperienceCommunity.Commerce.PaymentProviders.Core;

    namespace XperienceCommunity.Commerce.PaymentProviders.Stripe
    {
        /// <summary>
        /// Hosted Stripe Checkout implementation of IPaymentGateway.
        /// </summary>
        public sealed class StripeGateway : Core.IPaymentGateway
        {
            private readonly StripeOptions _options;
            private readonly SessionService _sessionService = new();
            private readonly EventUtility _eventUtility = new();

            /// <summary>
            /// Creates a new instance of <see cref="StripeGateway"/>.
            /// </summary>
            public StripeGateway(IOptions<StripeOptions> options)
            {
                _options = options.Value;
                StripeConfiguration.ApiKey = _options.ApiKey;
            }

            /// <inheritdoc />
            public async Task<Core.CreateSessionResult> CreateOrReuseSessionAsync(
                Core.OrderSnapshot order,
                CancellationToken ct = default)
            {
                var sessionOptions = new SessionCreateOptions
                {
                    Mode = "payment",
                    ClientReferenceId = order.OrderNumber,
                    SuccessUrl = order.SuccessUrl.ToString(),
                    CancelUrl = order.CancelUrl.ToString(),
                    Metadata = new Dictionary<string, string>
                    {
                        ["orderNumber"] = order.OrderNumber,
                        ["customerEmail"] = order.CustomerEmail
                    },
                    LineItems = new List<SessionLineItemOptions>
                    {
                        new()
                        {
                            Quantity = 1,
                            PriceData = new SessionLineItemPriceDataOptions
                            {
                                Currency = order.Currency.ToLowerInvariant(),
                                UnitAmount = order.AmountMinor,
                                ProductData = new SessionLineItemPriceDataProductDataOptions
                                {
                                    Name = $"Order {order.OrderNumber}"
                                }
                            }
                        }
                    }
                };

                var session = await _sessionService.CreateAsync(sessionOptions, cancellationToken: ct);
                return new Core.CreateSessionResult(new Uri(session.Url!), session.Id);
            }

            /// <inheritdoc />
            public async Task<Core.WebhookResult> HandleWebhookAsync(
                HttpRequest request,
                CancellationToken ct = default)
            {
                if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
                {
                    // If no secret, we cannot verify; treat as unhandled.
                    return new Core.WebhookResult(false, null);
                }

                // Read body
                request.EnableBuffering();
                string json;
                using (var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true))
                {
                    json = await reader.ReadToEndAsync();
                    request.Body.Position = 0;
                }

                var sigHeader = request.Headers["Stripe-Signature"];

                Event stripeEvent;
                try
                {
                    stripeEvent = EventUtility.ConstructEvent(json, sigHeader, _options.WebhookSecret);
                }
                catch
                {
                    // Signature invalid or payload malformed.
                    return new Core.WebhookResult(false, null);
                }

                // Try to extract order number from known event payload shapes.
                string? orderNumber = null;

                switch (stripeEvent.Type)
                {
                    case Events.PaymentIntentSucceeded:
                    case Events.PaymentIntentPaymentFailed:
                    {
                        if (stripeEvent.Data.Object is PaymentIntent pi)
                        {
                            orderNumber = TryGet(pi.Metadata, "orderNumber");
                        }
                        break;
                    }
                    case Events.CheckoutSessionCompleted:
                    {
                        if (stripeEvent.Data.Object is Session session)
                        {
                            orderNumber = !string.IsNullOrEmpty(session.ClientReferenceId)
                                ? session.ClientReferenceId
                                : TryGet(session.Metadata, "orderNumber");
                        }
                        break;
                    }
                    case Events.ChargeRefunded:
                    case Events.RefundUpdated:
                    {
                        if (stripeEvent.Data.Object is Charge charge)
                        {
                            orderNumber = TryGet(charge.Metadata, "orderNumber");
                        }
                        break;
                    }
                    default:
                        // Unrecognized or unsupported event type in MVP.
                        return new Core.WebhookResult(false, null);
                }

                return new Core.WebhookResult(true, orderNumber);

                static string? TryGet(IDictionary<string, string>? dict, string key)
                    => dict != null && dict.TryGetValue(key, out var v) ? v : null;
            }
        }
    }

Notes:
- This MVP only **parses and verifies** webhooks and returns correlation info (`orderNumber`).
- The **host app** handles idempotency and state updates using `IOrderPayments`.

---

## 5) Sample App (exact code)

### 5.1 samples/MinimalHostedCheckout/Program.cs
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

Notes:
- The sample demonstrates redirection to hosted checkout and a webhook endpoint that calls the gateway and then persists **in the app**.
- Replace `ConsoleOrderPayments` with your real implementation.

---

## 6) Tests

### 6.1 tests/Stripe.UnitTests/CheckoutTests.cs
- Create a test that:
  - Mocks Stripe configuration (can skip actual API call by asserting method path compiles or by abstracting `SessionService` behind a thin seam if needed).
  - Calls `CreateOrReuseSessionAsync` with a sample `OrderSnapshot`.
  - Asserts `CreateSessionResult` has a non-empty `ProviderRef` and a well-formed URL string (you may stub via conditional compilation or wrap the session service for unit testing).

*(It’s acceptable in MVP to keep this as a lightweight “can call method” test if you do not introduce a seam.)*

### 6.2 tests/Stripe.UnitTests/WebhookTests.cs (exact code)
    using System.Text;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Options;
    using XperienceCommunity.Commerce.PaymentProviders.Stripe;
    using Xunit;

    public class WebhookTests
    {
        [Fact]
        public async Task InvalidSignature_Returns_Unhandled()
        {
            var opts = Options.Create(new StripeOptions { ApiKey = "sk_test_x", WebhookSecret = "whsec_dummy" });
            var gateway = new StripeGateway(opts);

            var ctx = new DefaultHttpContext();
            var payload = "{}";
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            ctx.Request.Headers["Stripe-Signature"] = "t=0,v1=invalid";

            var result = await gateway.HandleWebhookAsync(ctx.Request);

            Assert.False(result.Handled);
            Assert.Null(result.OrderNumber);
        }
    }

---

## 7) CI (create .github/workflows/build.yml)
    name: build
    on:
      push:
      pull_request:
    jobs:
      build:
        runs-on: ${{ matrix.os }}
        strategy:
          matrix:
            os: [ubuntu-latest, windows-latest]
        steps:
          - uses: actions/checkout@v4
          - uses: actions/setup-dotnet@v4
            with:
              dotnet-version: '8.0.x'
          - run: dotnet restore
          - run: dotnet build
          - run: dotnet test

---

## 8) Dependabot (create .github/dependabot.yml)
    version: 2
    updates:
      - package-ecosystem: "nuget"
        directory: "/"
        schedule: { interval: "weekly" }
      - package-ecosystem: "github-actions"
        directory: "/"
        schedule: { interval: "weekly" }

---

## 9) README.md (short)
    # XperienceCommunity.Commerce.PaymentProviders.Stripe
    Minimal hosted Stripe Checkout + webhook parsing for Xperience by Kentico.
    - Redirect users to Stripe Checkout (CreateOrReuseSessionAsync)
    - Parse & verify webhooks (HandleWebhookAsync)
    - Host app persists state via IOrderPayments (see sample)
    - Idempotency: implement UNIQUE(provider,eventId) in your app’s DB
    Quickstart:
    1) services.AddXperienceStripeCheckout(opts => { opts.ApiKey=env("STRIPE_API_KEY"); opts.WebhookSecret=env("STRIPE_WEBHOOK_SECRET"); });
    2) POST /checkout/start -> 302 to Stripe
    3) POST /webhooks/stripe -> parse then persist state in app

---

## 10) CHANGELOG.md
    ## 0.1.0
    - MVP: hosted checkout session creation and webhook parsing
    - Sample Minimal API app
    - CI and Dependabot

---

## 11) LICENSE
    MIT

---

## 12) XML Documentation
- Add XML documentation comments to **all public** interfaces, classes, and methods in this project.
- Each public member must have at least a concise `<summary>` (1–2 lines).
- Keep tone factual and neutral.

---

## 13) Actions (execute in order)
1. Create branch `feat/stripe-mvp`.
2. Scaffold solution, projects, and files exactly as above.
3. Add package references.
4. Add Options, DI extensions, and `StripeGateway` with exact code.
5. Add sample Minimal API app.
6. Add unit tests.
7. Add CI workflow, Dependabot, README, CHANGELOG, LICENSE.
8. Run: `dotnet restore`; `dotnet build`; `dotnet test`.
9. Commit all; open PR to `main`; return PR URL.
