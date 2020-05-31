using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PublicIpWorker
{
    public class Worker : BackgroundService
    {
        private const string IpRequestUri = "https://ifconfig.me/ip";
        private readonly ILogger<Worker> _logger;
        private readonly HttpClient client;
        private readonly string functionUri;

        public Worker(ILogger<Worker> logger, IHttpClientFactory clientFactory, IConfiguration configuration)
        {
            _logger = logger;
            client = clientFactory.CreateClient();
            functionUri = configuration.GetValue<string>("FunctionUri");
            if (string.IsNullOrWhiteSpace(functionUri))
            {
                throw new ArgumentException("Required parameter was not supplied.", "FunctionUri");
            }
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string currentIp = null;
            while (!stoppingToken.IsCancellationRequested)
            {

                var ip = await GetCurrentHostIp();
                _logger.LogInformation($"Worker running at: {DateTimeOffset.UtcNow} (UTC). Current IP is {ip}.");

                if (ip != null && currentIp!=ip)
                {
                    _logger.LogInformation($"Worker running at: {DateTimeOffset.UtcNow} (UTC). Checking local gateway connection is up to date with correct ip.");

                    if(await UpdateLocalGatewayConnectionIp(ip))
                    {
                        currentIp = ip;
                    }
                }
                else
                {
                    _logger.LogInformation($"IP Address is unchanged or unable to retrieve the current IP Address.");
                }

                _logger.LogInformation($"Worker running at: {DateTimeOffset.UtcNow} (UTC). Waiting for 10 minutes.");

                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        private async Task<bool> UpdateLocalGatewayConnectionIp(string ip)
        {
            if (ip == null)
                throw new ArgumentNullException(nameof(ip));

            try
            {
                using var response = await client.PostAsync(functionUri, new StringContent(ip));
                return response.IsSuccessStatusCode;
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to call Function to check for IP change. Error suppressed. ");
                return false;
            }
        }

        private async Task<string> GetCurrentHostIp()
        {
            try
            {
                var response = await client.GetAsync(IpRequestUri);

                var ip = await response.Content.ReadAsStringAsync();

                return ip;
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve current IP. Error will be suppressed.");
                return null;
            }
        }
    }
}
