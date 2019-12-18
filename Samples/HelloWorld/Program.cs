using System;
using System.Collections;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

class RedisHub : Hub
{
    private RedisService _redisService;
    public RedisHub(RedisService redisService)
    {
        _redisService = redisService;
    }

    public override async Task OnConnectedAsync()
    {
        await _redisService.StartAsync();
    }
}

class RedisService : IAsyncDisposable
{
    private IHubContext<RedisHub> _context;
    private IConnectionMultiplexer _connection;
    private ILogger _logger;
    private IConfiguration _configuration;

    private object _lockObj = new object();
    private Task _startTask;

    public RedisService(IHubContext<RedisHub> context,
                        ILogger<RedisService> logger,
                        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.CloseAsync();
    }

    public Task StartAsync()
    {
        lock (_lockObj)
        {
            if (_startTask == null)
            {
                _startTask = DoStartAsync();
            }
        }

        return _startTask;
    }

    private async Task DoStartAsync()
    {
        var connectionString = _configuration["Redis:ConnectionString"];
        _connection = await ConnectionMultiplexer.ConnectAsync(connectionString, new LoggerTextWriter(_logger));
        var sub = _connection.GetSubscriber();
        await sub.SubscribeAsync("channel", async (key, value) =>
        {
            await _context.Clients.All.SendAsync("message", value.ToString());
        });
    }

    private class LoggerTextWriter : TextWriter
    {
        private readonly ILogger _logger;

        public LoggerTextWriter(ILogger logger)
        {
            _logger = logger;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {

        }

        public override void WriteLine(string value)
        {
            _logger.LogInformation(value);
        }
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebApplicationHost.CreateDefaultBuilder(args);

        builder.Services.AddSignalR();
        builder.Services.AddSingleton<RedisService>();

        var app = builder.Build();

        app.UseStaticFiles();

        app.MapHub<RedisHub>("/redis");

        app.MapGet("/", async context =>
        {
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("Hello World");
        });

        app.MapGet("/env", async context =>
        {
            context.Response.ContentType = "application/json";

            var configuration = context.RequestServices.GetRequiredService<IConfiguration>() as IConfigurationRoot;

            var vars = Environment.GetEnvironmentVariables()
                                  .Cast<DictionaryEntry>()
                                  .OrderBy(e => (string)e.Key)
                                  .ToDictionary(e => (string)e.Key, e => (string)e.Value);

            var data = new
            {
                version = Environment.Version.ToString(),
                env = vars,
                configuration = configuration.AsEnumerable().ToDictionary(c => c.Key, c => c.Value),
                configurtionDebug = configuration.GetDebugView(),
            };

            await JsonSerializer.SerializeAsync(context.Response.Body, data);
        });

        var client = new HttpClient();

        app.MapGet("/replicas", async context =>
        {
            context.Response.ContentType = "application/json";
            var k8sHost = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
            if (k8sHost == null)
            {
                await JsonSerializer.SerializeAsync(context.Response.Body, new { message = "Not running in k8s" });
                return;
            }
            var k8sPort = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");
            var podHostName = Environment.GetEnvironmentVariable("HOSTNAME");
            var scheme = k8sPort == "443" ? "https" : "http";
            var port = k8sPort == "443" || k8sPort == "80" ? "" : k8sPort;
            var hostAndPort = $"{scheme}://{k8sHost}:{port}";
            var deploymentNameIndex = podHostName.LastIndexOf('-');
            var deploymentName = "";
            if (deploymentNameIndex >= 0)
            {
                deploymentName = podHostName.Substring(0, deploymentNameIndex);
            }
            else
            {
                deploymentName = podHostName;
            }
            var url = $"{hostAndPort}/api/v1/namespaces/default/endpoints/{deploymentName}";
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                await JsonSerializer.SerializeAsync(context.Response.Body, new
                {
                    url = url,
                    status = response.StatusCode
                });
                return;
            }

            await (await response.Content.ReadAsStreamAsync()).CopyToAsync(context.Response.Body);
        });

        await app.RunAsync();
    }
}