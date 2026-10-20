using Broadside.Viewer.Blazor;
using Broadside.Viewer.Blazor.Sample;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// The render worker (#357) runs this Main too: there it only serves tiles, with the engine the page opens documents with.
if (RenderWorker.IsWorker)
{
    RenderWorker.Serve(static () => Demo.Options.Engine!);
    return;
}

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<Demo>();
await builder.Build().RunAsync().ConfigureAwait(false);
