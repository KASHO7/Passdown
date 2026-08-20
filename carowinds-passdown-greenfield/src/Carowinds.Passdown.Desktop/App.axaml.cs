using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Infrastructure;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Carowinds.Passdown.Desktop;

public partial class App : global::Avalonia.Application
{
    private IServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _services = BuildServiceProvider();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = _services ?? throw new InvalidOperationException(
                $"{nameof(_services)} was not initialized; {nameof(Initialize)} must run first.");

            // Storage must be ready (spec §17 Phase 1: "establish ... storage
            // migrations before building the production UI") before any
            // view model touches the database.
            services.GetRequiredService<MigrationRunner>().Run();

            var mainWindow = services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder
            .AddConfiguration(configuration.GetSection("Logging"))
            .AddConsole());

        services.Configure<StorageOptions>(configuration.GetSection("Storage"));

        services.AddSingleton(provider =>
        {
            var storageOptions = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            var databasePath = Path.Combine(AppPaths.LocalApplicationDataDirectory, storageOptions.DatabaseFileName);
            return new SqliteConnectionFactory(databasePath);
        });

        services.AddSingleton<IReadOnlyList<IMigration>>([new Migration0001CreateSchemaVersionTable()]);
        services.AddSingleton<MigrationRunner>();

        services.AddTransient<MainWindow>();
        services.AddTransient<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
