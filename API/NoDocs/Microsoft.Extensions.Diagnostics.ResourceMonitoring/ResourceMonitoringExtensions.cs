// Assembly 'Microsoft.Extensions.Diagnostics.ResourceMonitoring'

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring.Internal;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Diagnostics.ResourceMonitoring;

public static class ResourceMonitoringExtensions
{
    public static IServiceCollection AddResourceMonitoring(this IServiceCollection services);
    public static IServiceCollection AddResourceMonitoring(this IServiceCollection services, Action<IResourceMonitorBuilder> configure);
    public static IResourceMonitorBuilder ConfigureMonitor(this IResourceMonitorBuilder builder, Action<ResourceMonitoringOptions> configure);
    public static IResourceMonitorBuilder ConfigureMonitor(this IResourceMonitorBuilder builder, IConfigurationSection section);
}
