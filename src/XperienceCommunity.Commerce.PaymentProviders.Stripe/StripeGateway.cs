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
                case "payment_intent.succeeded":
                case "payment_intent.payment_failed":
                {
                    if (stripeEvent.Data.Object is PaymentIntent pi)
                    {
                        orderNumber = TryGet(pi.Metadata, "orderNumber");
                    }
                    break;
                }
                case "checkout.session.completed":
                {
                    if (stripeEvent.Data.Object is Session session)
                    {
                        orderNumber = !string.IsNullOrEmpty(session.ClientReferenceId)
                            ? session.ClientReferenceId
                            : TryGet(session.Metadata, "orderNumber");
                    }
                    break;
                }
                case "charge.refunded":
                case "refund.updated":
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
