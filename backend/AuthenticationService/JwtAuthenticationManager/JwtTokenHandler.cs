using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using JwtAuthenticationManager.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace JwtAuthenticationManager
{
    public class JwtTokenHandler
    {
        public const string SigningKeySetting = "Jwt:Key";
        public const int MinimumSigningKeyLength = 32;
        public const int JWT_TOKEN_VALIDITY_MINS = 60;

        private readonly byte[] _signingKey;
        private readonly List<UserAccount> _userAccounts;

        public JwtTokenHandler(IConfiguration configuration)
        {
            _signingKey = ReadSigningKey(configuration);

            // Demo accounts until a real user store exists. Sonar S2068 is acknowledged here.
#pragma warning disable S2068
            _userAccounts =
            [
                new UserAccount
                {
                    UserName = "admin",
                    Password = "admin123",
                    Role = "Administrator",
                },
                new UserAccount
                {
                    UserName = "user",
                    Password = "user123",
                    Role = "User",
                },
            ];
#pragma warning restore S2068
        }

        // HS256 needs a key of at least 256 bits; the API gateway validates tokens with the same key.
        public static byte[] ReadSigningKey(IConfiguration configuration)
        {
            var key = configuration[SigningKeySetting];
            if (string.IsNullOrWhiteSpace(key) || key.Length < MinimumSigningKeyLength)
                throw new InvalidOperationException(
                    $"'{SigningKeySetting}' must be configured with at least {MinimumSigningKeyLength} characters."
                );
            return Encoding.UTF8.GetBytes(key);
        }

        public AuthenticationResponse? GenerateJwtToken(AuthenticationRequest authenticationRequest)
        {
            if (
                string.IsNullOrWhiteSpace(authenticationRequest.UserName)
                || string.IsNullOrWhiteSpace(authenticationRequest.Password)
            )
                return null;

            /* Validation */

            var userAccount = _userAccounts.Find(x =>
                x.UserName == authenticationRequest.UserName
                && x.Password == authenticationRequest.Password
            );
            if (userAccount == null)
                return null;

            var tokenExpiryTimeStamp = DateTime.Now.AddMinutes(JWT_TOKEN_VALIDITY_MINS);
            var claimsIdentity = new ClaimsIdentity(
                new List<Claim>
                {
                    new Claim(JwtRegisteredClaimNames.Name, authenticationRequest.UserName),
                    new Claim("Role", userAccount.Role),
                }
            );

            var signingCredentials = new SigningCredentials(
                new SymmetricSecurityKey(_signingKey),
                SecurityAlgorithms.HmacSha256Signature
            );

            var securityTokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = claimsIdentity,
                Expires = tokenExpiryTimeStamp,
                SigningCredentials = signingCredentials,
            };

            var jwtSecurityTokenHandler = new JwtSecurityTokenHandler();
            var securityToken = jwtSecurityTokenHandler.CreateToken(securityTokenDescriptor);
            var token = jwtSecurityTokenHandler.WriteToken(securityToken);

            return new AuthenticationResponse
            {
                UserName = authenticationRequest.UserName,
                ExpiresIn = (int)tokenExpiryTimeStamp.Subtract(DateTime.Now).TotalSeconds,
                JwtToken = token,
            };
        }
    }
}
