using Microsoft.Extensions.DependencyInjection;
using RDesigner.Services;
using RDesigner.ViewModels;
using RDesigner.Views;
using System;

public class Startup
{
    public IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Регистрация сервисов
        services.AddSingleton<IDBService, PostgresDBService>();        

        services.AddTransient<MainViewModel>(); // Регистрация MainViewModel
        //services.AddTransient<MainView>();      // Регистрация MainView
        services.AddTransient<MainView>(provider => new MainView(provider)); // Регистрация MainView с передачей IServiceProvider

        // Регистрация главного окна
        services.AddTransient<MainWindow>();
            

        return services.BuildServiceProvider();
    }
}