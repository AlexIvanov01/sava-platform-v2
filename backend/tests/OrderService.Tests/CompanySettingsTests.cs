using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bogus.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute.ExceptionExtensions;
using OrderService.Controllers;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Mappings;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

internal static class CompanySettingsBuilder
{
    public static CompanySettingsUpdateDto Dto()
    {
        var f = TestData.Faker;
        return new(
            f.Company.CompanyName().ClampLength(max: 200),
            f.Random.ReplaceNumbers("#########"),
            f.Random.ReplaceNumbers("BG#########"),
            f.Address.StreetAddress().ClampLength(max: 500),
            f.Address.City().ClampLength(max: 100),
            f.Address.Country().ClampLength(max: 100),
            f.Name.FullName().ClampLength(max: 100),
            f.Company.CompanyName().ClampLength(max: 100),
            f.Finance.Iban().ClampLength(max: 35),
            f.Finance.Bic().ClampLength(max: 15),
            f.Internet.Email().ClampLength(max: 100),
            f.Random.ReplaceNumbers("+359 ### ### ###"),
            f.Random.Int(0, 365)
        );
    }

    // Saved settings, as CompanySettingsRepo returns them.
    public static CompanySettings Entity()
    {
        var settings = Dto().ToEntity();
        settings.Id = TestData.Faker.Random.Guid();
        return settings;
    }

    // What an invoice created with these settings has as its supplier.
    public static SupplierReadDto ToSupplierDto(this CompanySettings settings) =>
        new(
            settings.Name,
            settings.UIC,
            settings.VATNumber,
            settings.Address,
            settings.City,
            settings.Country,
            settings.Manager,
            settings.BankName,
            settings.IBAN,
            settings.BIC,
            settings.Email,
            settings.PhoneNumber
        );

    public static SupplierReadDto ToSupplierDto(this CompanySettingsReadDto settings) =>
        new(
            settings.Name,
            settings.UIC,
            settings.VATNumber,
            settings.Address,
            settings.City,
            settings.Country,
            settings.Manager,
            settings.BankName,
            settings.IBAN,
            settings.BIC,
            settings.Email,
            settings.PhoneNumber
        );

    public static void ShouldMatch(this CompanySettingsReadDto read, CompanySettingsUpdateDto dto)
    {
        read.Name.ShouldBe(dto.Name);
        read.UIC.ShouldBe(dto.UIC);
        read.VATNumber.ShouldBe(dto.VATNumber);
        read.Address.ShouldBe(dto.Address);
        read.City.ShouldBe(dto.City);
        read.Country.ShouldBe(dto.Country);
        read.Manager.ShouldBe(dto.Manager);
        read.BankName.ShouldBe(dto.BankName);
        read.IBAN.ShouldBe(dto.IBAN);
        read.BIC.ShouldBe(dto.BIC);
        read.Email.ShouldBe(dto.Email);
        read.PhoneNumber.ShouldBe(dto.PhoneNumber);
        read.PaymentTermsDays.ShouldBe(dto.PaymentTermsDays);
    }
}

public class CompanySettingsRepoTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    [Fact]
    public async Task Save_CreatesTheRowThenUpdatesOnlyGivenFields()
    {
        var first = CompanySettingsBuilder.Dto();
        await using (var context = Db.CreateContext())
        {
            (await new CompanySettingsRepo(context).GetAsync()).ShouldBeNull();
            await new CompanySettingsRepo(context).SaveAsync(first.ToEntity());
        }

        var newIban = TestData.Faker.Finance.Iban().ClampLength(max: 35);
        await using (var context = Db.CreateContext())
        {
            await new CompanySettingsRepo(context).SaveAsync(
                new CompanySettings { IBAN = newIban }
            );
        }

        var newTerms = first.PaymentTermsDays!.Value % 365 + 1;
        await using (var context = Db.CreateContext())
        {
            await new CompanySettingsRepo(context).SaveAsync(
                new CompanySettings { PaymentTermsDays = newTerms }
            );
        }

        await using (var context = Db.CreateContext())
        {
            (await context.CompanySettings.CountAsync(Ct)).ShouldBe(1);
            var saved = (await new CompanySettingsRepo(context).GetAsync()).ShouldNotBeNull();
            saved
                .ToReadDto()
                .ShouldMatch(first with { IBAN = newIban, PaymentTermsDays = newTerms });
            saved.CreatedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Seed_AddsTheSampleCompanyOnce()
    {
        await using (var context = Db.CreateContext())
        {
            await PrepDB.SeedCompanySettingsAsync(context, SeedData.CompanyFile);
            await PrepDB.SeedCompanySettingsAsync(context, SeedData.CompanyFile);
        }

        await using var check = Db.CreateContext();
        var expected = JsonSerializer.Deserialize<CompanySettingsUpdateDto>(
            await File.ReadAllTextAsync(SeedData.CompanyFile, Ct)
        )!;
        var saved = await check.CompanySettings.SingleAsync(Ct);
        saved.ToReadDto().ShouldMatch(expected);
        saved.Name.ShouldNotBeNullOrWhiteSpace();
        saved.UIC.ShouldNotBeNullOrWhiteSpace();
        saved.PaymentTermsDays.ShouldNotBeNull();
    }
}

public class CompanySettingsControllerTests
{
    private readonly ICompanySettingsRepo _repo = Substitute.For<ICompanySettingsRepo>();
    private readonly CompanySettingsController _controller;

    public CompanySettingsControllerTests() => _controller = new CompanySettingsController(_repo);

    [Fact]
    public async Task Get_NotSaved_Returns404()
    {
        _repo.GetAsync().Returns((CompanySettings?)null);

        (await _controller.GetSettings()).Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Save_FirstTimeWithoutNameOrUic_Returns400AndSavesNothing()
    {
        _repo.GetAsync().Returns((CompanySettings?)null);

        foreach (var dto in new[] { Dto() with { Name = null }, Dto() with { UIC = " " } })
        {
            var result = (
                await _controller.SaveSettings(dto)
            ).Result.ShouldBeOfType<ObjectResult>();
            result.StatusCode.ShouldBe(400);
        }
        await _repo.DidNotReceiveWithAnyArgs().SaveAsync(default!);
    }

    [Fact]
    public async Task Save_ExistingSettings_AcceptsAPartialUpdate()
    {
        var existing = Dto().ToEntity();
        _repo.GetAsync().Returns(existing);
        _repo.SaveAsync(Arg.Any<CompanySettings>()).Returns(existing);
        var dto = new CompanySettingsUpdateDto(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            TestData.Faker.Random.ReplaceNumbers("+359 ### ###"),
            TestData.Faker.Random.Int(0, 365)
        );

        var result = (await _controller.SaveSettings(dto)).Result.ShouldBeOfType<OkObjectResult>();

        result.Value.ShouldBeOfType<CompanySettingsReadDto>();
        await _repo
            .Received(1)
            .SaveAsync(
                Arg.Is<CompanySettings>(s =>
                    s.PhoneNumber == dto.PhoneNumber
                    && s.PaymentTermsDays == dto.PaymentTermsDays
                    && s.Name == null
                )
            );
    }

    [Fact]
    public async Task Save_RepoThrows_Returns500()
    {
        _repo.GetAsync().Returns(Dto().ToEntity());
        _repo.SaveAsync(Arg.Any<CompanySettings>()).ThrowsAsync(new InvalidOperationException());

        var result = (await _controller.SaveSettings(Dto())).Result.ShouldBeOfType<ObjectResult>();

        result.StatusCode.ShouldBe(500);
    }

    private static CompanySettingsUpdateDto Dto() => CompanySettingsBuilder.Dto();
}

[Trait("Category", "Integration")]
public class CompanySettingsApiTests(OrderApiFactory factory) : IClassFixture<OrderApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Put_SavesAndGetReturnsTheSettings()
    {
        var dto = CompanySettingsBuilder.Dto();

        var put = await _client.PutAsJsonAsync("/api/companysettings", dto, Ct);
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        var fetched = await _client.GetFromJsonAsync<CompanySettingsReadDto>(
            "/api/companysettings",
            Ct
        );
        fetched.ShouldNotBeNull().ShouldMatch(dto);
        fetched.Audit.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Put_InvalidEmail_Returns400()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/companysettings",
            CompanySettingsBuilder.Dto() with
            {
                Email = "not-an-email",
            },
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public async Task Put_PaymentTermsOutOfRange_Returns400AndKeepsTheSettings(int days)
    {
        var before = await _client.GetFromJsonAsync<CompanySettingsReadDto>(
            "/api/companysettings",
            Ct
        );

        var response = await _client.PutAsJsonAsync(
            "/api/companysettings",
            CompanySettingsBuilder.Dto() with
            {
                PaymentTermsDays = days,
            },
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var after = await _client.GetFromJsonAsync<CompanySettingsReadDto>(
            "/api/companysettings",
            Ct
        );
        after.ShouldNotBeNull().ShouldBe(before.ShouldNotBeNull());
    }
}
