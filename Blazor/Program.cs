using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BlazorTwins311.Lib;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebApplicationHost.CreateDefaultBuilder(args);
        builder.Services.AddRazorPages();
        builder.Services.AddServerSideBlazor();
        builder.Services.AddSingleton<TestCase, TestCase>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IPreRenderFlag, PreRenderFlag>();
        var app = builder.Build();
        app.MapRazorPages();
        app.UseStaticFiles();
        app.UseRouting();
        app.MapControllers();
        app.MapBlazorHub();
        app.MapFallbackToPage("/Index");
        //OpenBrowser("http://localhost:5000/");
        await app.RunAsync();
    }

    public static void OpenBrowser(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
        else
        {
            // throw 
        }
    }
}
