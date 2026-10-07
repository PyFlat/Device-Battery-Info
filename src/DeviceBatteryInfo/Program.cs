using DeviceBatteryInfo;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = MacroDeckPlugin
    .CreatePlugin(args)
    .UseMacroDeckLogging()
    .UseLocalization(Strings.LocalizationCatalog)
    .RegisterIntegration<BatteryIntegration>();

builder
    .Services.AddOptions<BatteryPluginOptions>()
    .Bind(builder.Configuration.GetSection(BatteryPluginOptions.SectionName));

builder.Services.AddSingleton<BatteryRegistry>();
builder.Services.AddSingleton<BatteryTrendTracker>();
builder.Services.AddSingleton<ChargingInference>();
builder.Services.AddSingleton<DeviceCatalog>();
builder.Services.AddBatterySources();

builder.Services.AddSingleton<BatteryPollingService>();
builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<BatteryPollingService>());

var plugin = builder.Build();

await plugin.RunAsync();
