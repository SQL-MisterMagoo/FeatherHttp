﻿using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var builder = WebApplicationHost.CreateDefaultBuilder(args);

        builder.Configuration.AddYamlFile("appsettings.yml", optional: true);

        builder.UseSerilog();

        builder.UseServiceProviderFactory(new AutofacServiceProviderFactory());

        var app = builder.Build();

        app.Listen("http://localhost:3000");

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();

        app.MapGet("/", async context =>
        {
            await context.Response.WriteAsync("Hello World");
        });

        await app.RunAsync();
    }
}