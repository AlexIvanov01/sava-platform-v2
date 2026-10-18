using System;
using System.IO;
using System.Reflection;
using InventoryService.AsyncDataServices;
using InventoryService.DataAccess;
using InventoryService.EventProcessing;
using InventoryService.SyncDataServices.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Build-time OpenAPI generation (GetDocument.Insider) runs this entry point and starts the host;
// skip side effects (DB migrations/seeding, background workers) in that case.
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

builder.Services.AddControllers();

if (!isOpenApiGeneration)
{
    builder.Services.AddHostedService<MessageBusSubscriber>();
}

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<ProductContext>(tags: ["ready"]);
builder.Services.AddDbContext<ProductContext>(options =>
{
    options.UseMySQL(
        builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.")
    );
});
builder.Services.AddScoped<IProductRepo, ProductRepo>();
builder.Services.AddSingleton<IMessageBusClient, MessageBusClient>();
builder.Services.AddGrpc();
builder.Services.AddSingleton<IEventProcessor, EventProcessor>();

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
app.MapGrpcService<GrpcInventoryService>();

app.MapGet(
    "/protos/inventory.proto",
    async context =>
    {
        await context.Response.WriteAsync(
            await File.ReadAllTextAsync(
                Path.Combine(app.Environment.ContentRootPath, "Protos", "inventory.proto")
            )
        );
    }
);

if (!isOpenApiGeneration)
{
    await PrepDB.PrepPopulation(app);
}

await app.RunAsync();
