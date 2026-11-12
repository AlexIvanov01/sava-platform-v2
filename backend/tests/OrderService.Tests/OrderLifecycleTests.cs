using OrderService.Models;
using OrderService.Ordering;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class OrderLifecycleTests
{
    private const string HasInvoice = "The order already has an invoice.";
    private const string ReopenWithInvoice =
        "A cancelled order with an invoice can't be reopened; delete the invoice first.";

    private static Order OrderIn(OrderStatus status, bool withInvoice)
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        order.Status = status;
        if (withInvoice)
        {
            order.Invoice = new Invoice
            {
                OrderId = order.Id,
                Order = order,
                InvoiceDate = TestData.Date(2025, 2026),
                InvoiceStatus = "Unpaid",
                TotalAmount = order.Items.Gross(),
            };
        }
        return order;
    }

    [Theory]
    [InlineData(OrderStatus.InProcess, true)]
    [InlineData(OrderStatus.Pending, true)]
    [InlineData(OrderStatus.Completed, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    public void IsEditable_OnlyInProcessAndPending(OrderStatus status, bool editable)
    {
        OrderIn(status, withInvoice: false).IsEditable().ShouldBe(editable);
        OrderIn(status, withInvoice: true).IsEditable().ShouldBe(editable);
    }

    // Every (from, to, has invoice) combination and the expected refusal (null = allowed). A change
    // to the current status is refused here; the controller answers it as a no-op before asking.
    public static TheoryData<OrderStatus, OrderStatus, bool, string?> Matrix()
    {
        var data = new TheoryData<OrderStatus, OrderStatus, bool, string?>();
        foreach (var from in Enum.GetValues<OrderStatus>())
        {
            foreach (var to in Enum.GetValues<OrderStatus>())
            {
                foreach (var withInvoice in new[] { false, true })
                {
                    data.Add(from, to, withInvoice, Expected(from, to, withInvoice));
                }
            }
        }
        return data;
    }

    private static string? Expected(OrderStatus from, OrderStatus to, bool withInvoice)
    {
        var refused = $"An order can't go from {from} to {to}.";
        return (from, to) switch
        {
            (OrderStatus.InProcess, OrderStatus.Pending) => null,
            (OrderStatus.Pending, OrderStatus.InProcess) => null,
            (OrderStatus.InProcess, OrderStatus.Completed) => withInvoice ? HasInvoice : null,
            (OrderStatus.Pending, OrderStatus.Completed) => withInvoice ? HasInvoice : null,
            (OrderStatus.InProcess, OrderStatus.Cancelled) => null,
            (OrderStatus.Pending, OrderStatus.Cancelled) => null,
            (OrderStatus.Completed, OrderStatus.Cancelled) => null,
            (OrderStatus.Cancelled, OrderStatus.Pending) => withInvoice ? ReopenWithInvoice : null,
            _ => refused,
        };
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void CheckTransition_FullMatrix(
        OrderStatus from,
        OrderStatus to,
        bool withInvoice,
        string? expected
    )
    {
        var order = OrderIn(from, withInvoice);

        OrderLifecycle.CheckTransition(order, to).ShouldBe(expected);
        order.Status.ShouldBe(from);
    }

    [Fact]
    public void Matrix_CoversEveryPair()
    {
        var statuses = Enum.GetValues<OrderStatus>();
        statuses.ShouldBe([
            OrderStatus.InProcess,
            OrderStatus.Pending,
            OrderStatus.Completed,
            OrderStatus.Cancelled,
        ]);
        Matrix().Count.ShouldBe(statuses.Length * statuses.Length * 2);
    }
}
