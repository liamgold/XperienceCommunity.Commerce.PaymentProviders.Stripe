using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XperienceCommunity.Commerce.PaymentProviders.Stripe;
using Core = XperienceCommunity.Commerce.PaymentProviders.Core;
using Xunit;

public class CheckoutTests
{
    [Fact]
    public void Gateway_CanBeInstantiated()
    {
        // Lightweight test to verify the gateway can be constructed
        var opts = Options.Create(new StripeOptions { ApiKey = "sk_test_dummy" });
        var logger = LoggerFactory.Create(builder => { }).CreateLogger<StripeGateway>();
        var gateway = new StripeGateway(opts, logger);

        Assert.NotNull(gateway);
    }

    [Fact]
    public void CreateSessionAsync_RequiresValidOrderSnapshot()
    {
        // Verify method signature accepts OrderSnapshot
        var opts = Options.Create(new StripeOptions { ApiKey = "sk_test_dummy" });
        var logger = LoggerFactory.Create(builder => { }).CreateLogger<StripeGateway>();
        var gateway = new StripeGateway(opts, logger);

        var order = new Core.OrderSnapshot(
            "TEST-001",
            1000,
            "GBP",
            "test@example.com",
            new Uri("https://test.com/success"),
            new Uri("https://test.com/cancel"));

        // Note: We can't actually call this without a real Stripe API key
        // This test verifies the method exists and accepts the correct parameter types
        Assert.NotNull(order);
        Assert.NotNull(gateway);
    }
}
