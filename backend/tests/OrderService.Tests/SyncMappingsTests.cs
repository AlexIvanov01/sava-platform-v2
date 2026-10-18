using System.Globalization;
using CustomerService;
using InventoryService;
using OrderService.Dtos;
using OrderService.Mappings;

namespace OrderService.Tests;

public class SyncMappingsTests
{
    [Fact]
    public void ToEntity_FromItemPublishedDto_CopiesFields()
    {
        var dto = new ItemPublishedDto
        {
            ExternalBatchId = Guid.NewGuid(),
            ExternalProductId = Guid.NewGuid(),
            Name = "Widget",
            Price = 1.5m,
            Lot = "L1",
            ExpirationDate = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
        };

        var item = dto.ToEntity();

        item.ExternalBatchId.ShouldBe(dto.ExternalBatchId.Value);
        item.ExternalProductId.ShouldBe(dto.ExternalProductId);
        item.Name.ShouldBe("Widget");
        item.Price.ShouldBe(1.5m);
        item.Lot.ShouldBe("L1");
        item.ExpirationDate.ShouldBe(dto.ExpirationDate);
    }

    [Fact]
    public void ToEntity_FromGrpcInventoryModel_ParsesInvariantCultureUnderBulgarianCulture()
    {
        var batchId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var model = new GrpcInventoryModel
        {
            ExternalBatchId = batchId.ToString(),
            ExternalProductId = productId.ToString(),
            Name = "Widget",
            Price = "12.99",
            Lot = "L1",
            ExpirationDate = "01/15/2030 00:00:00",
        };
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("bg-BG");

            var item = model.ToEntity();

            item.ExternalBatchId.ShouldBe(batchId);
            item.ExternalProductId.ShouldBe(productId);
            item.Price.ShouldBe(12.99m);
            item.ExpirationDate.ShouldBe(
                new DateTime(2030, 1, 15, 0, 0, 0, DateTimeKind.Unspecified)
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // Inventory sends empty strings for missing values (protobuf strings cannot be null).
    [Fact]
    public void ToEntity_FromGrpcInventoryModel_EmptyOptionalValuesBecomeNull()
    {
        var model = new GrpcInventoryModel
        {
            ExternalBatchId = Guid.NewGuid().ToString(),
            ExternalProductId = "",
            Name = "",
            Price = "",
            Lot = "",
            ExpirationDate = "",
        };

        var item = model.ToEntity();

        item.ExternalProductId.ShouldBeNull();
        item.Price.ShouldBeNull();
        item.ExpirationDate.ShouldBeNull();
    }

    [Fact]
    public void ToEntity_FromCustomerDtos_CopiesFields()
    {
        var id = Guid.NewGuid();
        var published = new CustomerPublishedDto(
            id,
            "N",
            "C",
            "E",
            "P",
            "A",
            "City",
            "BG",
            "B",
            "IBAN",
            "BIC",
            "VAT",
            "UIC"
        );
        var grpc = new GrpcCustomerModel
        {
            ExternalId = id.ToString(),
            Name = "N",
            CompanyName = "C",
            Email = "E",
            PhoneNumber = "P",
            Address = "A",
            City = "City",
            Country = "BG",
            BankName = "B",
            IBAN = "IBAN",
            BIC = "BIC",
            VATNumber = "VAT",
            UIC = "UIC",
        };

        var fromEvent = published.ToEntity();
        var fromGrpc = grpc.ToEntity();

        foreach (var customer in new[] { fromEvent, fromGrpc })
        {
            customer.ExternalId.ShouldBe(id);
            customer
                .ToReadDto()
                .ShouldBe(
                    new CustomerReadDto(
                        id,
                        "N",
                        "C",
                        "E",
                        "P",
                        "A",
                        "City",
                        "BG",
                        "B",
                        "IBAN",
                        "BIC",
                        "VAT",
                        "UIC"
                    )
                );
        }
    }
}
