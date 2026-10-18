using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace OcelotApiGateway.JwtAuthenticationManager
{
    public static class JwtAuthExtension
    {
        public const string SigningKeySetting = "Jwt:Key";
        public const int MinimumSigningKeyLength = 32;

        // Must match the key AuthenticationService signs tokens with.
        public static void AddJwtAuthentication(
            this IServiceCollection services,
            IConfiguration configuration
        )
        {
            var key = configuration[SigningKeySetting];
            if (string.IsNullOrWhiteSpace(key) || key.Length < MinimumSigningKeyLength)
                throw new InvalidOperationException(
                    $"'{SigningKeySetting}' must be configured with at least {MinimumSigningKeyLength} characters."
                );

            services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(o =>
                {
                    o.RequireHttpsMetadata = false;
                    o.SaveToken = true;
                    o.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    };
                });
        }
    }
}
