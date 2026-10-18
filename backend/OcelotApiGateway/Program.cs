using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using OcelotApiGateway.JwtAuthenticationManager;

var builder = WebApplication.CreateBuilder(args);

builder
    .Configuration.SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddOcelot(builder.Configuration);

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

// Before Ocelot, which otherwise handles every request.
app.UseHealthChecks("/healthz");
app.UseHealthChecks("/readyz");

await app.UseOcelot();

app.UseAuthentication();
app.UseAuthorization();

await app.RunAsync();
