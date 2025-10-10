# XperienceCommunity.Commerce.PaymentProviders.Stripe
Minimal hosted Stripe Checkout + webhook parsing for Xperience by Kentico.
- Redirect users to Stripe Checkout (CreateOrReuseSessionAsync)
- Parse & verify webhooks (HandleWebhookAsync)
- Host app persists state via IOrderPayments (see sample)
- Idempotency: implement UNIQUE(provider,eventId) in your app's DB
Quickstart:
1) services.AddXperienceStripeCheckout(opts => { opts.ApiKey=env("STRIPE_API_KEY"); opts.WebhookSecret=env("STRIPE_WEBHOOK_SECRET"); });
2) POST /checkout/start -> 302 to Stripe
3) POST /webhooks/stripe -> parse then persist state in app
