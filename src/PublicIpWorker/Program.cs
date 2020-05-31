using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;

namespace PublicIpWorker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHttpClient<Worker>()
                        .AddTransientHttpErrorPolicy(p =>
                            p.WaitAndRetryAsync(10, _ => TimeSpan.FromSeconds(1)));

                    services.AddHostedService<Worker>();
                })
                .ConfigureAppConfiguration(config =>
                    config.AddUserSecrets(Assembly.GetExecutingAssembly())
                        .AddCommandLine(args)
                            .AddEnvironmentVariables());
    }
}
