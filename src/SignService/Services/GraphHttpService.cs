using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using SignService.Models;
using SignService.Utils;

namespace SignService.Services
{
    public class GraphHttpService : IGraphHttpService
    {
        readonly AzureAdOptions azureAdOptions;
        readonly AdminConfig adminConfig;
        readonly AuthenticationContext adalContext;
        static readonly HttpMethod PatchMethod = new HttpMethod("PATCH");

        public GraphHttpService(IOptionsSnapshot<AzureAdOptions> azureAdOptions, IOptionsSnapshot<AdminConfig> adminConfig, IHttpContextAccessor contextAccessor)
        {
            this.azureAdOptions = azureAdOptions.Value;
            this.adminConfig = adminConfig.Value;

            var userId = contextAccessor.HttpContext.User.FindFirst("oid").Value;

            adalContext = new AuthenticationContext($"{azureAdOptions.Value.AADInstance}{azureAdOptions.Value.TenantId}", new ADALSessionCache(userId, contextAccessor));  
        }

        public async Task<T> Get<T>(string url)
        {
            using (var client = await CreateClient()
                                    .ConfigureAwait(false))
            {

                var response = await client.GetAsync($"{azureAdOptions.TenantId}/{url}").ConfigureAwait(false);

                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    object formatted = JsonConvert.DeserializeObject(responseContent);
                    throw new WebException("Error Calling the Graph API to update user: \n" +
                                           JsonConvert.SerializeObject(formatted, Formatting.Indented));
                }

                var jObject = JObject.Parse(responseContent);

                var x = jObject["value"];

                var values = x != null ? JArray.Parse(jObject["value"].ToString()) : new JArray(jObject);
                
                if (values != null && values.Count > 0)
                {
                    return values.ToObject<T>();
                }
                else
                {
                    return default(T);  // This returns null for reference types.
                }
            }
        }
        

        public async Task<TOutput> Post<TInput, TOutput>(string url, TInput item)
        {
            using (var client = await CreateClient()
                                    .ConfigureAwait(false))
            {
                var request = new StringContent(JsonConvert.SerializeObject(item), Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"{azureAdOptions.TenantId}/{url}", request).ConfigureAwait(false);
                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    object formatted = JsonConvert.DeserializeObject(responseContent);
                    throw new WebException("Error Calling the Graph API to update user: \n" +
                                           JsonConvert.SerializeObject(formatted, Formatting.Indented));
                }

                return JsonConvert.DeserializeObject<TOutput>(responseContent);
            }
        }


        public async Task Patch<TInput>(string url, TInput item)
        {
            using (var client = await CreateClient()
                                    .ConfigureAwait(false))
            {
                string contentBody = JsonConvert.SerializeObject(item);

                var request = new HttpRequestMessage(PatchMethod, $"{azureAdOptions.TenantId}/{url}")
                {
                    Content = new StringContent(contentBody, Encoding.UTF8, "application/json")
                };

                var response = await client.SendAsync(request).ConfigureAwait(false);
                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    object formatted = JsonConvert.DeserializeObject(responseContent);
                    throw new WebException("Error Calling the Graph API to update user: \n" +
                                           JsonConvert.SerializeObject(formatted, Formatting.Indented));
                }
            }
        }

        private async Task<HttpClient> CreateClient()
        {
            var accessToken = await adalContext.AcquireTokenSilentAsync("https://graph.windows.net", azureAdOptions.ClientId).ConfigureAwait(false);

            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.AccessToken);
            client.DefaultRequestHeaders
                   .Accept
                   .Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.BaseAddress = new Uri(adminConfig.GraphInstance);

            return client;
        }
    }
}