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
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.X509;

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
    private const string ServiceAccountPath = "/var/run/secrets/kubernetes.io/serviceaccount/";
    private const string ServiceAccountTokenKeyFileName = "token";
    private const string ServiceAccountRootCAKeyFileName = "ca.crt";

    static async Task Main(string[] args)
    {
        var builder = WebApplicationHost.CreateDefaultBuilder(args);

        builder.Services.AddSignalR();
        builder.Services.AddSingleton<RedisService>();

        var app = builder.Build();

        app.UseDeveloperExceptionPage();

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

        var handler = new HttpClientHandler();
        var client = new HttpClient(handler);
        var tokenPath = Path.Combine(ServiceAccountPath, ServiceAccountTokenKeyFileName);
        var certPath = Path.Combine(ServiceAccountPath, ServiceAccountRootCAKeyFileName);
        var token = "";

        var k8sHost = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
        var k8sPort = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");
        var podHostName = Environment.GetEnvironmentVariable("HOSTNAME");

        if (k8sHost != null)
        {
            token = File.ReadAllText(tokenPath);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var certs = LoadPemFileCert(certPath);

            foreach (var c in certs)
            {
                handler.ClientCertificates.Add(c);
            }
        }

        app.MapGet("/replicas", async context =>
        {
            context.Response.ContentType = "application/json";
            if (k8sHost == null)
            {
                await JsonSerializer.SerializeAsync(context.Response.Body, new { message = "Not running in k8s" });
                return;
            }
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
            var url = $"{hostAndPort}/api/v1/namespaces/default/pods/{deploymentName}";
            var response = await client.GetAsync(url);

            await (await response.Content.ReadAsStreamAsync()).CopyToAsync(context.Response.Body);
        });

        await app.RunAsync();
    }

    public static X509Certificate2Collection LoadPemFileCert(string file)
    {
        var certs = new X509CertificateParser().ReadCertificates(File.OpenRead(file));
        var certCollection = new X509Certificate2Collection();

        // Convert BouncyCastle X509Certificates to the .NET cryptography implementation and add
        // it to the certificate collection
        //
        foreach (Org.BouncyCastle.X509.X509Certificate cert in certs)
        {
            certCollection.Add(new X509Certificate2(cert.GetEncoded()));
        }

        return certCollection;
    }
}