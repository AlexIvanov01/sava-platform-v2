using System;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderService.AsyncDataServices;
using OrderService.DataAccess;
using OrderService.EventProcessing;
using OrderService.SyncDataServices.Grpc;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Build-time OpenAPI generation (GetDocument.Insider) runs this entry point and starts the host;
// skip side effects (DB migrations/seeding, background workers) in that case.
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

builder.Services.AddControllers();

builder.Services.AddOpenApi();
builder.Services.AddDbContext<OrderContext>(options =>
{
    options.UseMySQL(builder.Configuration.GetConnectionString("Default"));
});
builder.Services.AddScoped<IOrderRepo, OrderRepo>();
builder.Services.AddScoped<IInvoiceRepo, InvoiceRepo>();
builder.Services.AddScoped<ICustomerRepo, CustomerRepo>();
builder.Services.AddScoped<IItemRepo, ItemRepo>();
builder.Services.AddScoped<IInventoryDataClient, InventoryDataClient>();
builder.Services.AddScoped<ICustomerDataClient, CustomerDataClient>();
builder.Services.AddSingleton<IMessageBusClient, MessageBusClient>();
builder.Services.AddSingleton<IEventProcessor, EventProcessor>();

if (!isOpenApiGeneration)
{
    builder.Services.AddHostedService<MessageBusSubscriber>();
    builder.Services.AddHostedService<GrpcSyncService>();
}

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration).CreateLogger();

builder.Host.UseSerilog();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "v1"));
}

app.UseSerilogRequestLogging();

app.MapControllers();

if (!isOpenApiGeneration)
{
    await PrepDB.PrepPopulation(app);
}

await app.RunAsync();
