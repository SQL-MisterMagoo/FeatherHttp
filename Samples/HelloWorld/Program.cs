using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections;
using System.Text;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebApplicationHost.CreateDefaultBuilder(args);

        var app = builder.Build();

        app.MapGet("/", async context =>
        {
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("Hello World");
        });

        app.MapGet("/version", async context =>
        {
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(Environment.Version.ToString());
        });

        app.MapGet("/env", async context =>
        {
            context.Response.ContentType = "text/plain";
            var sb = new StringBuilder();
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                sb.AppendLine(entry.Key + " = " + entry.Value);
            }
            await context.Response.WriteAsync(sb.ToString());
        });

        await app.RunAsync();
    }
}