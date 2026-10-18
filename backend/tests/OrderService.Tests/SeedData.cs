using System.Text.Json;
using OrderService.Models;

namespace OrderService.Tests;

// Reads the services' development seed files (linked into the output by the .csproj) and maps
// them to what the Order service receives over gRPC.
internal static class SeedData
{
    public static string OrdersFile => PathOf("orders.json");

    public static Customer[] Customers()
    {
        using var doc = Read("customers.json");
        return
        [
            .. doc
                .RootElement.EnumerateArray()
                .Select(c => new Customer
                {
                    ExternalId = c.GetProperty("Id").GetGuid(),
                    Name = c.GetProperty("Name").GetString(),
                    CompanyName = c.GetProperty("CompanyName").GetString(),
                    Address = c.GetProperty("Address").GetString(),
                }),
        ];
    }

    // The Inventory service exposes one item per batch.
    public static Item[] Items()
    {
        using var doc = Read("products.json");
        return
        [
            .. doc
                .RootElement.EnumerateArray()
                .SelectMany(p =>
                    p.GetProperty("Batches")
                        .EnumerateArray()
                        .Select(b => new Item
                        {
                            ExternalBatchId = b.GetProperty("Id").GetGuid(),
                            ExternalProductId = p.GetProperty("Id").GetGuid(),
                            Name = p.GetProperty("Name").GetString(),
                            Price = b.GetProperty("SellPrice").GetDecimal(),
                            Lot = b.GetProperty("Lot").GetString(),
                            ExpirationDate = b.GetProperty("ExpirationDate").GetDateTime(),
                        })
                ),
        ];
    }

    public static (int Orders, int Invoices) OrderCounts()
    {
        using var doc = Read("orders.json");
        var orders = doc.RootElement.EnumerateArray().ToList();
        return (
            orders.Count,
            orders.Count(o => o.GetProperty("Invoice").ValueKind != JsonValueKind.Null)
        );
    }

    private static string PathOf(string file) =>
        Path.Combine(AppContext.BaseDirectory, "SeedData", file);

    private static JsonDocument Read(string file) =>
        JsonDocument.Parse(File.ReadAllText(PathOf(file)));
}
