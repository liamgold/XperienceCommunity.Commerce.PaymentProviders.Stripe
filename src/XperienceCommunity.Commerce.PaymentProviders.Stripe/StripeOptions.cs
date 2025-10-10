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
