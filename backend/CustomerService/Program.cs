using System;
using System.IO;
using System.Reflection;
using CustomerService.AsyncDataServices;
using CustomerService.DataAccess;
using CustomerService.SyncDataServeces.Grpc;
using Microsoft.AspNetCore.Builder;
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
builder.Services.AddOpenApi();
builder.Services.AddDbContext<CustomerContext>(options =>
{
    options.UseMySQL(
        builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.")
    );
});
builder.Services.AddScoped<ICustomerRepo, CustomerRepo>();
builder.Services.AddGrpc();
builder.Services.AddSingleton<IMessageBusClient, MessageBusClient>();

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
app.MapGrpcService<GrpcCustomerService>();

app.MapGet(
    "/protos/customer.proto",
    async context =>
    {
        await context.Response.WriteAsync(
            await File.ReadAllTextAsync(
                Path.Combine(app.Environment.ContentRootPath, "Protos", "customer.proto")
            )
        );
    }
);

if (!isOpenApiGeneration)
{
    await PrepDB.PrepPopulation(app);
}

await app.RunAsync();
