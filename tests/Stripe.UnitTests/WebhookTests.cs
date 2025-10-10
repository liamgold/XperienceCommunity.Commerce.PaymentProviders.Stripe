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
