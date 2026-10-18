using System.Globalization;
using InventoryService.Mappings;
using InventoryService.Models;

namespace InventoryService.Tests;

public class ProductMappingsTests
{
    [Fact]
    public void ToReadDto_MapsProductWithBatches()
    {
        var batch = InventoryBuilder.Batch();
        var product = InventoryBuilder.Product("Widget", batch);

        var dto = product.ToReadDto();

        dto.Id.ShouldBe(product.Id);
        dto.Name.ShouldBe("Widget");
        dto.Brand.ShouldBe(product.Brand);
        dto.Barcode.ShouldBe(product.Barcode);
        dto.ReorderLevel.ShouldBe(5);
        dto.Weight.ShouldBe(1.5f);
        var batchDto = dto.Batches.ShouldHaveSingleItem();
        batchDto.Id.ShouldBe(batch.Id);
        batchDto.Lot.ShouldBe(batch.Lot);
        batchDto.Quantity.ShouldBe(10);
        batchDto.PurchasePrice.ShouldBe(5.50m);
        batchDto.SellPrice.ShouldBe(12.99m);
        batchDto.ExpirationDate.ShouldBe(batch.ExpirationDate!.Value);
    }

    [Fact]
    public void ToReadDto_NullNumbers_DefaultToZero()
    {
        var dto = new Product { Id = Guid.NewGuid(), Batches = [new ProductBatch()] }.ToReadDto();

        dto.ReorderLevel.ShouldBe(0);
        dto.Weight.ShouldBe(0f);
        dto.Batches[0].Quantity.ShouldBe(0);
        dto.Batches[0].SellPrice.ShouldBe(0m);
        dto.Batches[0].ExpirationDate.ShouldBe(default);
    }

    [Fact]
    public void ToEntity_FromProductCreateDto_CopiesFields()
    {
        var product = InventoryBuilder.ProductCreateDto().ToEntity();

        product.Id.ShouldBe(Guid.Empty);
        product.Name.ShouldBe("Widget");
        product.Supplier.ShouldBe("Acme Ltd");
        product.Status.ShouldBe("Active");
        product.Weight.ShouldBe(1.5f);
        product.Batches.ShouldBeEmpty();
    }

    [Fact]
    public void ToEntity_FromProductUpdateDto_UsesRouteIdAndKeepsNulls()
    {
        var id = Guid.NewGuid();

        var product = InventoryBuilder.ProductUpdateDto(name: "Renamed").ToEntity(id);

        product.Id.ShouldBe(id);
        product.Name.ShouldBe("Renamed");
        product.Brand.ShouldBeNull();
        product.ReorderLevel.ShouldBeNull();
    }

    [Fact]
    public void ToEntity_FromBatchDtos_CopiesFields()
    {
        var created = InventoryBuilder.BatchCreateDto("LOT-9", 3).ToEntity();
        var id = Guid.NewGuid();
        var updated = InventoryBuilder.BatchUpdateDto(quantity: 7).ToEntity(id);

        created.Lot.ShouldBe("LOT-9");
        created.Quantity.ShouldBe(3);
        created.SellPrice.ShouldBe(12.99m);
        updated.Id.ShouldBe(id);
        updated.Quantity.ShouldBe(7);
        updated.Lot.ShouldBeNull();
    }

    [Fact]
    public void ToGrpcModel_FormatsWithInvariantCultureRegardlessOfCurrentCulture()
    {
        var product = InventoryBuilder.Product("Widget");
        var batch = InventoryBuilder.Batch();
        batch.ProductId = product.Id;
        batch.Product = product;
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("bg-BG");

            var model = batch.ToGrpcModel();

            model.ExternalBatchId.ShouldBe(batch.Id.ToString());
            model.ExternalProductId.ShouldBe(product.Id.ToString());
            model.Name.ShouldBe("Widget");
            model.Lot.ShouldBe("LOT-1");
            model.Price.ShouldBe("12.99");
            model.ExpirationDate.ShouldBe("01/15/2030 00:00:00");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ToGrpcModel_NullValues_BecomeEmptyStrings()
    {
        var batch = new ProductBatch { Id = Guid.NewGuid(), Product = new Product() };

        var model = batch.ToGrpcModel();

        model.ExternalProductId.ShouldBeEmpty();
        model.Name.ShouldBeEmpty();
        model.Price.ShouldBeEmpty();
        model.ExpirationDate.ShouldBeEmpty();
        model.Lot.ShouldBeEmpty();
    }
}
