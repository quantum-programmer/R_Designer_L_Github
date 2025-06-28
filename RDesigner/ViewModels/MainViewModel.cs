using RDesigner.Services;
using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RDesigner.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastReport;
using System.IO;
using FastReport.Export.Dbf;
using Npgsql;
using RDesigner.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Serilog;
using Newtonsoft.Json;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;

namespace RDesigner.ViewModels;

    public partial class MainViewModel:ViewModelBase
    {
        private readonly IDBService _dbService;
    
        [ObservableProperty]
        private ObservableCollection<ARMReport> reports = new();
        
        private ObservableCollection<ARMReport> FRXReports = new();

        [ObservableProperty]
        private ARMReport? _selectedReport;

        public MainViewModel(IDBService myService)
        {
            this._dbService = myService;
            LoadReportsAsync();
            _ = StartListeningForReportsChangeAsync(); // Запуск прослушивания уведомлений
        }
        private async Task LoadReportsAsync()
        {
            try
            {
                var reportsFromDb = await _dbService.GetAllReportsAsync();
                Reports = new ObservableCollection<ARMReport>(reportsFromDb);
                Log.Information("Отчеты успешно загружены из базы данных");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при загрузке отчетов из базы данных"); // Использование Log
            }
        }

    /* private string DuplicateName(string name)
     {
         // Получаем путь к каталогу и имя файла без расширения
         string directory = Path.GetDirectoryName(name); // Каталог
         string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(name); // Имя файла без расширения
         string extension = Path.GetExtension(name); // Расширение файла

         // Начинаем с суффикса "_1"
         int counter = 1;
         string newName;

         // Проверяем, существует ли файл с таким именем
         do
         {
             // Формируем новое имя файла с суффиксом
             newName = Path.Combine(directory, $"{fileNameWithoutExtension}_{counter}{extension}");
             counter++;
         }
         while (File.Exists(newName)); // Повторяем, пока не найдем уникальное имя

         return newName; // Возвращаем уникальное имя файла
     }*/

        // Вспомогательный метод для отображения сообщения об ошибке
        private async Task ShowErrorMessageAsync(string title, string message)
        {
            var messageBox = MessageBoxManager.GetMessageBoxStandard(
                title,
                message,
                ButtonEnum.Ok,
                Icon.Error
            );

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                await messageBox.ShowWindowDialogAsync(desktop.MainWindow);
            }
        }

        // Команда для кнопки "Просмотр"
        [RelayCommand]
        private async void ViewReport()
        {
            if (SelectedReport != null)
            {
                try 
                {
                    // Загрузка отчета из бинарных данных
                    using (MemoryStream stream = new MemoryStream(SelectedReport.reportData))
                    {
                        Report report = new Report();
                        report.Load(stream);
                        // Отображение отчета
                        report.Show();
                        Log.Information($"Report: {SelectedReport.reportData} успешно открыт для просмотра");
                    }
                }
                catch (FileNotFoundException ex)
                {
                    Log.Error(ex, $"Ошибка: файл отчета не найден.");
                    await ShowErrorMessageAsync("Ошибка", "Файл отчета не найден.");
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Error(ex, $"Ошибка: нет доступа к файлу отчета.");
                    await ShowErrorMessageAsync("Ошибка", "Нет доступа к файлу отчета.");
                }
                catch (IOException ex)
                {
                    Log.Error(ex, $"Ошибка ввода-вывода при открытии отчета.");
                    await ShowErrorMessageAsync("Ошибка", "Ошибка ввода-вывода при открытии отчета.");
                }
                catch (Exception ex) // Общий случай для всех остальных исключений
                {
                    Log.Error(ex, $"Неизвестная ошибка при открытии отчета.");
                    await ShowErrorMessageAsync("Ошибка", "Неизвестная ошибка при открытии отчета.");
                }
            }
        }

        // Команда для кнопки "Создать"
        [RelayCommand]    
        private async void CreateReport()
        {
            var Owner = (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var createReportViewModel = new CreateReportViewModel(_dbService);
            var createReportWindow = new CreateReportView { DataContext = createReportViewModel };
            //createReportWindow.ShowDialog(Owner);
            createReportWindow.Title = "Создать отчет";
            // Показываем окно и ждем результата
            var result = await createReportWindow.ShowDialog<bool>(Owner);
            // Если нажата кнопка "Сохранить"
            if (result)
            {
                // Получаем введенное имя отчета
                string reportName = string.IsNullOrEmpty(createReportViewModel.ReportName)
                    ? "Без названия"
                    : createReportViewModel.ReportName;
                // Получаем введенное описание отчета
                string reportDescription = string.IsNullOrEmpty(createReportViewModel.ReportDescription)
                    ? "Без названия"
                    : createReportViewModel.ReportDescription;
                byte[] reportBytes;

                // Путь к исходному файлу
                // string sourceFilePath = @"Fillings_FINAL.frx";                                
                string copyFilePath = Path.Combine(AppContext.BaseDirectory, $"{reportName}.frx");
                try
                {                    
                    Report report = new Report();
                    report.Design();
                    report.Save(copyFilePath);
                    Log.Information($"Копия файла {reportName}.frx сохранена в каталог");

                    using (MemoryStream stream = new MemoryStream())
                    {                       
                        report.Save(stream);
                        reportBytes = stream.ToArray();
                    }
                }

                catch (Exception ex)
                {
                    // Обработка ошибок (например, если файл не найден или нет прав доступа)
                    Log.Error($"Ошибка при создании файла отчета: {ex.Message}");
                    return;
                }

                ARMReport newReport = new();
                newReport.ParentID = 0;
                newReport.UniqueID = 1;
                newReport.Name = reportName;
                newReport.isFolder = false;
                newReport.isDelete = false;
                newReport.Description = reportDescription;
                newReport.reportData =  reportBytes;

                try
                {
                    if (await _dbService.InsertReportAsync(newReport))
                    {
                        Log.Information($"Файл {reportName}.frx сохранен в базу данных");
                        File.Delete(copyFilePath);
                        Log.Information($"Копия файла {reportName}.frx удалена из каталога");
                    }
                    else
                    {
                        Log.Error("Отчет не был сохранен в базу данных. Копия файла сохранена в каталог приложения.");
                    }
                }
                catch (IOException ioEx)
                {
                    // Обработка ошибок, связанных с файловой системой
                    Log.Error(ioEx, $"Ошибка при удалении файла {reportName}.frx. Возможно, файл занят другим процессом или отсутствуют права доступа.");
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    // Обработка ошибок, связанных с отсутствием прав доступа
                    Log.Error(uaEx, $"Ошибка при удалении файла {reportName}.frx. Недостаточно прав доступа.");
                }
                catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
                {
                    Log.Error(ex, "Ошибка при работе с базой данных (PostgreSQL)");
                }

                catch (Exception ex) // Обработка всех остальных исключений
                {
                    Log.Error(ex, "Неизвестная ошибка при сохранении отчета в базу данных");
                }
            }                
        }


        private async void CreateReport(ARMReport report)
        {
            string copyFilePath = Path.Combine(AppContext.BaseDirectory, $"{report.Name}.frx");
            try
            {
                if (await _dbService.InsertReportAsync(report))
                {
                    Log.Information($"Файл {report.Name}.frx сохранен в базу данных");
                    File.Delete(copyFilePath);
                    Log.Information($"Копия файла {report.Name}.frx удалена из каталога");
                }
                else
                {
                    Log.Error("Отчет не был сохранен в базу данных. Копия файла осталась в каталоге приложения.");
                }
            }
            catch (IOException ioEx)
            {
                // Обработка ошибок, связанных с файловой системой
                Log.Error(ioEx, $"Ошибка при удалении файла {report.Name}.frx. Возможно, файл занят другим процессом или отсутствуют права доступа.");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                // Обработка ошибок, связанных с отсутствием прав доступа
                Log.Error(uaEx, $"Ошибка при удалении файла {report.Name}.frx. Недостаточно прав доступа.");
            }
            catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
            {
                Log.Error(ex, "Ошибка при работе с базой данных (PostgreSQL)");
            }

            catch (Exception ex) // Обработка всех остальных исключений
            {
                Log.Error(ex, "Неизвестная ошибка при сохранении отчета в базу данных");
            }
            
        }

        // Команда для кнопки "Изменить"
        [RelayCommand]
        private async void UpdateReport()
        {
            if (SelectedReport == null)
                return;

            var Owner = (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var createReportViewModel = new CreateReportViewModel(_dbService);
            var createReportWindow = new CreateReportView { DataContext = createReportViewModel };
            createReportViewModel.ReportName = SelectedReport.Name;
            createReportViewModel.ReportDescription = SelectedReport.Description;
            createReportWindow.Title = "Изменить отчет";
            var result = await createReportWindow.ShowDialog<bool>(Owner);
            // Если нажата кнопка "Сохранить"
            if (result)
            {
                // Получаем введенное имя отчета
                string reportName = string.IsNullOrEmpty(createReportViewModel.ReportName)
                    ? "Без названия"
                    : createReportViewModel.ReportName;
                // Получаем введенное описание отчета
                string reportDescription = string.IsNullOrEmpty(createReportViewModel.ReportDescription)
                    ? "Без названия" 
                    : createReportViewModel.ReportDescription;
                byte[] reportBytes = SelectedReport.reportData;
                string copyFilePath = Path.Combine(AppContext.BaseDirectory, $"{reportName}.frx");

                try
                {
                    using (MemoryStream stream = new MemoryStream(reportBytes))
                    {
                        Report report = new Report();
                        report.Load(stream);
                        // Отображение отчета
                        report.Design();
                        report.Save(copyFilePath);
                        Log.Information($"Копия файла {reportName}.frx сохранена в каталог");

                        // Сохраняем отчет в новый поток
                        using (MemoryStream newStream = new MemoryStream())
                        {
                            report.Save(newStream);
                            reportBytes = newStream.ToArray();
                        }
                    }
                }

                catch (Exception ex)
                {
                    // Обработка ошибок (например, если файл не найден или нет прав доступа)
                    Log.Error($"Ошибка при создании файла отчета: {ex.Message}");
                    return;
                }

                ARMReport newReport = new();
                newReport.ARMReportID = SelectedReport.ARMReportID;
                newReport.ParentID = 0;
                newReport.UniqueID = 1;
                newReport.Name = reportName;
                newReport.isFolder = false;
                newReport.isDelete = false;
                newReport.Description = reportDescription;
                newReport.reportData = reportBytes;

                try
                {
                    if (await _dbService.UpdateReportAsync(newReport))
                    {
                        Log.Information($"Файл {reportName}.frx изменен в базе данных");
                        File.Delete(copyFilePath);
                        Log.Information($"Копия файла {reportName}.frx удалена из каталога");
                    }
                    else
                    {
                        Log.Error("Отчет не был изменен в базе данных. Копия файла сохранена в каталог приложения.");
                    }
                }
                catch (IOException ioEx)
                {
                    // Обработка ошибок, связанных с файловой системой
                    Log.Error(ioEx, $"Ошибка при удалении файла {reportName}.frx. Возможно, файл занят другим процессом или отсутствуют права доступа.");
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    // Обработка ошибок, связанных с отсутствием прав доступа
                    Log.Error(uaEx, $"Ошибка при удалении файла {reportName}.frx. Недостаточно прав доступа.");
                }
                catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
                {
                    Log.Error(ex, "Ошибка при работе с базой данных (PostgreSQL)");
                }

                catch (Exception ex) // Обработка всех остальных исключений
                {
                    Log.Error(ex, "Неизвестная ошибка при сохранении отчета в базу данных");
                }

            }
        }

        // Команда для кнопки "Удалить"
        [RelayCommand]
        private async void DeleteReport()
        {
            if (SelectedReport == null)
                return;
            // Показываем MessageBox с подтверждением
            var messageBox = MessageBoxManager.GetMessageBoxStandard(
                "Подтверждение", // Заголовок окна
                "Вы уверены, что хотите удалить этот отчет?", // Текст сообщения
                ButtonEnum.YesNo, // Кнопки (Да/Нет)
                Icon.Question // Иконка (вопросительный знак)
            );

            // Отображаем MessageBox и ждем результата
            var result = ButtonResult.No;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                result = await messageBox.ShowWindowDialogAsync(desktop.MainWindow);

            // Если пользователь нажал "Да"
            if (result == ButtonResult.Yes)
            {                
                if (SelectedReport != null)
                {
                    // Удаление отчета из базы данных
                    var f_del = await _dbService.DeleteReportAsync(SelectedReport.ARMReportID);
                    if (f_del)
                    {                    
                        Log.Information($"Отчет {SelectedReport.Name}.frx успешно удален из базы");
                        // Обновление списка отчетов
                        await LoadReportsAsync();
                    }
                    else
                        Log.Information($"Отчет {SelectedReport.Name}.frx не был удален из базы");
                }
            }
        }

        // Команда для кнопки "Создать дубликат"
        [RelayCommand]
        private async void DuplicateReport()
        {
            ARMReport newReport = new();
            newReport.ParentID = 0;
            newReport.UniqueID = 1;
            newReport.Name = SelectedReport.Name;
            newReport.isFolder = false;
            newReport.isDelete = false;
            newReport.Description = SelectedReport.Description;
            newReport.reportData = SelectedReport.reportData;

        // var reportName = DuplicateName(newReport.Name);
            var reportName = newReport.Name;
            string copyFilePath = Path.Combine(AppContext.BaseDirectory, $"{reportName}.frx");

            try
            {
                Report report = new Report();
                report.Save(copyFilePath);
                Log.Information($"Копия файла {reportName}.frx сохранена в каталог");

                if (await _dbService.InsertReportAsync(newReport))
                {
                    Log.Information($"Файл {reportName}.frx сохранен в базу данных");
                    File.Delete(copyFilePath);
                    Log.Information($"Копия файла {reportName}.frx удалена из каталога");
                }
                else
                {
                    Log.Error("Отчет не был сохранен в базу данных. Копия файла сохранена в каталог приложения.");
                }
            }
            catch (IOException ioEx)
            {
                // Обработка ошибок, связанных с файловой системой
                Log.Error(ioEx, $"Ошибка при удалении файла {reportName}.frx. Возможно, файл занят другим процессом или отсутствуют права доступа.");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                // Обработка ошибок, связанных с отсутствием прав доступа
                Log.Error(uaEx, $"Ошибка при удалении файла {reportName}.frx. Недостаточно прав доступа.");
            }
            catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
            {
                Log.Error(ex, "Ошибка при работе с базой данных (PostgreSQL)");
            }

            catch (Exception ex) // Обработка всех остальных исключений
            {
                Log.Error(ex, "Неизвестная ошибка при сохранении отчета в базу данных");
            }
        }

        [RelayCommand]
        private async void RenameReport()
        {
            if (SelectedReport == null)
                return;

            var Owner = (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var createReportViewModel = new CreateReportViewModel(_dbService);
            var createReportWindow = new CreateReportView { DataContext = createReportViewModel };
            createReportViewModel.ReportName = SelectedReport.Name;
            createReportViewModel.ReportDescription = SelectedReport.Description;
            createReportWindow.Title = "Изменить отчет";
            var result = await createReportWindow.ShowDialog<bool>(Owner);
            // Если нажата кнопка "Сохранить"
            if (result)
            {
                // Получаем введенное имя отчета
                string reportName = string.IsNullOrEmpty(createReportViewModel.ReportName)
                    ? "Без названия"
                    : createReportViewModel.ReportName;
                // Получаем введенное описание отчета
                string reportDescription = string.IsNullOrEmpty(createReportViewModel.ReportDescription)
                    ? "Без названия"
                    : createReportViewModel.ReportDescription;
                byte[] reportBytes = SelectedReport.reportData;
                string copyFilePath = Path.Combine(AppContext.BaseDirectory, $"{reportName}.frx");

                try
                {
                    using (MemoryStream stream = new MemoryStream(reportBytes))
                    {
                        Report report = new Report();
                        report.Load(stream);
                        report.Save(copyFilePath);
                        Log.Information($"Копия файла {reportName}.frx сохранена в каталог");

                        // Сохраняем отчет в новый поток
                        using (MemoryStream newStream = new MemoryStream())
                        {
                            report.Save(newStream);
                            reportBytes = newStream.ToArray();
                        }
                    }
                }

                catch (Exception ex)
                {
                    // Обработка ошибок (например, если файл не найден или нет прав доступа)
                    Log.Error($"Ошибка при создании файла отчета: {ex.Message}");
                    return;
                }

                ARMReport newReport = new();
                newReport.ARMReportID = SelectedReport.ARMReportID;
                newReport.ParentID = 0;
                newReport.UniqueID = 1;
                newReport.Name = reportName;
                newReport.isFolder = false;
                newReport.isDelete = false;
                newReport.Description = reportDescription;
                newReport.reportData = reportBytes;

                try
                {
                    if (await _dbService.UpdateReportAsync(newReport))
                    {
                        Log.Information($"Файл {reportName}.frx изменен в базе данных");
                        File.Delete(copyFilePath);
                        Log.Information($"Копия файла {reportName}.frx удалена из каталога");
                    }
                    else
                    {
                        Log.Error("Отчет не был изменен в базе данных. Копия файла сохранена в каталог приложения.");
                    }
                }
                catch (IOException ioEx)
                {
                    // Обработка ошибок, связанных с файловой системой
                    Log.Error(ioEx, $"Ошибка при удалении файла {reportName}.frx. Возможно, файл занят другим процессом или отсутствуют права доступа.");
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    // Обработка ошибок, связанных с отсутствием прав доступа
                    Log.Error(uaEx, $"Ошибка при удалении файла {reportName}.frx. Недостаточно прав доступа.");
                }
                catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
                {
                    Log.Error(ex, "Ошибка при работе с базой данных (PostgreSQL)");
                }

                catch (Exception ex) // Обработка всех остальных исключений
                {
                    Log.Error(ex, "Неизвестная ошибка при сохранении отчета в базу данных");
                }

            }
        }

        [RelayCommand]
        private async void ImportFromXml()
        {
            byte[] reportBytes;
            string reportName = "";
            string copyFilePath = "";
            try
            {
                // Вызываем диалог выбора файла
                string filePath = await OpenFileXMLDialogAsync();

                if (string.IsNullOrEmpty(filePath))
                {
                    Log.Information("Импорт отменен: файл не выбран.");
                    return;
                }

                // Проверяем, что файл .xml существует
                if (!File.Exists(filePath))
                {
                    Log.Error($"Файл не найден: {filePath}");
                    return;
                }

                reportName = Path.GetFileNameWithoutExtension(filePath);
                copyFilePath = Path.Combine(AppContext.BaseDirectory, $"{reportName}.frx");

                using (MemoryStream stream = new MemoryStream())
                {
                    Report report = new Report();
                    report.Load(filePath);
                    report.Save(stream);
                    report.Save(copyFilePath);
                    reportBytes = stream.ToArray();
                    Log.Information($"Импортированный файл сохранен: {copyFilePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при импорте из XML");
                return;
            }

            string reportDescription = reportName;
            ARMReport newReport = new();
            newReport.ParentID = 0;
            newReport.UniqueID = 1;
            newReport.Name = reportName;
            newReport.isFolder = false;
            newReport.isDelete = false;
            newReport.Description = reportDescription;
            newReport.reportData = reportBytes;

            try
            {
                if (await _dbService.InsertReportAsync(newReport))
                {
                    Log.Information($"Файл {reportName}.frx сохранен в базу данных");
                    File.Delete(copyFilePath);
                    Log.Information($"Копия файла {reportName}.frx удалена из каталога");
                }
                else
                {
                    Log.Error("Отчет не был сохранен в базу данных. Копия файла сохранена в каталог приложения.");
                }
            }
            catch (IOException ioEx)
            {
                // Обработка ошибок, связанных с файловой системой
                Log.Error(ioEx, $"Ошибка при удалении файла {reportName}.frx. Возможно, файл занят другим процессом или отсутствуют права доступа.");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                // Обработка ошибок, связанных с отсутствием прав доступа
                Log.Error(uaEx, $"Ошибка при удалении файла {reportName}.frx. Недостаточно прав доступа.");
            }
            catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
            {
                Log.Error(ex, "Ошибка при работе с базой данных (PostgreSQL)");
            }

            catch (Exception ex) // Обработка всех остальных исключений
            {
                Log.Error(ex, "Неизвестная ошибка при сохранении отчета в базу данных");
            }

        }
    
        [RelayCommand]
        private async void ExportToXml()
        {
            if (SelectedReport == null)
                return;

            try
            {
                // Вызываем диалог выбора файла
                string filePath = await SaveFileToXMLDialogAsync();

                if (string.IsNullOrEmpty(filePath))
                {
                    Log.Information("Экспорт отменен: файл не выбран.");
                    return;
                }
                using (MemoryStream stream = new MemoryStream(SelectedReport.reportData))
                {
                    Report report = new Report();
                    report.Load(stream);
                    report.Save(filePath);
                    Log.Information($"Экспорт в XML выполнен успешно: {filePath}");
                }                                
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при экспорте в XML");
            }
        }

        [RelayCommand]
        private async void ImportFromFRX()        
        {
            byte[] reportBytes;
            string reportName = "";
            string copyFilePath = "";
            try
            {
                // Вызываем диалог выбора файла
                string filePath = await OpenFileFRXDialogAsync();

                if (string.IsNullOrEmpty(filePath))
                {
                    Log.Information("Импорт отменен: файл не выбран.");
                    return;
                }

                // Проверяем, что файл .fxr существует
                if (!File.Exists(filePath))
                {
                    Log.Error($"Файл не найден: {filePath}");
                    return;
                }

                reportName = Path.GetFileNameWithoutExtension(filePath);
                copyFilePath = Path.Combine(AppContext.BaseDirectory, $"{reportName}.frx");

                using (MemoryStream stream = new MemoryStream())
                {
                    Report report = new Report();
                    report.Load(filePath);
                    using (MemoryStream streamForSave = new MemoryStream())
                    {
                        report.Save(streamForSave);
                        reportBytes = streamForSave.ToArray();
                    }
                    report.Save(copyFilePath);                    
                    Log.Information($"Импортированный файл сохранен: {copyFilePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при импорте из FRX");
                return;
            }

            string reportDescription = reportName;
            ARMReport newReport = new();
            newReport.ParentID = 0;
            newReport.UniqueID = 1;
            newReport.Name = reportName;
            newReport.isFolder = false;
            newReport.isDelete = false;
            newReport.Description = reportDescription;
            newReport.reportData = reportBytes;

            try
            {
                if (await _dbService.InsertReportAsync(newReport))
                {
                    Log.Information($"Файл {reportName}.frx сохранен в базу данных");
                    File.Delete(copyFilePath);
                    Log.Information($"Копия файла {reportName}.frx удалена из каталога");
                }
                else
                {
                    Log.Error("Отчет не был сохранен в базу данных. Копия файла сохранена в каталог приложения.");
                }
            }
            catch (IOException ioEx)
            {
                // Обработка ошибок, связанных с файловой системой
                Log.Error(ioEx, $"Ошибка при удалении файла {reportName}.frx. Возможно, файл занят другим процессом или отсутствуют права доступа.");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                // Обработка ошибок, связанных с отсутствием прав доступа
                Log.Error(uaEx, $"Ошибка при удалении файла {reportName}.frx. Недостаточно прав доступа.");
            }
            catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
            {
                Log.Error(ex, "Ошибка при работе с базой данных (PostgreSQL)");
            }

            catch (Exception ex) // Обработка всех остальных исключений
            {
                Log.Error(ex, "Неизвестная ошибка при сохранении отчета в базу данных");
            }

        }

        [RelayCommand]
        private async void UploadAllFRXToDB()
        {
            try
            {
                // Получаем текущий каталог приложения
                string currentDirectory = AppContext.BaseDirectory;

                // Ищем все файлы с расширением .frx в текущем каталоге
                string[] frxFiles = Directory.GetFiles(currentDirectory, "*.frx");

                // Очищаем коллекцию FRXReports перед заполнением
                FRXReports.Clear();

                // Проходим по всем найденным файлам
                foreach (string filePath in frxFiles)
                {
                    try
                    {
                        // Читаем содержимое файла в массив байтов
                        byte[] reportData = await File.ReadAllBytesAsync(filePath);

                        // Создаем объект ARMReport
                        ARMReport report = new ARMReport
                        {
                            Name = Path.GetFileNameWithoutExtension(filePath),
                            Description = "Импортированный из файла",
                            ParentID = 0,
                            UniqueID = 1,
                            isFolder = false,
                            isDelete = false,
                            reportData = reportData
                        };

                        CreateReport(report);

                        Log.Information($"Файл {Path.GetFileName(filePath)} успешно добавлен в коллекцию FRXReports.");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Ошибка при обработке файла {Path.GetFileName(filePath)}.");
                    }
                }

                Log.Information($"Все файлы .frx из каталога {currentDirectory} успешно обработаны.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при поиске и обработке файлов .frx.");
            }
        }

        private async void HandleReportChange(string payload)
        {
            // Десериализация JSON-уведомления            
            if (payload != "")
            {
                Log.Information($"{payload}");
                // Загружаем полные данные отчета из базы данных
                await LoadReportsAsync();
            }
        }

        public async Task StartListeningForReportsChangeAsync()
        {
            try
            {
                await _dbService.ListenForReportsChangeAsync(HandleReportChange);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while starting to listen for report changes.");
            }
        }

        public static async Task<string> OpenFileXMLDialogAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select File",
                AllowMultiple = false, // Позволяет выбирать только один файл            
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter
                    {
                        Name = "XML Files", // Название фильтра
                        Extensions = new List<string> { "xml" } // Расширения файлов
                    }
                }
            };

            var window = Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            var result = await dialog.ShowAsync(window);
            return result?.FirstOrDefault(); // Возвращает путь к первому выбранному файлу или null, если
        }

        public static async Task<string>SaveFileToXMLDialogAsync()
        {

                // Создаем диалог сохранения файла
                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Сохранить отчет как XML",
                    Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter
                    {
                        Name = "XML Files", // Название фильтра
                        Extensions = new List<string> { "xml" } // Расширения файлов
                    }
                },
                    InitialFileName = "report.xml" // Предлагаемое имя файла по умолчанию
                };

                // Получаем главное окно приложения
                var window = Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                // Открываем диалог сохранения файла
                string outputFilePath = await saveFileDialog.ShowAsync(window);
                return outputFilePath;
        }

        public static async Task<string> OpenFileFRXDialogAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select File",
                AllowMultiple = false, // Позволяет выбирать только один файл            
                Filters = new List<FileDialogFilter>
                    {
                        new FileDialogFilter
                        {
                            Name = "FRX Files", // Название фильтра
                            Extensions = new List<string> { "frx" } // Расширения файлов
                        }
                    }
            };

            var window = Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            var result = await dialog.ShowAsync(window);
            return result?.FirstOrDefault(); // Возвращает путь к первому выбранному файлу или null, если
        }

}
