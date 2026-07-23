namespace MomoQuant.Domain.Execution;

using MomoQuant.Domain.Enums;

public interface IExecutionProvider
{
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken);
    Task<OrderStatus> GetOrderStatusAsync(string orderId, CancellationToken cancellationToken);
    Task<CancelOrderResult> CancelOrderAsync(string orderId, CancellationToken cancellationToken);
}
