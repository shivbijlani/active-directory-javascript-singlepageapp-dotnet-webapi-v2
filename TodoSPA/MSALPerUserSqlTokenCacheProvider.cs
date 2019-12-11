﻿/*
 The MIT License (MIT)

Copyright (c) 2015 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using Microsoft.Identity.Client;
using System;
using System.ComponentModel.DataAnnotations;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Security.Claims;
using TodoListService.Utils;
using TodoSPA.DAL;

namespace TodoListService
{
    /// <summary>
    /// This is a MSAL's TokenCache implementation for one user. It uses Sql server as a backend store and uses the Entity Framework to read and write to that database.
    /// </summary>
    /// <seealso cref="https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/token-cache-serialization"/>
    public class MSALPerUserSqlTokenCacheProvider
    {
        /// <summary>
        /// The EF's DBContext object to be used to read and write from the Sql server database.
        /// </summary>
        private TodoListServiceContext TokenCacheDb;

        /// <summary>
        /// This keeps the latest copy of the token in memory to save calls to DB, if possible.
        /// </summary>
        private PerWebUserCache InMemoryCache;

        /// <summary>
        /// Once the user signes in, this will not be null and can be ontained via a call to Thread.CurrentPrincipal
        /// </summary>
        internal ClaimsPrincipal SignedInUser;

        public MSALPerUserSqlTokenCacheProvider(ITokenCache tokenCache, TodoListServiceContext tokenCacheDbContext, ClaimsPrincipal user)
        {
            this.TokenCacheDb = tokenCacheDbContext;
            this.SignedInUser = user;

            this.Initialize(tokenCache, user);
        }

        /// <summary>Initializes this instance of TokenCacheProvider with essentials to initialize themselves.</summary>
        /// <param name="tokenCache">The token cache instance of MSAL application</param>
        /// <param name="httpcontext">The Httpcontext whose Session will be used for caching.This is required by some providers.</param>
        /// <param name="user">The signed-in user for whom the cache needs to be established. Not needed by all providers.</param>
        public void Initialize(ITokenCache tokenCache, ClaimsPrincipal user)
        {
            tokenCache.SetBeforeAccess(this.UserTokenCacheBeforeAccessNotification);
            tokenCache.SetAfterAccess(this.UserTokenCacheAfterAccessNotification);
            tokenCache.SetBeforeWrite(this.UserTokenCacheBeforeWriteNotification);

            if (user == null)
            {
                // No users signed in yet, so we return
                return;
            }

            this.SignedInUser = user;
        }

        /// <summary>
        /// Explores the Claims of a signed-in user (if available) to populate the unique Id of this cache's instance.
        /// </summary>
        /// <returns>The signed in user's object.tenant Id , if available in the HttpContext.User instance</returns>
        private string GetSignedInUsersUniqueId()
        {
            if (this.SignedInUser != null)
            {
                return this.SignedInUser.GetMsalAccountId();
            }
            return null;
        }

        /// <summary>
        /// if you want to ensure that no concurrent write take place, use this notification to place a lock on the entry
        /// </summary>
        /// <param name="args">Contains parameters used by the MSAL call accessing the cache.</param>
        private void UserTokenCacheBeforeWriteNotification(TokenCacheNotificationArgs args)
        {
            // Since we are using a Rowversion for concurrency, we need not to do anything in this handler.
        }

        /// <summary>
        /// Right before it reads the cache, a call is made to BeforeAccess notification. Here, you have the opportunity of retrieving your persisted cache blob
        /// from the Sql database. We pick it from the database, save it in the in-memory copy, and pass it to the base class by calling the Deserialize().
        /// </summary>
        /// <param name="args">Contains parameters used by the MSAL call accessing the cache.</param>
        private void UserTokenCacheBeforeAccessNotification(TokenCacheNotificationArgs args)
        {
            this.ReadCacheForSignedInUser(args);
        }

        /// <summary>
        /// Raised AFTER MSAL added the new token in its in-memory copy of the cache.
        /// This notification is called every time MSAL accessed the cache, not just when a write took place:
        /// If MSAL's current operation resulted in a cache change, the property TokenCacheNotificationArgs.HasStateChanged will be set to true.
        /// If that is the case, we call the TokenCache.Serialize() to get a binary blob representing the latest cache content – and persist it.
        /// </summary>
        /// <param name="args">Contains parameters used by the MSAL call accessing the cache.</param>
        private void UserTokenCacheAfterAccessNotification(TokenCacheNotificationArgs args)
        {
            this.SetSignedInUserFromNotificationArgs(args);

            // if state changed, i.e. new token obtained
            if (args.HasStateChanged && !string.IsNullOrWhiteSpace(this.GetSignedInUsersUniqueId()))
            {
                if (this.InMemoryCache == null)
                {
                    this.InMemoryCache = new PerWebUserCache
                    {
                        WebUserUniqueId = this.GetSignedInUsersUniqueId()
                    };
                }

                this.InMemoryCache.CacheBits = args.TokenCache.SerializeMsalV3();
                this.InMemoryCache.LastWrite = DateTime.Now;

                try
                {
                    // Update the DB and the lastwrite
                    this.TokenCacheDb.Entry(InMemoryCache).State = InMemoryCache.EntryId == 0 ? EntityState.Added : EntityState.Modified;
                    this.TokenCacheDb.SaveChanges();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Record already updated on a different thread, so just read the updated record
                    this.ReadCacheForSignedInUser(args);
                }
            }
        }

        /// <summary>
        /// To keep the cache, ClaimsPrincipal and Sql in sync, we ensure that the user's object Id we obtained by MSAL after
        /// successful sign-in is set as the key for the cache.
        /// </summary>
        /// <param name="args">Contains parameters used by the MSAL call accessing the cache.</param>
        private void SetSignedInUserFromNotificationArgs(TokenCacheNotificationArgs args)
        {
            if (this.SignedInUser == null && args.Account != null)
            {
                this.SignedInUser = args.Account.ToClaimsPrincipal();
            }
        }

        /// <summary>
        /// Reads the cache data from the backend database.
        /// </summary>
        private void ReadCacheForSignedInUser(TokenCacheNotificationArgs args)
        {
            if (this.InMemoryCache == null) // first time access
            {
                this.InMemoryCache = GetLatestUserRecordQuery().FirstOrDefault();
            }
            else
            {
                // retrieve last written record from the DB
                var lastwriteInDb = GetLatestUserRecordQuery().Select(n => n.LastWrite).FirstOrDefault();

                // if the persisted copy is newer than the in-memory copy
                if (lastwriteInDb > InMemoryCache.LastWrite)
                {
                    // read from from storage, update in-memory copy
                    this.InMemoryCache = GetLatestUserRecordQuery().FirstOrDefault();
                }
            }

            // Send data to the TokenCache instance
            args.TokenCache.DeserializeMsalV3((InMemoryCache == null) ? null : InMemoryCache.CacheBits);
        }

        /// <summary>
        /// Clears the TokenCache's copy and the database copy of this user's cache.
        /// </summary>
        public void Clear()
        {
            // Delete from DB
            var cacheEntries = this.TokenCacheDb.PerUserCacheList.Where(c => c.WebUserUniqueId == this.GetSignedInUsersUniqueId());
            this.TokenCacheDb.PerUserCacheList.RemoveRange(cacheEntries);
            this.TokenCacheDb.SaveChanges();
        }

        private IOrderedQueryable<PerWebUserCache> GetLatestUserRecordQuery()
        {
            string userId = this.GetSignedInUsersUniqueId();
            return this.TokenCacheDb.PerUserCacheList.Where(c => c.WebUserUniqueId == userId)
                .OrderByDescending(d => d.LastWrite);
        }
    }

    public class PerWebUserCache
    {
        [Key]
        public int EntryId { get; set; }
        public string WebUserUniqueId { get; set; }
        public byte[] CacheBits { get; set; }
        public DateTime LastWrite { get; set; }

        /// <summary>
        /// Provided here as a precaution against concurrent updates by multiple threads.
        /// </summary>
        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}