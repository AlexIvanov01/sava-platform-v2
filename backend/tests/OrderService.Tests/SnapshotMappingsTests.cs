using System.Globalization;
using CustomerService;
using InventoryService;
using OrderService.Mappings;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class SnapshotMappingsTests
{
    private static void UnderBulgarianCulture(Action action)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("bg-BG");
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ProductSnapshot_ParsesInvariantDecimalsUnderBulgarianCulture()
    {
        var f = TestData.Faker;
        var id = f.Random.Guid();
        var (listPrice, vatRate, cost) = (
            f.Finance.Amount(1, 1000),
            f.Finance.Amount(0, 25),
            f.Finance.Amount(1, 1000)
        );
        var model = new GrpcProductSnapshot
        {
            Id = id.ToString(),
            Sku = f.Random.Replace("???-#####"),
            Name = f.Commerce.ProductName(),
            UnitOfMeasure = "Piece",
            ListPrice = listPrice.ToString(CultureInfo.InvariantCulture),
            VatRate = vatRate.ToString(CultureInfo.InvariantCulture),
            Cost = cost.ToString(CultureInfo.InvariantCulture),
        };

        UnderBulgarianCulture(() =>
        {
            var snapshot = model.ToSnapshot();

            snapshot.Id.ShouldBe(id);
            snapshot.SKU.ShouldBe(model.Sku);
            snapshot.Name.ShouldBe(model.Name);
            snapshot.UnitOfMeasure.ShouldBe(model.UnitOfMeasure);
            snapshot.ListPrice.ShouldBe(listPrice);
            snapshot.VatRate.ShouldBe(vatRate);
            snapshot.Cost.ShouldBe(cost);
        });
    }

    // Protobuf strings can't be null, so InventoryService sends missing values as empty strings.
    [Fact]
    public void ProductSnapshot_EmptyValuesBecomeNull()
    {
        var id = TestData.Faker.Random.Guid();

        var snapshot = new GrpcProductSnapshot { Id = id.ToString() }.ToSnapshot();

        snapshot.ShouldBe(new(id, null, null, null, null, null, null));
    }

    [Fact]
    public void BatchSnapshot_ParsesRoundTripDate()
    {
        var f = TestData.Faker;
        var (id, productId) = (f.Random.Guid(), f.Random.Guid());
        var expiration = TestData.Date(2027, 2035);
        var model = new GrpcBatchSnapshot
        {
            Id = id.ToString(),
            ProductId = productId.ToString(),
            Lot = f.Random.Replace("LOT-#####"),
            ExpirationDate = expiration.ToString("O", CultureInfo.InvariantCulture),
        };

        UnderBulgarianCulture(() =>
        {
            var snapshot = model.ToSnapshot();

            snapshot.ShouldBe(new(id, productId, model.Lot, expiration));
            snapshot.ExpirationDate!.Value.Kind.ShouldBe(DateTimeKind.Unspecified);
        });
    }

    [Fact]
    public void BatchSnapshot_UtcDateKeepsItsKind()
    {
        var expiration = DateTime.SpecifyKind(TestData.Date(2027, 2035), DateTimeKind.Utc);
        var model = new GrpcBatchSnapshot
        {
            Id = TestData.Faker.Random.Guid().ToString(),
            ProductId = TestData.Faker.Random.Guid().ToString(),
            ExpirationDate = expiration.ToString("O", CultureInfo.InvariantCulture),
        };

        var date = model.ToSnapshot().ExpirationDate.ShouldNotBeNull();

        date.ShouldBe(expiration);
        date.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void BatchSnapshot_EmptyValuesBecomeNull()
    {
        var (id, productId) = (TestData.Faker.Random.Guid(), TestData.Faker.Random.Guid());

        var snapshot = new GrpcBatchSnapshot
        {
            Id = id.ToString(),
            ProductId = productId.ToString(),
        }.ToSnapshot();

        snapshot.ShouldBe(new(id, productId, null, null));
    }

    [Fact]
    public void CustomerSnapshot_CopiesEveryField()
    {
        var customer = OrderBuilder.Customer();
        var model = new GrpcCustomerModel
        {
            ExternalId = TestData.Faker.Random.Guid().ToString(),
            Name = customer.Name,
            CompanyName = customer.CompanyName,
            Email = customer.Email,
            PhoneNumber = customer.PhoneNumber,
            Address = customer.Address,
            City = customer.City,
            Country = customer.Country,
            BankName = customer.BankName,
            IBAN = customer.IBAN,
            BIC = customer.BIC,
            VATNumber = customer.VATNumber,
            UIC = customer.UIC,
        };

        model.ToSnapshot().ToReadDto().ShouldBe(customer.ToReadDto());
    }

    [Fact]
    public void CustomerSnapshot_EmptyValuesBecomeNull()
    {
        var model = new GrpcCustomerModel { ExternalId = TestData.Faker.Random.Guid().ToString() };

        model
            .ToSnapshot()
            .ToReadDto()
            .ShouldBe(new(null, null, null, null, null, null, null, null, null, null, null, null));
    }
}
