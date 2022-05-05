using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GetRolesForUser
{
    public static class Function1
    {
        private static ILogger _logger;
        private static string _objectId; // User
        private static string _clientId;// App
        private static string _tenantId; // Tenant
        private static string _scope;  // "roles", "roles groups" or "groups"

        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]
            HttpRequest req,
            ILogger log)
        {
            _logger = log;
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);
                _objectId = data.objectId; // User
                _clientId = data.clientId; // App
                _tenantId = data.tenantId; // Tenant
                _scope = data.scope; // "roles", "roles groups" or "groups"

                var accessToken = await GetAccessToken();
                var userData = await GetAssignedRolesIds(accessToken);
                if (userData == null)
                {
                    return new NotFoundResult();
                }

                var roleNames = await MatchRoleIdsToNames(accessToken, userData);
                return new JsonResult(
                    new
                    {
                        roles = roleNames
                    });
            }
            catch (Exception ex)
            {
                log.LogCritical(ex.Message);
                return new ExceptionResult(ex, true);
            }
        }

        private static async Task<JObject> GetAssignedRolesIds(string accessToken)
        {
            var httpClient = new HttpClient();
            try
            {
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                var url =
                    $"https://graph.microsoft.com/beta/users/{_objectId}/appRoleAssignments?$select=appRoleId,resourceId,resourceDisplayName";
                _logger.LogInformation(url);

                var res = await httpClient.GetAsync(url);
                _logger.LogInformation("HttpStatusCode=" + res.StatusCode.ToString());
                if (!res.IsSuccessStatusCode)
                {
                    return null;
                }

                var respData = await res.Content.ReadAsStringAsync();
                _logger.LogInformation(respData);
                return JObject.Parse(respData);
            }
            finally
            {
                httpClient.Dispose();
            }
        }

        private static async Task<List<string>> MatchRoleIdsToNames(string accessToken, JObject userData)
        {
            var roleNames = new List<string>();
            var httpClient = new HttpClient();
            try
            {
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                var url =
                    $"https://graph.microsoft.com/beta/servicePrincipals?$filter=appId eq '{_clientId}'&$select=id,appRoles";
                _logger.LogInformation(url);
                var res = await httpClient.GetAsync(url);
                JArray appRolesArray = null;
                var spId = "";
                if (!res.IsSuccessStatusCode)
                {
                    return roleNames;
                }

                var respData = res.Content.ReadAsStringAsync().Result;
                var spoData = JObject.Parse(respData);
                foreach (var item in spoData["value"])
                {
                    spId = item["id"].ToString();
                    appRolesArray = (JArray)item["appRoles"];
                    roleNames.AddRange(from itemUser in userData["value"]
                        where spId == itemUser["resourceId"].ToString()
                        select itemUser["appRoleId"].ToString()
                        into appRoleId
                        from role in appRolesArray
                        where appRoleId == role["id"].ToString()
                        select role["value"].ToString());
                }

                return roleNames;
            }
            finally
            {
                httpClient.Dispose();
            }
        }

        private static async Task<string> GetAccessToken()
        {
            var client = new HttpClient();
            var dict = new Dictionary<string, string>
            {
                { "grant_type", "client_credentials" },
                { "client_id", _clientId },
                { "client_secret", "7wO7Q~KYI2b0Z2rlTJjoBwETK_tm7x4XmlApF" },
                { "resource", "https://graph.microsoft.com" },
                { "_scope", "User.Read.All AppRoleAssignment.Read.All" }
            };

            var urlTokenEndpoint = $"https://login.microsoftonline.com/{_tenantId}/oauth2/token?api-version=1.0";

            var resp = client.PostAsync(urlTokenEndpoint, new FormUrlEncodedContent(dict)).Result;
            var contents = await resp.Content.ReadAsStringAsync();
            client.Dispose();

            // If the client creds failed, return error
            return JObject.Parse(contents)["access_token"].ToString();
        }
    }
}
