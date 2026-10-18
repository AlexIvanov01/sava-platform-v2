using System.ComponentModel.DataAnnotations;
using OrderService.Dtos;

namespace OrderService.Tests;

public class OrderDtoValidationTests
{
    private static List<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, true);
        return results;
    }

    [Fact]
    public void CreateDto_DuplicateItems_ReportsOrderItems()
    {
        var itemId = Guid.NewGuid();
        var dto = OrderBuilder.CreateDto(Guid.NewGuid(), (itemId, 1), (itemId, 2));

        var error = Validate(dto).ShouldHaveSingleItem();

        error.MemberNames.ShouldBe(["OrderItems"]);
        error.ErrorMessage.ShouldNotBeNull().ShouldContain(itemId.ToString());
    }

    [Fact]
    public void UpdateDto_DuplicateItems_ReportsOrderItems()
    {
        var itemId = Guid.NewGuid();
        var dto = OrderBuilder.UpdateDto(null, null, (itemId, 1), (itemId, 1));

        Validate(dto).ShouldHaveSingleItem().MemberNames.ShouldBe(["OrderItems"]);
    }

    [Fact]
    public void Dtos_DistinctOrMissingItems_AreValid()
    {
        Validate(OrderBuilder.CreateDto(Guid.NewGuid(), (Guid.NewGuid(), 1), (Guid.NewGuid(), 1)))
            .ShouldBeEmpty();
        Validate(OrderBuilder.UpdateDto("Confirmed")).ShouldBeEmpty();
    }
}
