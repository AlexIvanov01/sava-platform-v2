using System.Text.Json.Serialization;

namespace OrderService.Models;

// InProcess and Pending (waiting for clarification) behave the same: the order can be edited,
// deleted and completed. Completed and Cancelled orders are locked. See OrderLifecycle.
[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]
public enum OrderStatus
{
    InProcess,
    Pending,
    Completed,
    Cancelled,
}
