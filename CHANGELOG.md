# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0-beta] - 2025-10-17

### Added
- Initial pre-release of Stripe payment provider integration
- Stripe Hosted Checkout session creation via `CreateOrReuseSessionAsync`
- Webhook handling with signature verification via `HandleWebhookAsync`
- Support for webhook events: `checkout.session.completed`, `payment_intent.succeeded`, `payment_intent.payment_failed`, `charge.refunded`, `refund.updated`
- Thread-safe per-request API key handling using `RequestOptions`
- Structured logging with `ILogger<StripeGateway>` integration
- Configuration validation at startup (validates API key presence)
- Order number extraction from `ClientReferenceId` and metadata
- Complete README with quick start guide, security best practices, and troubleshooting
- Minimal API sample application demonstrating full checkout flow
- Built on Stripe.net SDK v49.0.0
- CI workflow for pull requests
- Automated NuGet publishing via GitHub releases

### Notes
- This is a pre-release version intended for early adopters and feedback
- Requires `XperienceCommunity.Commerce.PaymentProviders.Core` 0.1.0-beta or later
- Host applications must implement `IOrderPayments` to persist order state changes
- Idempotency handling is delegated to host application implementation
- Compatible with .NET 8.0+ and Xperience by Kentico 29.0.0+
