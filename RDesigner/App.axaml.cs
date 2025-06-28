using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using RDesigner.ViewModels;
using RDesigner.Views;
using System;
using Microsoft.Extensions.DependencyInjection;


namespace RDesigner
{
    public partial class App : Application
    {
        public IServiceProvider _serviceProvider;
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var startup = new Startup();
            _serviceProvider = startup.ConfigureServices();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Создание главного окна через DI контейнер
                //desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                desktop.MainWindow = new MainWindow
                {
                    Content = _serviceProvider.GetRequiredService<MainView>()                   
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}