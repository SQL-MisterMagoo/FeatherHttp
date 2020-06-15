using Microsoft.AspNetCore.Blazor.Hosting;
using System.Threading.Tasks;

namespace BlazorSample
{
    class Program
    {
        static void Main(string[] args)
        {
            BlazorWebAssemblyHost.CreateDefaultBuilder()
                .UseBlazorStartup<Startup>()
                .Build()
                .Run();
        }

    }
}