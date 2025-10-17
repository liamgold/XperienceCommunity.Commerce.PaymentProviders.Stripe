#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CMS.Commerce;
using CMS.DataEngine;
using Microsoft.Extensions.Logging;
using XperienceCommunity.Commerce.PaymentProviders.Core;

namespace DancingGoat.Commerce.Payment;

/// <summary>
/// Service for managing order payment states.
/// </summary>
public class OrderPaymentsService : IOrderPayments
{
    private readonly IInfoProvider<OrderInfo> orderInfoProvider;
    private readonly IInfoProvider<OrderStatusInfo> orderStatusInfoProvider;
    private readonly ILogger<OrderPaymentsService> logger;

    public OrderPaymentsService(
        IInfoProvider<OrderInfo> orderInfoProvider,
        IInfoProvider<OrderStatusInfo> orderStatusInfoProvider,
        ILogger<OrderPaymentsService> logger)
    {
        this.orderInfoProvider = orderInfoProvider;
        this.orderStatusInfoProvider = orderStatusInfoProvider;
        this.logger = logger;
    }

    public async Task SetStateAsync(
        string orderNumber,
        PaymentState state,
        string? providerRef = null,
        CancellationToken ct = default)
    {
        try
        {
            // Find the order by order number
            var order = (await orderInfoProvider
                .Get()
                .WhereEquals(nameof(OrderInfo.OrderNumber), orderNumber)
                .TopN(1)
                .GetEnumerableTypedResultAsync(cancellationToken: ct))
                .FirstOrDefault();

            if (order == null)
            {
                logger.LogWarning("Order {OrderNumber} not found when attempting to update payment state", orderNumber);
                return;
            }

            // Map payment state to order status code name
            var statusCodeName = MapPaymentStateToOrderStatus(state);

            // Retrieve the order status by code name
            var orderStatus = await orderStatusInfoProvider.GetAsync(statusCodeName, ct);

            if (orderStatus == null)
            {
                logger.LogWarning(
                    "Order status '{StatusCodeName}' not found for payment state {PaymentState}. Order {OrderNumber} status not updated.",
                    statusCodeName,
                    state,
                    orderNumber);
                return;
            }

            // Update the order status
            order.OrderOrderStatusID = orderStatus.OrderStatusID;

            // Save the order
            await orderInfoProvider.SetAsync(order, ct);

            logger.LogInformation(
                "Order {OrderNumber} status updated to {OrderStatus} (Payment state: {PaymentState}, Provider ref: {ProviderRef})",
                orderNumber,
                statusCodeName,
                state,
                providerRef);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error updating payment state for order {OrderNumber} to {PaymentState}",
                orderNumber,
                state);
            throw;
        }
    }

    /// <summary>
    /// Maps payment provider states to Kentico order status code names.
    /// These code names match the order statuses in Dancing Goat.
    /// </summary>
    private static string MapPaymentStateToOrderStatus(PaymentState state)
    {
        return state switch
        {
            PaymentState.Pending => "Pending",
            PaymentState.Processing => "Processing",
            PaymentState.Succeeded => "PaymentReceived",
            PaymentState.Failed => "PaymentFailed",
            // Dancing Goat doesn't have Refunded/PartiallyRefunded statuses,
            // so we'll map them to PaymentReceived as a fallback
            PaymentState.Refunded => "PaymentReceived",
            PaymentState.PartiallyRefunded => "PaymentReceived",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown payment state")
        };
    }
}
