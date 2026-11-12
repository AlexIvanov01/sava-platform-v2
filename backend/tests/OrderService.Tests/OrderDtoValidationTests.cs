using System.ComponentModel.DataAnnotations;
using OrderService.Dtos;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// The cross-field rules in IValidatableObject. Attributes on the record parameters are checked by
// MVC model binding; OrderApiTests covers those.
public class OrderDtoValidationTests
{
    private static List<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, true);
        return results;
    }

    private static Guid NewId() => TestData.Faker.Random.Guid();

    [Fact]
    public void CreateDto_DuplicateProducts_ReportsItems()
    {
        var productId = NewId();
        var dto = OrderBuilder.CreateDto(
            NewId(),
            OrderBuilder.ItemDto(productId, (NewId(), 1)),
            OrderBuilder.ItemDto(productId, (NewId(), 2))
        );

        var error = Validate(dto).ShouldHaveSingleItem();

        error.MemberNames.ShouldBe(["Items"]);
        error.ErrorMessage.ShouldNotBeNull().ShouldContain("Duplicate product ids");
        error.ErrorMessage.ShouldContain(productId.ToString());
    }

    [Fact]
    public void CreateDto_SameBatchTwice_ReportsItems()
    {
        var batchId = NewId();
        var dto = OrderBuilder.CreateDto(
            NewId(),
            OrderBuilder.ItemDto(NewId(), (batchId, 1)),
            OrderBuilder.ItemDto(NewId(), (NewId(), 1), (batchId, 2))
        );

        var error = Validate(dto).ShouldHaveSingleItem();

        error.MemberNames.ShouldBe(["Items"]);
        error.ErrorMessage.ShouldNotBeNull().ShouldContain("Duplicate batch ids");
        error.ErrorMessage.ShouldContain(batchId.ToString());
    }

    [Fact]
    public void UpdateDto_DuplicateProductsAndBatches_ReportsBoth()
    {
        var productId = NewId();
        var batchId = NewId();
        var dto = OrderBuilder.UpdateDto(
            null,
            OrderBuilder.ItemDto(productId, (batchId, 1), (batchId, 1)),
            OrderBuilder.ItemDto(productId, (NewId(), 1))
        );

        var errors = Validate(dto);

        errors.Count.ShouldBe(2);
        errors.ShouldAllBe(e => e.MemberNames.Single() == "Items");
        errors[0].ErrorMessage.ShouldNotBeNull().ShouldContain(productId.ToString());
        errors[1].ErrorMessage.ShouldNotBeNull().ShouldContain(batchId.ToString());
    }

    [Fact]
    public void Dtos_DistinctOrMissingItems_AreValid()
    {
        Validate(
                OrderBuilder.CreateDto(
                    NewId(),
                    OrderBuilder.ItemDto(NewId(), (NewId(), 1), (NewId(), 2)),
                    OrderBuilder.ItemDto(NewId(), (NewId(), 1))
                )
            )
            .ShouldBeEmpty();
        Validate(OrderBuilder.AddressUpdateDto()).ShouldBeEmpty();
        Validate(new OrderUpdateDto(null, null, null, null)).ShouldBeEmpty();
    }
}
