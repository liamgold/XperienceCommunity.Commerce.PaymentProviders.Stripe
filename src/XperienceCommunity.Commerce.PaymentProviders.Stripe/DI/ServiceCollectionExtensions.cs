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

            // Add validation for StripeOptions
            services.AddOptions<StripeOptions>()
                .Validate(opts => !string.IsNullOrWhiteSpace(opts.ApiKey),
                    "Stripe API key must be configured. Set StripeOptions.ApiKey with your secret key.")
                .ValidateOnStart();

            services.AddScoped<Core.IPaymentGateway, StripeGateway>();
            return services;
        }
    }
}
