using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NSubstitute.ExceptionExtensions;
using OrderService.DataAccess;
using OrderService.Mappings;
using OrderService.Models;
using OrderService.Ordering;
using OrderService.SyncDataServices.Grpc;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// PrepDB.SeedOrdersAsync against substituted gRPC clients that answer from the Customer and
// Inventory seed files.
public class OrderSeedTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    private readonly IInventoryDataClient _inventory = Substitute.For<IInventoryDataClient>();
    private readonly ICustomerDataClient _customers = Substitute.For<ICustomerDataClient>();

    private void ServeSeedData()
    {
        _inventory.Serves(SeedData.Catalog());
        foreach (var (id, customer) in SeedData.Customers())
        {
            _customers.GetCustomerAsync(id).Returns(customer);
        }
    }

    // Seeds the company settings first unless told not to, as PrepPopulation does.
    private async Task SeedOrdersAsync(string? seedFile = null, bool withCompany = true)
    {
        await using var context = Db.CreateContext();
        if (withCompany)
        {
            await PrepDB.SeedCompanySettingsAsync(context, SeedData.CompanyFile);
        }
        await PrepDB.SeedOrdersAsync(
            context,
            _inventory,
            _customers,
            seedFile ?? SeedData.OrdersFile
        );
    }

    // A copy of the order seed file with a change; deleted when the returned value is disposed.
    private static SeedFile ModifiedSeedFile(Action<JsonArray> change)
    {
        var orders = JsonNode.Parse(File.ReadAllText(SeedData.OrdersFile))!.AsArray();
        change(orders);
        var path = Path.Combine(
            Path.GetDirectoryName(SeedData.OrdersFile)!,
            $"orders-{TestData.Faker.Random.Guid():N}.json"
        );
        File.WriteAllText(path, orders.ToJsonString());
        return new SeedFile(path);
    }

    private sealed record SeedFile(string Path) : IDisposable
    {
        public void Dispose() => File.Delete(Path);
    }

    private async Task<List<Order>> LoadOrdersAsync()
    {
        await using var context = Db.CreateContext();
        return await context
            .Orders.Include(o => o.Invoice)
            .Include(o => o.Items)
                .ThenInclude(l => l.Batches)
            .AsSplitQuery()
            .ToListAsync(Ct);
    }

    [Fact]
    public async Task SeedOrdersAsync_SeedsTheOrdersWithSnapshotsOnce()
    {
        ServeSeedData();
        var seeds = SeedData.Orders();
        var catalog = SeedData.Catalog();
        var customers = SeedData.Customers();
        var company = SeedData.Company().ToEntity().ToSupplierDto();

        await SeedOrdersAsync();
        await SeedOrdersAsync();

        var orders = await LoadOrdersAsync();
        orders.Select(o => o.Id).ShouldBe(seeds.Select(s => s.Id), ignoreOrder: true);
        foreach (var seed in seeds)
        {
            var order = orders.Single(o => o.Id == seed.Id);
            order.OrderDate.ShouldBe(seed.OrderDate);
            order.ShippingAddress.ShouldBe(seed.ShippingAddress);
            order.Status.ShouldBe(seed.Status);
            order.CompletedDate.ShouldBe(seed.CompletedDate);
            order.CancelledDate.ShouldBe(seed.CancelledDate);
            order.CustomerId.ShouldBe(seed.CustomerId);
            order
                .Customer.ShouldNotBeNull()
                .ToReadDto()
                .ShouldBe(customers[seed.CustomerId].ToReadDto());
            order
                .Items.SelectMany(l => l.Batches.Select(b => (l.ProductId, b.BatchId, b.Quantity)))
                .ShouldBe(
                    seed.Items.SelectMany(l =>
                        l.Batches!.Select(b =>
                            (l.ProductId!.Value, b.BatchId!.Value, b.Quantity!.Value)
                        )
                    ),
                    ignoreOrder: true
                );
            foreach (var item in order.Items)
            {
                var product = catalog.Products[item.ProductId];
                (item.SKU, item.Name, item.UnitPrice, item.VatRate, item.Cost).ShouldBe(
                    (product.SKU, product.Name, product.ListPrice, product.VatRate, product.Cost)
                );
                item.Batches.ShouldAllBe(b => b.Lot == catalog.Batches[b.BatchId].Lot);
            }

            if (seed.Invoice == null)
            {
                order.Invoice.ShouldBeNull();
            }
            else
            {
                var invoice = order.Invoice.ShouldNotBeNull();
                invoice.Number.ShouldBe(seed.Invoice.Number);
                invoice.InvoiceDate.ShouldBe(seed.Invoice.InvoiceDate);
                invoice.TaxEventDate.ShouldBe(seed.Invoice.InvoiceDate);
                invoice.DueDate.ShouldBe(seed.Invoice.DueDate);
                invoice.PaymentMethod.ShouldBe(PaymentMethod.BankTransfer);
                invoice.PaymentStatus.ShouldBe(seed.Invoice.PaymentStatus);
                invoice.PaidDate.ShouldBe(seed.Invoice.PaidDate);
                invoice.Supplier.ToReadDto().ShouldBe(company);
                invoice.Customer.ToReadDto().ShouldBe(customers[seed.CustomerId].ToReadDto());
                invoice
                    .Items.Select(i => (i.ProductId, i.Quantity, i.Gross))
                    .ShouldBe(
                        order.Items.Select(i => (i.ProductId, i.Quantity, i.Gross())),
                        ignoreOrder: true
                    );
                (invoice.Net, invoice.Vat, invoice.Gross).ShouldBe(
                    (order.Items.Net(), order.Items.Vat(), order.Items.Gross())
                );
                invoice.VatTotals.Sum(t => t.Net).ShouldBe(invoice.Net);
            }
        }
        orders.Count(o => o.Invoice != null).ShouldBe(seeds.Count(s => s.Invoice != null));
        orders
            .Where(o => o.Status == OrderStatus.Completed)
            .ShouldAllBe(o => o.Invoice != null && o.CompletedDate != null);
        orders
            .Where(o => o.Status is OrderStatus.InProcess or OrderStatus.Pending)
            .ShouldAllBe(o => o.Invoice == null);
        // The second run found orders and didn't ask the other services again.
        await _inventory.ReceivedWithAnyArgs(1).GetOrderSnapshotAsync(default!, default!);
    }

    [Fact]
    public async Task SeedOrdersAsync_NoCompanySettings_SeedsNothingWhenTheSeedHasInvoices()
    {
        ServeSeedData();
        SeedData.Orders().ShouldContain(s => s.Invoice != null);

        await SeedOrdersAsync(withCompany: false);

        (await LoadOrdersAsync()).ShouldBeEmpty();
        _inventory.ReceivedCalls().ShouldBeEmpty();
        _customers.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task SeedOrdersAsync_NoCompanySettings_SeedsOrdersWithoutInvoices()
    {
        ServeSeedData();
        using var file = ModifiedSeedFile(orders =>
        {
            foreach (var order in orders.Where(o => o!["Invoice"] is not null).ToList())
            {
                orders.Remove(order);
            }
        });
        var expected = SeedData.Orders().Where(s => s.Invoice == null).Select(s => s.Id).ToList();

        await SeedOrdersAsync(file.Path, withCompany: false);

        var orders = await LoadOrdersAsync();
        orders.Select(o => o.Id).ShouldBe(expected, ignoreOrder: true);
        orders.ShouldAllBe(o => o.Invoice == null);
    }

    // An owned snapshot can belong to one owner only, so orders (and invoices) of the same customer
    // each need their own copy.
    [Fact]
    public async Task SeedOrdersAsync_InvoicedOrdersOfOneCustomer_EachGetTheirOwnSnapshots()
    {
        ServeSeedData();
        var invoiced = SeedData.Orders().Where(s => s.Invoice != null).Take(3).ToList();
        invoiced.Count.ShouldBe(3);
        var customerId = invoiced[0].CustomerId;
        using var file = ModifiedSeedFile(orders =>
        {
            foreach (var order in orders)
            {
                if (invoiced.Exists(s => s.Id == Guid.Parse(order!["Id"]!.GetValue<string>())))
                {
                    order!["CustomerId"] = customerId.ToString();
                }
            }
        });

        await SeedOrdersAsync(file.Path);

        var orders = (await LoadOrdersAsync()).Where(o => o.CustomerId == customerId).ToList();
        orders.Select(o => o.Id).ShouldBe(invoiced.Select(s => s.Id), ignoreOrder: true);
        var expected = SeedData.Customers()[customerId].ToReadDto();
        foreach (var order in orders)
        {
            order.Customer.ShouldNotBeNull().ToReadDto().ShouldBe(expected);
            order.Invoice.ShouldNotBeNull().Customer.ToReadDto().ShouldBe(expected);
        }
        (await LoadOrdersAsync()).Count.ShouldBe(SeedData.Orders().Count);
        await _customers.Received(1).GetCustomerAsync(customerId);
    }

    [Fact]
    public async Task SeedOrdersAsync_AsksInventoryForEveryProductAndBatchOnce()
    {
        ServeSeedData();
        var items = SeedData.Orders().SelectMany(s => s.Items).ToList();
        var productIds = items.Select(l => l.ProductId!.Value).Distinct().ToList();
        var batchIds = items
            .SelectMany(l => l.Batches!)
            .Select(b => b.BatchId!.Value)
            .Distinct()
            .ToList();

        await SeedOrdersAsync();

        await _inventory
            .Received(1)
            .GetOrderSnapshotAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Order().SequenceEqual(productIds.Order())
                ),
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Order().SequenceEqual(batchIds.Order())
                )
            );
    }

    [Theory]
    [InlineData("InventoryService")]
    [InlineData("CustomerService")]
    public async Task SeedOrdersAsync_ServiceUnavailable_SeedsNothing(string service)
    {
        ServeSeedData();
        var unavailable = new UpstreamUnavailableException(
            service,
            new InvalidOperationException()
        );
        if (service == "InventoryService")
        {
            _inventory.GetOrderSnapshotAsync(default!, default!).ThrowsAsyncForAnyArgs(unavailable);
        }
        else
        {
            _customers.GetCustomerAsync(Arg.Any<Guid>()).ThrowsAsync(unavailable);
        }

        await SeedOrdersAsync();

        (await LoadOrdersAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SeedOrdersAsync_CustomerMissing_SeedsNothing()
    {
        ServeSeedData();
        var missing = TestData.Faker.PickRandom(SeedData.Orders()).CustomerId;
        _customers.GetCustomerAsync(missing).Returns((CustomerSnapshot?)null);

        await SeedOrdersAsync();

        (await LoadOrdersAsync()).ShouldBeEmpty();
    }

    // Inventory doesn't know one of the batches (e.g. its seed data changed): no partial seed.
    [Fact]
    public async Task SeedOrdersAsync_InvalidItems_SeedsNothing()
    {
        ServeSeedData();
        var catalog = SeedData.Catalog();
        var missing = TestData
            .Faker.PickRandom(
                SeedData.Orders().SelectMany(s => s.Items).SelectMany(l => l.Batches!).ToList()
            )
            .BatchId!.Value;
        _inventory.Serves(
            OrderBuilder.Catalog(
                catalog.Products.Values,
                catalog.Batches.Values.Where(b => b.Id != missing)
            )
        );

        await SeedOrdersAsync();

        (await LoadOrdersAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SeedOrdersAsync_OrdersPresent_DoesNothing()
    {
        ServeSeedData();
        var existing = await SeedOrderAsync(1);

        await SeedOrdersAsync();

        (await LoadOrdersAsync()).ShouldHaveSingleItem().Id.ShouldBe(existing.Id);
        _inventory.ReceivedCalls().ShouldBeEmpty();
        _customers.ReceivedCalls().ShouldBeEmpty();
    }
}

// The three services' seed files share ids; the order seed only works while they agree.
public class OrderSeedFileTests
{
    [Fact]
    public void OrderIds_AreUnique()
    {
        var ids = SeedData.Orders().Select(o => o.Id).ToList();

        ids.ShouldNotBeEmpty();
        ids.ShouldBeUnique();
    }

    [Fact]
    public void Customers_ExistInTheCustomerSeed()
    {
        var customers = SeedData.Customers();

        SeedData.Orders().ShouldAllBe(o => customers.ContainsKey(o.CustomerId));
    }

    [Fact]
    public void Items_ReferenceInventoryProductsAndTheirOwnBatches()
    {
        var catalog = SeedData.Catalog();

        foreach (var order in SeedData.Orders())
        {
            order.Items.ShouldNotBeEmpty($"Order {order.Id} has no items.");
            foreach (var item in order.Items)
            {
                var productId = item.ProductId.ShouldNotBeNull();
                catalog.Products.ShouldContainKey(productId, $"Order {order.Id}");
                catalog.Products[productId].ListPrice.ShouldNotBeNull($"Product {productId}");
                item.Batches.ShouldNotBeNull().ShouldNotBeEmpty($"Order {order.Id}");
                foreach (var batch in item.Batches)
                {
                    var batchId = batch.BatchId.ShouldNotBeNull();
                    catalog.Batches.ShouldContainKey(batchId, $"Order {order.Id}");
                    catalog.Batches[batchId].ProductId.ShouldBe(productId, $"Batch {batchId}");
                    batch.Quantity.ShouldNotBeNull().ShouldBeGreaterThan(0);
                }
            }
        }
    }

    // What the API would reject (OrderCreateDto.Validate) the seed shouldn't contain either.
    [Fact]
    public void Orders_HaveNoDuplicateProductsOrBatches()
    {
        foreach (var order in SeedData.Orders())
        {
            order.Items.Select(l => l.ProductId).ShouldBeUnique($"Order {order.Id}");
            order
                .Items.SelectMany(l => l.Batches!)
                .Select(b => b.BatchId)
                .ShouldBeUnique($"Order {order.Id}");
        }
    }

    // Every status is seeded, so the development data shows the whole lifecycle.
    [Fact]
    public void Orders_CoverEveryStatus()
    {
        SeedData
            .Orders()
            .Select(o => o.Status)
            .Distinct()
            .ShouldBe(Enum.GetValues<OrderStatus>(), ignoreOrder: true);
    }

    // The seed is in a state the status endpoint could have produced: Completed orders have an
    // invoice and a CompletedDate, Cancelled ones a CancelledDate, open ones neither.
    [Fact]
    public void Orders_StatusDatesAndInvoicesAreConsistent()
    {
        foreach (var order in SeedData.Orders())
        {
            var because = $"Order {order.Id} is {order.Status}";
            switch (order.Status)
            {
                case OrderStatus.Completed:
                    order.Invoice.ShouldNotBeNull(because);
                    order.CompletedDate.ShouldNotBeNull(because);
                    order.CancelledDate.ShouldBeNull(because);
                    break;
                case OrderStatus.Cancelled:
                    order.CancelledDate.ShouldNotBeNull(because);
                    // A cancelled order may keep the invoice it got when it was completed.
                    if (order.Invoice == null)
                    {
                        order.CompletedDate.ShouldBeNull(because);
                    }
                    break;
                default:
                    order.Invoice.ShouldBeNull(because);
                    order.CompletedDate.ShouldBeNull(because);
                    order.CancelledDate.ShouldBeNull(because);
                    break;
            }
            if (order.CompletedDate is { } completed)
            {
                completed.ShouldBeGreaterThanOrEqualTo(order.OrderDate.ShouldNotBeNull(), because);
            }
            if (order.CancelledDate is { } cancelled)
            {
                cancelled.ShouldBeGreaterThanOrEqualTo(order.OrderDate.ShouldNotBeNull(), because);
            }
            if (order.Invoice is { } invoice)
            {
                invoice.InvoiceDate.ShouldBe(order.CompletedDate.ShouldNotBeNull(), because);
            }
        }
    }

    // What GET /api/Invoices/numbering and PUT /api/Invoices accept, without repeats.
    [Fact]
    public void Invoices_HaveUniqueTenDigitNumbers()
    {
        var numbers = SeedData
            .Orders()
            .Select(o => o.Invoice)
            .OfType<InvoiceSeed>()
            .Select(i => i.Number)
            .ToList();

        numbers.ShouldNotBeEmpty();
        numbers.ShouldAllBe(n => n.Length == 10 && n.All(char.IsAsciiDigit));
        numbers.ShouldBeUnique();
    }

    // A state InvoiceChanges could have produced: PaidDate set exactly when Paid.
    [Fact]
    public void Invoices_PaymentFieldsAreConsistent()
    {
        foreach (var order in SeedData.Orders().Where(o => o.Invoice != null))
        {
            var invoice = order.Invoice!;
            var because = $"Invoice {invoice.Number}";
            invoice.DueDate.ShouldBeGreaterThanOrEqualTo(invoice.InvoiceDate, because);
            if (invoice.PaymentStatus == PaymentStatus.Paid)
            {
                invoice
                    .PaidDate.ShouldNotBeNull(because)
                    .ShouldBeGreaterThanOrEqualTo(invoice.InvoiceDate, because);
            }
            else
            {
                invoice.PaidDate.ShouldBeNull(because);
            }
        }
        SeedData
            .Orders()
            .Select(o => o.Invoice?.PaymentStatus)
            .OfType<PaymentStatus>()
            .Distinct()
            .ShouldBe(Enum.GetValues<PaymentStatus>(), ignoreOrder: true);
    }

    [Fact]
    public void Orders_ApplyToTheSeedCatalogWithoutErrors()
    {
        var catalog = SeedData.Catalog();

        foreach (var seed in SeedData.Orders())
        {
            OrderItems.Apply(new Order(), seed.Items, catalog).ShouldBeEmpty($"Order {seed.Id}");
        }
    }
}
