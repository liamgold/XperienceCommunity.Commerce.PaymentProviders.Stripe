using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using XperienceCommunity.Commerce.PaymentProviders.Core;

namespace DancingGoat.Commerce.Payment;

/// <summary>
/// Controller for handling Stripe webhook callbacks.
/// </summary>
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
        logger.LogInformation("Stripe webhook received");

        var result = await paymentGateway.HandleWebhookAsync(Request, cancellationToken);

        if (!result.Handled)
        {
            logger.LogWarning("Stripe webhook not handled. This could be due to: missing WebhookSecret, invalid signature, or unsupported event type");
            return BadRequest("Webhook not handled");
        }

        logger.LogInformation("Stripe webhook handled successfully. OrderNumber: {OrderNumber}", result.OrderNumber);

        if (result.OrderNumber != null)
        {
            // Update order payment state based on webhook event type
            // For now, we'll mark it as succeeded. In production, you'd want to
            // parse the event type and set the appropriate state
            await orderPayments.SetStateAsync(
                result.OrderNumber,
                PaymentState.Succeeded,
                providerRef: null,
                cancellationToken);

            logger.LogInformation("Order {OrderNumber} payment state set to Succeeded", result.OrderNumber);
        }

        return Ok();
    }
}
