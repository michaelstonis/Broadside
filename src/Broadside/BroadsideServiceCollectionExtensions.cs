using Broadside;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// The registration lives in the container's namespace, as the Microsoft.Extensions convention asks, so AddBroadside is found
// next to AddLogging and AddOptions without another using directive.
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>Registers Broadside with a dependency-injection container.</summary>
/// <remarks>
/// <para>
/// Spec #33, user stories 43 to 45: a hosted application gets one <see cref="PdfEngine"/>, a singleton, built from the same
/// <see cref="PdfOptions"/> the fluent entry point takes, so it opens documents exactly as <see cref="PdfDocument.Open(string)"/>
/// does. Nothing is registered globally: two containers hold two independent engines.
/// </para>
/// <para>
/// The options go through the options pattern, so configuration delegates run in registration order and post-configuration runs
/// last. To bind them from configuration, the host adds its own binding, for instance
/// <c>services.AddOptions&lt;PdfOptions&gt;().BindConfiguration("Broadside")</c> from
/// <c>Microsoft.Extensions.Options.ConfigurationExtensions</c>. When the container has an <see cref="ILoggerFactory"/>, every
/// diagnostic is also logged through it. Documents are not registered: the caller opens and disposes them.
/// </para>
/// </remarks>
public static class BroadsideServiceCollectionExtensions
{
    /// <summary>Registers the <see cref="PdfEngine"/> singleton and its <see cref="PdfOptions"/>, with default options.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddBroadside(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<PdfOptions>();
        services.TryAddSingleton(static provider => new PdfEngine(
            provider.GetRequiredService<IOptions<PdfOptions>>(),
            provider.GetService<ILoggerFactory>()));
        return services;
    }

    /// <summary>
    /// Registers the <see cref="PdfEngine"/> singleton and its <see cref="PdfOptions"/>, configured by <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Sets the options fluently, as on the static entry point, for instance <c>options =&gt; options.UseStrict()</c>. Calling
    /// <c>AddBroadside</c> again registers no second engine; it adds its delegate, which runs after this one.
    /// </param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddBroadside(this IServiceCollection services, Action<PdfOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddBroadside().Configure(configure);
        return services;
    }
}
