# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a NuGet package providing Stripe Checkout integration for Xperience by Kentico commerce solutions. It implements the `IPaymentGateway` interface from the Commerce Payment Providers Core abstractions, enabling hosted checkout sessions, webhook verification, and payment state management.

**Key Components:**
- `StripeGateway`: Main implementation of `IPaymentGateway` that handles session creation and webhook processing
- `StripeOptions`: Configuration class for API key and webhook secret
- `ServiceCollectionExtensions`: DI registration with startup validation

## Build Commands

**Build the solution:**
```bash
dotnet build
```

**Build in Release mode:**
```bash
dotnet build --configuration Release
```

**Run tests:**
```bash
dotnet test
```

**Run tests with verbose output:**
```bash
dotnet test --verbosity normal
```

**Run a specific test:**
```bash
dotnet test --filter "FullyQualifiedName~Gateway_CanBeInstantiated"
```

**Create NuGet package (automatic on build):**
```bash
dotnet pack --configuration Release
```

**Restore dependencies:**
```bash
dotnet restore
```

## Architecture

### Payment Flow Architecture

1. **Session Creation Flow:**
   - Consumer calls `IPaymentGateway.CreateOrReuseSessionAsync()` with an `OrderSnapshot`
   - `StripeGateway` creates a Stripe Checkout Session using per-request `RequestOptions` with API key
   - API key is passed per-request (not globally) for thread-safe multi-tenant scenarios
   - Returns `CreateSessionResult` with redirect URL and session ID

2. **Webhook Processing Flow:**
   - Stripe sends webhook to consumer's endpoint
   - Consumer calls `IPaymentGateway.HandleWebhookAsync()` with the `HttpRequest`
   - `StripeGateway` verifies signature using `WebhookSecret` via `EventUtility.ConstructEvent()`
   - Extracts order number from event metadata or `ClientReferenceId`
   - Returns `WebhookResult` indicating success and order number
   - Consumer uses `IOrderPayments` (their implementation) to update order status

### Key Design Patterns

**Per-Request API Key Pattern:**
The gateway uses Stripe.net's `RequestOptions` to pass API keys per-request rather than setting a global API key. This is critical for thread safety and enables multi-tenant scenarios where different requests might use different Stripe accounts.

```csharp
var requestOptions = new RequestOptions { ApiKey = _options.ApiKey };
var session = await _sessionService.CreateAsync(sessionOptions, requestOptions, ct);
```

**Webhook Security:**
- Signature verification is mandatory if `WebhookSecret` is configured
- Request body buffering is enabled to read the body without consuming the stream
- Validation uses Stripe's `EventUtility.ConstructEvent()` which throws on invalid signatures
- Unsupported event types return `WebhookResult(false, null)` but are logged

**Order Number Extraction:**
Order numbers are extracted from different event types using a pattern match:
- `checkout.session.completed`: Prefers `ClientReferenceId`, falls back to metadata
- `payment_intent.*`: Uses metadata `orderNumber` field
- `charge.refunded`, `refund.updated`: Uses charge metadata

### Dependencies

**Core Dependency:**
- `XperienceCommunity.Commerce.PaymentProviders.Core` (0.1.0-beta): Provides `IPaymentGateway`, `IOrderPayments`, `OrderSnapshot`, and result types

**Stripe Integration:**
- `Stripe.net` (v49.0.0): Official Stripe SDK for session creation and webhook verification

**Framework Dependencies:**
- Targets .NET 8.0
- Uses Central Package Management (Directory.Packages.props)
- Nullable reference types enabled with warnings as errors

## Project Structure

```
src/XperienceCommunity.Commerce.PaymentProviders.Stripe/
  ├── StripeGateway.cs           # Main gateway implementation
  ├── StripeOptions.cs           # Configuration options
  └── DI/
      └── ServiceCollectionExtensions.cs  # DI registration with validation

tests/Stripe.UnitTests/
  ├── CheckoutTests.cs           # Lightweight instantiation tests
  └── WebhookTests.cs            # Webhook processing tests

examples/DancingGoat/            # Full working example with Xperience by Kentico
```

## Configuration Requirements

**Validation at Startup:**
The `AddXperienceStripeCheckout()` extension method validates that `ApiKey` is not null or whitespace using `.ValidateOnStart()`. The application will fail to start with a clear error message if configuration is missing.

**WebhookSecret Behavior:**
If `WebhookSecret` is not configured, webhook signature validation is skipped with a warning log. This is not recommended for production but allows local development without webhook setup.

## Testing Notes

**Current Test Coverage:**
Tests are lightweight and focus on instantiation and method signatures rather than actual Stripe API calls. This avoids requiring test API keys or mocking the Stripe SDK.

**Running Tests Locally:**
No special configuration needed - tests don't make real API calls.

**Adding New Tests:**
When adding tests that interact with Stripe APIs, consider using Stripe's test mode keys and testing against their test environment, or use mocking frameworks to avoid external dependencies.

## DancingGoat Example

The `/examples/DancingGoat` directory contains a complete working implementation showing:
- Full checkout controller implementation
- Webhook endpoint with signature verification
- `IOrderPayments` implementation using Kentico's `OrderInfo` and `OrderStatusInfo`
- Configuration via appsettings.json with user secrets for keys

Reference this example when implementing the payment gateway in a new Xperience by Kentico project.

## Important Development Notes

**Supported Webhook Events:**
Only these Stripe events are handled:
- `checkout.session.completed`
- `payment_intent.succeeded`
- `payment_intent.payment_failed`
- `charge.refunded`
- `refund.updated`

Any other event type will return `WebhookResult(false, null)` with a warning log.

**Security Considerations:**
- Never commit API keys or webhook secrets to source control
- Always use `WebhookSecret` in production to verify webhook authenticity
- The package uses structured logging - ensure sensitive data is not logged

**Package Versioning:**
Current version is `0.1.0-beta`. Follow semantic versioning for updates. Package is automatically generated on build via `GeneratePackageOnBuild`.
