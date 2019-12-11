using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TodoListService;
using TodoSPA.DAL;
using TodoSPA.Models;

namespace TodoSPA.Controllers
{
    [Authorize]
    public class CalendarController : ApiController
    {
        private static string appKey = ConfigurationManager.AppSettings["ida:AppKey"];
        private static string redirectUri = ConfigurationManager.AppSettings["ida:RedirectUri"];
        private static string aadInstance = ConfigurationManager.AppSettings["ida:AADInstance"];
        private static string tenant = ConfigurationManager.AppSettings["ida:Tenant"];
        private static string clientId = ConfigurationManager.AppSettings["ida:ClientID"];
        private TodoListServiceContext dbContext = new TodoListServiceContext();
        private static string graphUserUrl = ConfigurationManager.AppSettings["ida:GraphUserUrl"];
        private static string graphCalendarUrl = ConfigurationManager.AppSettings["ida:graphCalendarUrl"];
        private static string[] scopes = { "Calendars.ReadWrite" };

        public async Task<Event[]> Get()
        {
            return await CallGraphAPIOnBehalfOfUser();
        }

        private async Task<Event[]> CallGraphAPIOnBehalfOfUserV2()
        {
            IConfidentialClientApplication confidentialClientApplication = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithRedirectUri(redirectUri)
                .WithClientSecret(appKey)
                .Build();

            OnBehalfOfProvider authProvider = new OnBehalfOfProvider(confidentialClientApplication, scopes);
            GraphServiceClient graphClient = new GraphServiceClient(authProvider);

            var events = await graphClient.Me.Events
                .Request()
                .Header("Prefer", "outlook.timezone=\"Pacific Standard Time\"")
                .Select(e => new
                {
                    e.Subject,
                    e.Body,
                    e.BodyPreview,
                    e.Organizer,
                    e.Attendees,
                    e.Start,
                    e.End,
                    e.Location
                })
                .GetAsync();

            return events.ToArray();
        }

        private async Task<Event[]> CallGraphAPIOnBehalfOfUser()
        {

            // We will use MSAL.NET to get a token to call the API On Behalf Of the current user
            try
            {
                string authority = String.Format(CultureInfo.InvariantCulture, aadInstance, tenant);

                // Creating a ConfidentialClientApplication using the Build pattern (https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/Client-Applications)
                var app = ConfidentialClientApplicationBuilder.Create(clientId)
                   .WithAuthority(authority)
                   .WithClientSecret(appKey)
                   .WithRedirectUri(redirectUri)
                   .Build();

                // Hooking MSALPerUserSqlTokenCacheProvider class on ConfidentialClientApplication's UserTokenCache.
                MSALPerUserSqlTokenCacheProvider sqlCache = new MSALPerUserSqlTokenCacheProvider(app.UserTokenCache, dbContext, ClaimsPrincipal.Current);

                //Grab the Bearer token from the HTTP Header using the identity bootstrap context. This requires SaveSigninToken to be true at Startup.Auth.cs
                var bootstrapContext = ClaimsPrincipal.Current.Identities.First().BootstrapContext.ToString();

                // Creating a UserAssertion based on the Bearer token sent by TodoListClient request.
                //urn:ietf:params:oauth:grant-type:jwt-bearer is the grant_type required when using On Behalf Of flow: https://docs.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-on-behalf-of-flow
                UserAssertion userAssertion = new UserAssertion(bootstrapContext, "urn:ietf:params:oauth:grant-type:jwt-bearer");

                // Acquiring an AuthenticationResult for the scope user.read, impersonating the user represented by userAssertion, using the OBO flow
                AuthenticationResult result = await app.AcquireTokenOnBehalfOf(scopes, userAssertion)
                    .ExecuteAsync();

                string accessToken = result.AccessToken;
                if (accessToken == null)
                {
                    throw new Exception("Access Token could not be acquired.");
                }

                // Call the Graph API and retrieve the user's profile.
                string requestUrl = String.Format(CultureInfo.InvariantCulture, graphCalendarUrl, HttpUtility.UrlEncode(tenant));
                HttpClient client = new HttpClient();
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                HttpResponseMessage response = await client.SendAsync(request);

                // Return the user's profile.
                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(responseString);
                    return (json["value"].ToObject<Event[]>());
                }

                // An unexpected error occurred calling the Graph API.
                throw new Exception("An unexpected error occurred calling the Graph API.");
            }
            catch (MsalUiRequiredException msalServiceException)
            {
                /*
                * If you used the scope `.default` on the client application, the user would have been prompted to consent for Graph API back there
                * and no incremental consents are required (this exception is not expected). However, if you are using the scope `access_as_user`,
                * this exception will be thrown at the first time the API tries to access Graph on behalf of the user for an incremental consent.
                * You must then, add the logic to delegate the consent screen to your client application here.
                * This sample doesn't use the incremental consent strategy.
                */
                throw msalServiceException;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
