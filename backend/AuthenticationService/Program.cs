using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Reflection;
using AuthenticationService.Auditing;
using AuthenticationService.DataAccess;
using AuthenticationService.Models;
using AuthenticationService.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Build-time OpenAPI generation (GetDocument.Insider) runs this entry point without the signing key;
// skip side effects (DB migrations/seeding) in that case.
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<AuthContext>(tags: ["ready"]);
builder.Services.AddDbContext<AuthContext>(options =>
{
    options.UseMySQL(
        builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.")
    );
});
builder.Services.AddScoped<IUserRepo, UserRepo>();
builder.Services.AddScoped<IRefreshTokenRepo, RefreshTokenRepo>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<JwtTokenHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// This service validates its own tokens (user management, password change); the gateway
// also enforces roles for everything routed through it.
if (!isOpenApiGeneration)
{
    // Fail at startup, not on the first request, when Jwt:Key is missing or too short.
    var signingKey = new SymmetricSecurityKey(
        JwtTokenHandler.ReadSigningKey(builder.Configuration)
    );
    builder
        .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                ValidateIssuer = false,
                ValidateAudience = false,
                IssuerSigningKey = signingKey,
                NameClaimType = JwtRegisteredClaimNames.Name,
                RoleClaimType = JwtTokenHandler.RoleClaimType,
            };
        });
}
builder.Services.AddAuthorization();

Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();

builder.Host.UseSerilog();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "v1"));
}

app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

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
