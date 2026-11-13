using System;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderService.AsyncDataServices;
using OrderService.Auditing;
using OrderService.DataAccess;
using OrderService.Messaging;
using OrderService.SyncDataServices.Grpc;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Build-time OpenAPI generation (GetDocument.Insider) runs this entry point and starts the host;
// skip side effects (DB migrations/seeding, background workers) in that case.
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

builder.Services.AddControllers();

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<OrderContext>(tags: ["ready"]);
builder.Services.AddDbContext<OrderContext>(options =>
{
    options.UseMySQL(
        builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.")
    );
});
builder.Services.AddScoped<IOrderRepo, OrderRepo>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<IInvoiceRepo, InvoiceRepo>();
builder.Services.AddScoped<ICompanySettingsRepo, CompanySettingsRepo>();
builder.Services.AddSingleton<IInventoryDataClient, InventoryDataClient>();
builder.Services.AddSingleton<ICustomerDataClient, CustomerDataClient>();
builder.Services.AddSingleton<IMessageBusClient, MessageBusClient>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<MessagingOptions>(
    builder.Configuration.GetSection(MessagingOptions.Section)
);
builder.Services.AddSingleton<OutboxSignal>();
if (!isOpenApiGeneration)
{
    builder.Services.AddHostedService<OutboxPublisher>();
    builder.Services.AddHostedService<OutboxCleanup>();
}

Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();

builder.Host.UseSerilog();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "v1"));
}

app.UseSerilogRequestLogging();

app.MapControllers();

// Liveness runs no checks; readiness also checks the database connection.
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks(
    "/readyz",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }
);

if (!isOpenApiGeneration)
{
    await PrepDB.PrepPopulation(app);
}

await app.RunAsync();
