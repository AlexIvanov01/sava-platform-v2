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

        var dto = product.ToReadDto(DateOnly.FromDateTime(DateTime.UtcNow));

        dto.Id.ShouldBe(product.Id);
        dto.Name.ShouldBe("Widget");
        dto.Brand.ShouldBe(product.Brand);
        dto.Barcode.ShouldBe(product.Barcode);
        dto.ReorderLevel.ShouldBe(product.ReorderLevel!.Value);
        dto.Weight.ShouldBe(product.Weight!.Value);
        var batchDto = dto.Batches.ShouldHaveSingleItem();
        batchDto.Id.ShouldBe(batch.Id);
        batchDto.Lot.ShouldBe(batch.Lot);
        batchDto.Quantity.ShouldBe(batch.Quantity!.Value);
        batchDto.PurchasePrice.ShouldBe(batch.PurchasePrice!.Value);
        batchDto.ExpirationDate.ShouldBe(batch.ExpirationDate!.Value);
    }

    [Fact]
    public void ToReadDto_NullNumbers_DefaultToZero()
    {
        var dto = new Product { Id = Guid.NewGuid(), Batches = [new ProductBatch()] }.ToReadDto(
            DateOnly.FromDateTime(DateTime.UtcNow)
        );

        dto.ReorderLevel.ShouldBe(0);
        dto.Weight.ShouldBe(0f);
        dto.Batches[0].Quantity.ShouldBe(0);
        dto.Batches[0].PurchasePrice.ShouldBe(0m);
        dto.Batches[0].ExpirationDate.ShouldBe(default);
    }

    [Fact]
    public void ToEntity_FromProductCreateDto_CopiesFields()
    {
        var dto = InventoryBuilder.ProductCreateDto();

        var product = dto.ToEntity();

        product.Id.ShouldBe(Guid.Empty);
        product.Name.ShouldBe(dto.Name);
        product.Supplier.ShouldBe(dto.Supplier);
        product.Status.ShouldBe(dto.Status);
        product.Weight.ShouldBe(dto.Weight);
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
        var createDto = InventoryBuilder.BatchCreateDto("LOT-9", 3);
        var created = createDto.ToEntity();
        var id = Guid.NewGuid();
        var updated = InventoryBuilder.BatchUpdateDto(quantity: 7).ToEntity(id);

        created.Lot.ShouldBe("LOT-9");
        created.Quantity.ShouldBe(3);
        created.PurchasePrice.ShouldBe(createDto.PurchasePrice);
        updated.Id.ShouldBe(id);
        updated.Quantity.ShouldBe(7);
        updated.Lot.ShouldBeNull();
    }
}
