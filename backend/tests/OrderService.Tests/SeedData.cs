using System.Text.Json;
using OrderService.Dtos;
using OrderService.Models;
using OrderService.SyncDataServices.Grpc;

namespace OrderService.Tests;

// Reads the services' development seed files (linked into the output by the .csproj) and maps
// them to what the Order service receives over gRPC.
internal static class SeedData
{
    public static string OrdersFile => PathOf("orders.json");

    public static Dictionary<Guid, CustomerSnapshot> Customers()
    {
        using var doc = Read("customers.json");
        return doc
            .RootElement.EnumerateArray()
            .ToDictionary(
                c => c.GetProperty("Id").GetGuid(),
                c => new CustomerSnapshot
                {
                    Name = c.GetProperty("Name").GetString(),
                    CompanyName = c.GetProperty("CompanyName").GetString(),
                    Email = c.GetProperty("Email").GetString(),
                    PhoneNumber = c.GetProperty("PhoneNumber").GetString(),
                    Address = c.GetProperty("Address").GetString(),
                    City = c.GetProperty("City").GetString(),
                    Country = c.GetProperty("Country").GetString(),
                    BankName = c.GetProperty("BankName").GetString(),
                    IBAN = c.GetProperty("IBAN").GetString(),
                    BIC = c.GetProperty("BIC").GetString(),
                    VATNumber = c.GetProperty("VATNumber").GetString(),
                    UIC = c.GetProperty("UIC").GetString(),
                }
            );
    }

    // Everything InventoryService would snapshot from its seed data. Cost is the highest purchase
    // price of the batches in stock (Inventory also leaves out expired ones, but that depends on
    // today's date and the order seed doesn't depend on it).
    public static CatalogSnapshot Catalog()
    {
        using var doc = Read("products.json");
        var products = doc.RootElement.EnumerateArray().ToList();
        return OrderBuilder.Catalog(
            products.Select(p =>
            {
                var costs = p.GetProperty("Batches")
                    .EnumerateArray()
                    .Where(b => b.GetProperty("Quantity").GetInt32() > 0)
                    .Select(b => b.GetProperty("PurchasePrice").GetDecimal())
                    .ToList();
                return new ProductSnapshot(
                    p.GetProperty("Id").GetGuid(),
                    p.GetProperty("SKU").GetString(),
                    p.GetProperty("Name").GetString(),
                    p.GetProperty("UnitOfMeasure").GetString(),
                    Decimal(p.GetProperty("ListPrice")),
                    Decimal(p.GetProperty("VatRate")),
                    costs.Count == 0 ? null : costs.Max()
                );
            }),
            products.SelectMany(p =>
                p.GetProperty("Batches")
                    .EnumerateArray()
                    .Select(b => new BatchSnapshot(
                        b.GetProperty("Id").GetGuid(),
                        p.GetProperty("Id").GetGuid(),
                        b.GetProperty("Lot").GetString(),
                        b.GetProperty("ExpirationDate").ValueKind == JsonValueKind.Null
                            ? null
                            : b.GetProperty("ExpirationDate").GetDateTime()
                    ))
            )
        );
    }

    public static List<OrderSeed> Orders() =>
        JsonSerializer.Deserialize<List<OrderSeed>>(File.ReadAllText(OrdersFile))
        ?? throw new InvalidOperationException("The order seed file is empty.");

    private static decimal? Decimal(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();

    private static string PathOf(string file) =>
        Path.Combine(AppContext.BaseDirectory, "SeedData", file);

    private static JsonDocument Read(string file) =>
        JsonDocument.Parse(File.ReadAllText(PathOf(file)));
}

// An order in OrderService's dummy_data.json.
internal sealed record OrderSeed(
    Guid Id,
    DateTime? OrderDate,
    DateTime? ShippedDate,
    string? ShippingAddress,
    string? OrderStatus,
    Guid CustomerId,
    List<OrderItemWriteDto> Items,
    InvoiceSeed? Invoice
);

internal sealed record InvoiceSeed(DateTime? InvoiceDate, string? InvoiceStatus);
