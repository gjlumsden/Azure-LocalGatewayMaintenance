using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Microsoft.Azure.Services.AppAuthentication;
using Newtonsoft.Json;
using System.Text;
using System.Net;
using System.Web.Http;

namespace Networking.LocalGatewayIpUpdater
{
    public class LocalGatewayFunctions
    {
        const string LocalGatewayUriFormat = "https://management.azure.com/subscriptions/{0}/resourceGroups/{1}/providers/Microsoft.Network/localNetworkGateways/{2}?api-version=2020-04-01";
        private readonly HttpClient client;
        private readonly string subscriptionId;
        private readonly string resourceGroup;
        private readonly string localgatewayName;
        private readonly string localGatewayUri;

        public LocalGatewayFunctions(HttpClient client)
        {
            this.client = client;
            subscriptionId = Environment.GetEnvironmentVariable("SubscriptionId");
            resourceGroup = Environment.GetEnvironmentVariable("ResourceGroup");
            localgatewayName = Environment.GetEnvironmentVariable("LocalGatewayName");

            localGatewayUri = string.Format(LocalGatewayUriFormat, subscriptionId, resourceGroup, localgatewayName);
        }

        [FunctionName("LocalGatewayPublicIpFunction")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "gateway/ip")] HttpRequest req,
            ILogger log)
        {
            return await UpdateCurrentLocalGatewayIp(req, log);
        }

        private async Task<IActionResult> UpdateCurrentLocalGatewayIp(HttpRequest req, ILogger log)
        {
            try
            {
                var requestedIp = await req.ReadAsStringAsync();
                if(!IPAddress.TryParse(requestedIp, out _))
                {
                    return new BadRequestErrorMessageResult("Supplied value was not a valid IP Address.");
                }
                log.LogInformation($"Requested IP address is {requestedIp}");
                var localGateway = await GetLocalGatewayRequest();
                var currentIp = (string)GetIPFromResult(localGateway);
                log.LogInformation($"Current Local Gateway IP Address is {currentIp}");

                if (requestedIp != currentIp)
                {
                    log.LogInformation($"IP Addresses are different. Attempting to update the Local Gateway.");
                    var token = await GetAccessToken();
                    localGateway.properties.gatewayIpAddress = requestedIp;
                    using var request = new HttpRequestMessage(HttpMethod.Put, localGatewayUri);
                    request.Content = new StringContent(JsonConvert.SerializeObject(localGateway), Encoding.UTF8, "application/json");
                    request.Headers.Add("Authorization", $"Bearer {token}");
                    var result = await client.SendAsync(request);

                    log.LogInformation($"Received response {result.StatusCode}");

                    if (result.IsSuccessStatusCode)
                    {
                        var resultGateway = JsonConvert.DeserializeObject<dynamic>(await result.Content.ReadAsStringAsync());
                        if ((string)resultGateway.properties.gatewayIpAddress == requestedIp)
                        {
                            return new OkObjectResult(new { Updated = true, CurrentIp = resultGateway.properties.gatewayIpAddress });
                        }
                        else
                        {
                            log.LogWarning($"Received a success response, but the expected IP was not set. Current gateway IP is {resultGateway.properties.gatewayIpAddress}");
                            return new InternalServerErrorResult();
                        }
                    }
                    else
                    {
                        log.LogError($"Did not receive a success status code. Local Gateway was not updated. Response was: {await result.Content.ReadAsStringAsync()}");
                        return new InternalServerErrorResult();
                    }
                }
                return new NoContentResult();
            }
            catch(Exception ex)
            {
                log.LogError(ex, "Failed to update the Local Gateway IP Address :(");
                return new InternalServerErrorResult();
            }
        }

        private async Task<dynamic> GetLocalGatewayRequest()
        {
            var token = await GetAccessToken();
            using var request = new HttpRequestMessage(HttpMethod.Get, localGatewayUri);
            request.Headers.Add("Authorization", $"Bearer {token}");
            using var response = await client.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<dynamic>(result);
        }

        private static dynamic GetIPFromResult(dynamic localGateway)
        {
            var ip = localGateway.properties.gatewayIpAddress;
            return ip;
        }

        private async Task<string> GetAccessToken()
        {
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            var accessToken = await azureServiceTokenProvider.GetAccessTokenAsync("https://management.azure.com/");
            return accessToken;
        }
    }
}
