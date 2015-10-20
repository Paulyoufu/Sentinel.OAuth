﻿namespace Sentinel.OAuth.TokenManagers.RavenDbTokenRepository.Implementation
{
    using Sentinel.OAuth.Core.Interfaces.Factories;
    using Sentinel.OAuth.Core.Interfaces.Models;
    using Sentinel.OAuth.TokenManagers.RavenDbTokenRepository.Models.OAuth;
    using System;
    using System.Collections.Generic;

    /// <summary>A token factory for RavenDB entities.</summary>
    public class RavenTokenFactory : ITokenFactory
    {
        /// <summary>Creates an access token.</summary>
        /// <param name="clientId">The client identifier.</param>
        /// <param name="redirectUri">The redirect URI.</param>
        /// <param name="userId">The user identifier.</param>
        /// <param name="scope">The scope.</param>
        /// <param name="token">The hashed token.</param>
        /// <param name="ticket">The encrypted principal.</param>
        /// <param name="validTo">The point in time where the token expires.</param>
        /// <returns>The new access token.</returns>
        public IAccessToken CreateAccessToken(
            string clientId,
            string redirectUri,
            string userId,
            IEnumerable<string> scope,
            string token,
            string ticket,
            DateTime validTo)
        {
            return new RavenAccessToken()
            {
                ClientId = clientId,
                RedirectUri = redirectUri,
                Subject = userId,
                Token = token,
                Ticket = ticket,
                ValidTo = validTo,
                Created = DateTime.UtcNow,
                Scope = scope
            };
        }

        /// <summary>Creates a refresh token.</summary>
        /// <param name="clientId">The client identifier.</param>
        /// <param name="redirectUri">The redirect URI.</param>
        /// <param name="userId">The user identifier.</param>
        /// <param name="scope">The scope.</param>
        /// <param name="token">The hashed token.</param>
        /// <param name="validTo">The point in time where the token expires.</param>
        /// <returns>The new refresh token.</returns>
        public IRefreshToken CreateRefreshToken(string clientId, string redirectUri, string userId, IEnumerable<string> scope, string token, DateTime validTo)
        {
            return new RavenRefreshToken()
            {
                ClientId = clientId,
                RedirectUri = redirectUri,
                Subject = userId,
                Token = token,
                ValidTo = validTo,
                Created = DateTime.UtcNow,
                Scope = scope
            };
        }

        /// <summary>Creates an authorization code.</summary>
        /// <param name="clientId">The client identifier.</param>
        /// <param name="redirectUri">The redirect URI.</param>
        /// <param name="userId">The user identifier.</param>
        /// <param name="scope">The scope.</param>
        /// <param name="code">The hashed code.</param>
        /// <param name="ticket">The encrypted principal.</param>
        /// <param name="validTo">The point in time where the code expires.</param>
        /// <returns>The new authorization code.</returns>
        public IAuthorizationCode CreateAuthorizationCode(
            string clientId,
            string redirectUri,
            string userId,
            IEnumerable<string> scope,
            string code,
            string ticket,
            DateTime validTo)
        {
            return new RavenAuthorizationCode()
            {
                ClientId = clientId,
                RedirectUri = redirectUri,
                Subject = userId,
                Scope = scope,
                Code = code,
                Ticket = ticket,
                ValidTo = validTo,
                Created = DateTime.UtcNow
            };
        }
    }
}