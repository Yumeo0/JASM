﻿using System.Diagnostics;
using System.Threading.RateLimiting;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.WinUI.Activation;
using GIMI_ModManager.WinUI.BackgroundServices;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.ViewModels;
using GIMI_ModManager.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Polly;
using Polly.RateLimiting;
using Polly.Retry;
using Serilog;
using Serilog.Events;
using Serilog.Templates;

namespace GIMI_ModManager.WinUI;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host { get; }

    public static T GetService<T>()
        where T : class
    {
        if ((Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");

        return service;
    }

    public static string TMP_DIR { get; } = Path.Combine(Path.GetTempPath(), "JASM_TMP");
    public static string ROOT_DIR { get; } = AppDomain.CurrentDomain.BaseDirectory;
    public static WindowEx MainWindow { get; } = new MainWindow();

    public static UIElement? AppTitlebar { get; set; }

    public App()
    {
        InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .UseSerilog((context, configuration) =>
            {
                configuration.MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning);
                configuration.Filter.ByExcluding(logEvent =>
                    logEvent.Exception is RateLimiterRejectedException);
                configuration.Enrich.FromLogContext();
                configuration.ReadFrom.Configuration(context.Configuration);
                var mt = new ExpressionTemplate(
                    "[{@t:yyyy-MM-dd'T'HH:mm:ss} {@l:u3} {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}] {@m}\n{@x}");
                configuration.WriteTo.File(formatter: mt, "logs\\log.txt");
                if (Debugger.IsAttached) configuration.WriteTo.Debug();
            })
            .ConfigureServices((context, services) =>
            {
                // Default Activation Handler
                services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

                // Other Activation Handlers
                services.AddTransient<IActivationHandler, FirstTimeStartupActivationHandler>();

                // Services
                services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
                services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
                services.AddSingleton<INavigationViewService, NavigationViewService>();

                services.AddSingleton<IActivationService, ActivationService>();
                services.AddSingleton<IPageService, PageService>();
                services.AddSingleton<INavigationService, NavigationService>();

                services.AddSingleton<IWindowManagerService, WindowManagerService>();
                services.AddSingleton<NotificationManager>();
                services.AddSingleton<ModNotificationManager>();
                services.AddTransient<ModDragAndDropService>();

                services.AddSingleton<ElevatorService>();
                services.AddSingleton<GenshinProcessManager>();
                services.AddSingleton<ThreeDMigtoProcessManager>();

                services.AddSingleton<UpdateChecker>();

                // Core Services
                services.AddSingleton<IFileService, FileService>();
                services.AddSingleton<IGenshinService, GenshinService>();
                services.AddSingleton<ISkinManagerService, SkinManagerService>();
                services.AddSingleton<ModCrawlerService>();

                services.AddHttpClient<IModUpdateChecker, GameBanana>(client =>
                {
                    client.BaseAddress = new Uri("https://gamebanana.com/");
                    client.DefaultRequestHeaders.Add("User-Agent", "JASM-Just_Another_Skin_Manager-Update-Checker");
                    client.DefaultRequestHeaders.Add("Accept", "application/json");
                });

                services.AddResiliencePipeline(GameBanana.HttpClientName, (builder, context) =>
                {
                    builder
                        .AddTimeout(TimeSpan.FromSeconds(10))
                        .AddRateLimiter(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions()
                        {
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0,
                            TokenLimit = 10,
                            AutoReplenishment = true,
                            TokensPerPeriod = 2,
                            ReplenishmentPeriod = TimeSpan.FromSeconds(1)
                        }))
                        .AddRetry(new RetryStrategyOptions()
                        {
                            BackoffType = DelayBackoffType.Linear,
                            UseJitter = true,
                            MaxRetryAttempts = 8,
                            Delay = TimeSpan.FromMilliseconds(200)
                        });
                    builder.TelemetryListener = null;
                });

                services.AddTransient<IModUpdateChecker, GameBanana>();

                services.AddHostedService<ModUpdateAvailableChecker>();

                // Views and ViewModels
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<StartupViewModel>();
                services.AddTransient<StartupPage>();
                services.AddTransient<ShellPage>();
                services.AddTransient<ShellViewModel>();
                services.AddTransient<NotificationsViewModel>();
                services.AddTransient<NotificationsPage>();
                services.AddTransient<CharactersViewModel>();
                services.AddTransient<CharactersPage>();
                services.AddTransient<CharacterDetailsViewModel>();
                services.AddTransient<CharacterDetailsPage>();
                services.AddTransient<DebugViewModel>();
                services.AddTransient<DebugPage>();

                // Configuration
                services.Configure<LocalSettingsOptions>(
                    context.Configuration.GetSection(nameof(LocalSettingsOptions)));
            }).Build();
        Task.Run(() => { Host.StartAsync(); });

        UnhandledException += App_UnhandledException;
    }

    private async void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception");
        await Log.CloseAndFlushAsync();
    }


    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        NotImplemented.NotificationManager = GetService<NotificationManager>();
        base.OnLaunched(args);
        await GetService<IActivationService>().ActivateAsync(args);
    }
}