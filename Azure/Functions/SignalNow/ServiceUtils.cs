// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.SignalNow
{
    public class ServiceUtils
    {
        private static readonly char[] PropertySeparator = { ';' };
        private static readonly char[] KeyValueSeparator = { '=' };
        private const string EndpointProperty = "endpoint";
        private const string AccessKeyProperty = "accesskey";

        public string Endpoint { get; }

        public string AccessKey { get; }

        public ServiceUtils(string connectionString)
        {
            (Endpoint, AccessKey) = ParseConnectionString(connectionString);
        }

        public bool ParseAndValidateToken(string tokenString, out string userIdHash, out string userId, string audience = null)
        {
            JwtSecurityTokenHandler JwtTokenHandler = new JwtSecurityTokenHandler();
            userIdHash = string.Empty;
            userId = string.Empty;

            if (!JwtTokenHandler.CanReadToken(tokenString))
            {
                return false;
            }

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AccessKey));

            TokenValidationParameters valParams = new TokenValidationParameters()
            {
                ValidateIssuer = false, 
                ValidateIssuerSigningKey = true, 
                IssuerSigningKey = securityKey, 
                ValidateLifetime = true, 
                ValidateAudience = string.IsNullOrEmpty(audience) ? false : true, 
                ValidAudience = audience
            };

            SecurityToken validToken = null;
            ClaimsPrincipal principal = null;

            try
            {
                principal = JwtTokenHandler.ValidateToken(tokenString, valParams, out validToken);
            }
            catch(Microsoft.IdentityModel.Tokens.SecurityTokenInvalidSignatureException)
            {
                System.Diagnostics.Debug.WriteLine("Token signature verification failed.");
            }
            
            if(principal != null)
            {
                userIdHash = principal.Claims.Where(claim => claim.Type == ClaimTypes.NameIdentifier).FirstOrDefault().Value;
                userId = principal.Claims.Where(claim => claim.Type == ClaimTypes.GivenName).FirstOrDefault().Value;
            }

            return !string.IsNullOrEmpty(userIdHash);
        }

        public string GenerateAccessToken(string audience, string userId, TimeSpan? lifetime = null)
        {
            IEnumerable<Claim> claims = null;
            if (userId != null)
            {
                claims = new[]
                {
                    new Claim(ClaimTypes.GivenName, userId), 
                    new Claim(ClaimTypes.NameIdentifier, SignalRGroupUtils.GetNameHash(userId, AccessKey))
                };
            }

            return GenerateAccessTokenInternal(audience, claims, lifetime ?? TimeSpan.FromHours(1));
        }

        public string GenerateAccessTokenInternal(string audience, IEnumerable<Claim> claims, TimeSpan lifetime)
        {
            var expire = DateTime.UtcNow.Add(lifetime);

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AccessKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            JwtSecurityTokenHandler JwtTokenHandler = new JwtSecurityTokenHandler();
            var token = JwtTokenHandler.CreateJwtSecurityToken(
                issuer: null,
                audience: audience,
                subject: claims == null ? null : new ClaimsIdentity(claims),
                expires: expire,
                signingCredentials: credentials);
            return JwtTokenHandler.WriteToken(token);
        }

        internal static (string, string) ParseConnectionString(string connectionString)
        {
            var properties = connectionString.Split(PropertySeparator, StringSplitOptions.RemoveEmptyEntries);
            if (properties.Length > 1)
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in properties)
                {
                    var kvp = property.Split(KeyValueSeparator, 2);
                    if (kvp.Length != 2) continue;

                    var key = kvp[0].Trim();
                    if (dict.ContainsKey(key))
                    {
                        throw new ArgumentException($"Duplicate properties found in connection string: {key}.");
                    }

                    dict.Add(key, kvp[1].Trim());
                }

                if (dict.ContainsKey(EndpointProperty) && dict.ContainsKey(AccessKeyProperty))
                {
                    return (dict[EndpointProperty].TrimEnd('/'), dict[AccessKeyProperty]);
                }
            }

            throw new ArgumentException($"Connection string missing required properties {EndpointProperty} and {AccessKeyProperty}.");
        }
    }
}
