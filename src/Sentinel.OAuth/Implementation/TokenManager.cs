﻿namespace Sentinel.OAuth.Implementation
{
    using Common.Logging;
    using Sentinel.OAuth.Core.Constants.Identity;
    using Sentinel.OAuth.Core.Interfaces.Factories;
    using Sentinel.OAuth.Core.Interfaces.Identity;
    using Sentinel.OAuth.Core.Interfaces.Managers;
    using Sentinel.OAuth.Core.Interfaces.Providers;
    using Sentinel.OAuth.Core.Interfaces.Repositories;
    using Sentinel.OAuth.Core.Managers;
    using Sentinel.OAuth.Models.Identity;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>A universal token manager. Takes care of processing the tokens without caring where and how they are stored.</summary>
    public class TokenManager : BaseTokenManager
    {
        /// <summary>The logger.</summary>
        private readonly ILog logger;

        /// <summary>Manager for user.</summary>
        private readonly IUserManager userManager;

        /// <summary>Initializes a new instance of the TokenManager class.</summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when one or more required arguments are null.
        /// </exception>
        /// <param name="logger">The logger.</param>
        /// <param name="userManager">Manager for users.</param>
        /// <param name="principalProvider">The principal provider.</param>
        /// <param name="cryptoProvider">The crypto provider.</param>
        /// <param name="tokenFactory">The token factory.</param>
        /// <param name="tokenRepository">The token repository.</param>
        public TokenManager(ILog logger, IUserManager userManager, IPrincipalProvider principalProvider, ICryptoProvider cryptoProvider, ITokenFactory tokenFactory, ITokenRepository tokenRepository)
            : base(principalProvider, cryptoProvider, tokenFactory, tokenRepository)
        {
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }

            this.logger = logger;
            this.userManager = userManager;
        }

        /// <summary>Authenticates the authorization code.</summary>
        /// <param name="redirectUri">The redirect URI.</param>
        /// <param name="authorizationCode">The authorization code.</param>
        /// <returns>The client principal.</returns>
        public override async Task<ISentinelPrincipal> AuthenticateAuthorizationCodeAsync(string redirectUri, string authorizationCode)
        {
            this.logger.DebugFormat("Authenticating authorization code '{0}' for redirect uri '{1}'", authorizationCode, redirectUri);

            var authorizationCodes = await this.TokenRepository.GetAuthorizationCodes(redirectUri, DateTime.UtcNow);

            this.logger.DebugFormat("Got {0} authorization codes expiring after {1}", authorizationCodes.Count(), DateTime.UtcNow.ToString("s"));

            var entity = authorizationCodes.FirstOrDefault(x => this.CryptoProvider.ValidateHash(authorizationCode, x.Code));

            if (entity != null)
            {
                this.logger.DebugFormat("Authorization code is valid");

                // Delete used authorization code
                await this.TokenRepository.DeleteAuthorizationCode(entity);

                var storedPrincipal = this.PrincipalProvider.Decrypt(entity.Ticket, authorizationCode);

                // Set correct authentication method and return new principal
                return this.PrincipalProvider.Create(AuthenticationType.OAuth, storedPrincipal.Identity.Claims.ToArray());
            }

            this.logger.Warn("Authorization code is not valid");

            return this.PrincipalProvider.Anonymous;
        }

        /// <summary>Authenticates the access token.</summary>
        /// <param name="accessToken">The access token.</param>
        /// <returns>The user principal.</returns>
        public override async Task<ISentinelPrincipal> AuthenticateAccessTokenAsync(string accessToken)
        {
            this.logger.DebugFormat("Authenticating access token");

            var accessTokens = await this.TokenRepository.GetAccessTokens(DateTime.UtcNow);

            this.logger.DebugFormat("Got {0} access tokens expiring after {1}", accessTokens.Count(), DateTime.UtcNow.ToString("s"));

            var entity = accessTokens.FirstOrDefault(x => this.CryptoProvider.ValidateHash(accessToken, x.Token));

            if (entity != null)
            {
                this.logger.DebugFormat("Access token is valid. It belongs to the user '{0}', client '{1}' and redirect uri '{2}'", entity.Subject, entity.ClientId, entity.RedirectUri);

                var storedPrincipal = this.PrincipalProvider.Decrypt(entity.Ticket, accessToken);

                return this.PrincipalProvider.Create(AuthenticationType.OAuth, storedPrincipal.Identity.Claims.ToArray());
            }

            this.logger.WarnFormat("Access token '{0}' is not valid", accessToken);

            return this.PrincipalProvider.Anonymous;
        }

        /// <summary>Authenticates the refresh token.</summary>
        /// <param name="clientId">The client id.</param>
        /// <param name="refreshToken">The refresh token.</param>
        /// <param name="redirectUri">The redirect URI.</param>
        /// <returns>The user principal.</returns>
        public override async Task<ISentinelPrincipal> AuthenticateRefreshTokenAsync(string clientId, string refreshToken, string redirectUri)
        {
            this.logger.DebugFormat("Authenticating refresh token for client '{0}' and redirect uri '{1}'", clientId, redirectUri);

            var refreshTokens = await this.TokenRepository.GetRefreshTokens(clientId, redirectUri, DateTime.UtcNow);

            this.logger.DebugFormat("Got {0} refresh tokens expiring after {1}", refreshTokens.Count(), DateTime.UtcNow.ToString("s"));

            var entity = refreshTokens.FirstOrDefault(x => this.CryptoProvider.ValidateHash(refreshToken, x.Token));

            if (entity != null)
            {
                this.logger.DebugFormat("Refresh token is valid. It belongs to the user '{0}', client '{1}' and redirect uri '{2}'", entity.Subject, entity.ClientId, entity.RedirectUri);

                // Delete refresh token to prevent it being used again
                await this.TokenRepository.DeleteRefreshToken(entity);

                // Re-authenticate user to get new claims
                var user = await this.userManager.AuthenticateUserAsync(entity.Subject);

                // Make sure the user has the correct client claim
                user.Identity.RemoveClaim(x => x.Type == ClaimType.Client);
                user.Identity.AddClaim(ClaimType.Client, entity.ClientId);

                return this.PrincipalProvider.Create(AuthenticationType.OAuth, user.Identity.Claims.ToArray());
            }

            this.logger.WarnFormat("Refresh token '{0}' is not valid", refreshToken);

            return this.PrincipalProvider.Anonymous;
        }

        /// <summary>Generates an authorization code for the specified client.</summary>
        /// <exception cref="ArgumentException">
        /// Thrown when one or more arguments have unsupported or illegal values.
        /// </exception>
        /// <param name="userPrincipal">The user principal.</param>
        /// <param name="expire">The expire time.</param>
        /// <param name="redirectUri">The redirect URI.</param>
        /// <param name="scope">The scope.</param>
        /// <returns>An authorization code.</returns>
        public override async Task<string> CreateAuthorizationCodeAsync(ISentinelPrincipal userPrincipal, TimeSpan expire, string redirectUri, IEnumerable<string> scope)
        {
            if (!userPrincipal.Identity.IsAuthenticated)
            {
                this.logger.ErrorFormat("The specified user is not authenticated");

                return string.Empty;
            }

            var client = userPrincipal.Identity.Claims.FirstOrDefault(x => x.Type == ClaimType.Client);

            if (client == null || string.IsNullOrEmpty(client.Value))
            {
                throw new ArgumentException("The specified principal does not have a valid client identifier", nameof(userPrincipal));
            }

            // Delete all expired authorization codes
            await this.TokenRepository.DeleteAuthorizationCodes(DateTime.UtcNow);

            // Remove unnecessary claims from principal
            userPrincipal.Identity.RemoveClaim(x => x.Type == ClaimType.AccessToken || x.Type == ClaimType.RefreshToken);

            // Add scope claims
            if (scope != null)
            {
                userPrincipal.Identity.AddClaim(scope.Select(x => new SentinelClaim(ClaimType.Scope, x)).ToArray());
            }

            // Create and store authorization code for future use
            this.logger.DebugFormat("Creating authorization code for client '{0}', redirect uri '{1}' and user '{2}'", client, redirectUri, userPrincipal.Identity.Name);

            string code;
            var hashedCode = this.CryptoProvider.CreateHash(out code, 256);

            var authorizationCode = this.TokenFactory.CreateAuthorizationCode(
                client.Value,
                redirectUri,
                userPrincipal.Identity.Name,
                scope,
                hashedCode,
                this.PrincipalProvider.Encrypt(userPrincipal, code),
                DateTime.UtcNow.Add(expire));

            // Add authorization code to database
            var result = await this.TokenRepository.InsertAuthorizationCode(authorizationCode);

            if (result != null)
            {
                this.logger.DebugFormat("Successfully created and stored authorization code");

                return code;
            }

            this.logger.ErrorFormat("Unable to create and/or store authorization code");

            return string.Empty;
        }

        /// <summary>Creates an access token.</summary>
        /// <param name="userPrincipal">The user principal.</param>
        /// <param name="expire">The expire time.</param>
        /// <param name="clientId">The client identifier.</param>
        /// <param name="redirectUri">The redirect URI.</param>
        /// <param name="scope">The scope.</param>
        /// <returns>An access token.</returns>
        public override async Task<string> CreateAccessTokenAsync(ISentinelPrincipal userPrincipal, TimeSpan expire, string clientId, string redirectUri, IEnumerable<string> scope)
        {
            if (!userPrincipal.Identity.IsAuthenticated)
            {
                this.logger.ErrorFormat("The specified principal is not authenticated");

                return string.Empty;
            }

            // Delete all expired access tokens
            await this.TokenRepository.DeleteAccessTokens(DateTime.UtcNow);

            // Remove unnecessary claims from principal
            userPrincipal.Identity.RemoveClaim(x => x.Type == ClaimType.AccessToken || x.Type == ClaimType.RefreshToken);

            // Add scope claims
            if (scope != null)
            {
                userPrincipal.Identity.AddClaim(scope.Select(x => new SentinelClaim(ClaimType.Scope, x)).ToArray());
            }

            this.logger.DebugFormat("Creating access token for client '{0}', redirect uri '{1}' and user '{2}'", clientId, redirectUri, userPrincipal.Identity.Name);

            // Create new access token
            string token;
            var hashedToken = this.CryptoProvider.CreateHash(out token, 2048);

            var accessToken = this.TokenFactory.CreateAccessToken(
                clientId,
                redirectUri,
                userPrincipal.Identity.Name,
                scope,
                hashedToken,
                this.PrincipalProvider.Encrypt(userPrincipal, token),
                DateTime.UtcNow.Add(expire));

            // Add access token to database
            var result = await this.TokenRepository.InsertAccessToken(accessToken);

            if (result != null)
            {
                this.logger.DebugFormat("Successfully created and stored access token");

                return token;
            }

            this.logger.ErrorFormat("Unable to create and/or store access token");

            return string.Empty;
        }

        /// <summary>Creates a refresh token.</summary>
        /// <param name="userPrincipal">The principal.</param>
        /// <param name="expire">The expire time.</param>
        /// <param name="clientId">The client identifier.</param>
        /// <param name="redirectUri">The redirect URI.</param>
        /// <param name="scope">The scope.</param>
        /// <returns>A refresh token.</returns>
        public override async Task<string> CreateRefreshTokenAsync(ISentinelPrincipal userPrincipal, TimeSpan expire, string clientId, string redirectUri, IEnumerable<string> scope)
        {
            if (!userPrincipal.Identity.IsAuthenticated)
            {
                this.logger.ErrorFormat("The specified principal is not authenticated");

                return string.Empty;
            }

            // Delete all expired refresh tokens
            await this.TokenRepository.DeleteRefreshTokens(DateTime.UtcNow);

            // Remove unnecessary claims from principal
            userPrincipal.Identity.RemoveClaim(x => x.Type == ClaimType.AccessToken || x.Type == ClaimType.RefreshToken);

            // Add scope claims
            if (scope != null)
            {
                userPrincipal.Identity.AddClaim(scope.Select(x => new SentinelClaim(ClaimType.Scope, x)).ToArray());
            }

            this.logger.DebugFormat("Creating refresh token for client '{0}', redirect uri '{1}' and user '{2}'", clientId, redirectUri, userPrincipal.Identity.Name);

            // Create new refresh token
            string token;
            var hashedToken = this.CryptoProvider.CreateHash(out token, 2048);

            var refreshToken = this.TokenFactory.CreateRefreshToken(clientId, redirectUri, userPrincipal.Identity.Name, scope, hashedToken, DateTime.UtcNow.Add(expire));

            // Add refresh token to database
            var result = await this.TokenRepository.InsertRefreshToken(refreshToken);

            if (result != null)
            {
                this.logger.DebugFormat("Successfully created and stored refresh token");

                return token;
            }

            this.logger.ErrorFormat("Unable to create and/or store refresh token");

            return string.Empty;
        }
    }
}