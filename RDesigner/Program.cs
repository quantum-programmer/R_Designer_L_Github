using Avalonia;
using Serilog;
//using Serilog.Sinks.Console;
using Serilog.Sinks.File;
using Serilog.Events;
using System;

namespace RDesigner
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Настройка Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug() // Минимальный уровень логирования
                .WriteTo.Console() // Логи в консоль (опционально)
                .WriteTo.File(
                    path: "logs/RDesigner.log", // Путь к файлу логов
                    rollingInterval: RollingInterval.Day, // Ротация логов по дням
                    fileSizeLimitBytes: 10 * 1024 * 1024, // 10 МБ
                    rollOnFileSizeLimit: true, // Создавать новый файл при превышении размера
                    retainedFileCountLimit: 7, // Хранить логи за последние 7 дней
                    restrictedToMinimumLevel: LogEventLevel.Information, // Минимальный уровень для файла
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}" // Формат логов
                )
                .CreateLogger();

            try
            {
                Log.Information("Запуск приложения");

                // Ваш код инициализации приложения
                // Например, запуск Avalonia
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex) 
            {
                Log.Fatal(ex, "Приложение завершилось с ошибкой");
            }
            finally
            {
                Log.CloseAndFlush(); // Закрыть и очистить логгер
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
