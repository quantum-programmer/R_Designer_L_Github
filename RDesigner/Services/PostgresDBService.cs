using System;
using Npgsql;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using RDesigner.Models;
using System.Runtime.InteropServices;
using Serilog;
using Avalonia;
using FastReport;
using Microsoft.Extensions.DependencyInjection;
using RDesigner.Views;
using Tmds.DBus.Protocol;

namespace RDesigner.Services 
{
    public class PostgresDBService : IDBService
    {

        private static string? connectionString;
        private static string? _connectionString;
        private NpgsqlDataSource dataSource;                          

        public bool CheckConnection()
        {
            throw new NotImplementedException();
        }

        public PostgresDBService()
        {
            InitializeDataSource();
        }

        private void InitializeDataSource()
        {
            var app = Application.Current as App;            

            if (ConfigurationManager.AppSettings[
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "WindowsDBPort" : "LinuxDBPort"] is string portStr
                && int.TryParse(portStr, out var port)
                && ConfigurationManager.AppSettings[
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "WindowsDBHost" : "LinuxDBHost"] is string host
                && ConfigurationManager.AppSettings[
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "WindowsDBase" : "LinuxDBase"] is string dBase
                && ConfigurationManager.AppSettings[
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "WindowsPass" : "LinuxPass"] is string password
                && ConfigurationManager.AppSettings[
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "WindowsLogin" : "LinuxLogin"] is string login)
            {
                NpgsqlConnectionStringBuilder builder =
                    new(connectionString)
                    {
                        Host = host,
                        Database = dBase,
                        Port = port,
                        Username = login,
                        Password = password
                    };
                dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
                _connectionString = builder.ConnectionString;
                dataSource = NpgsqlDataSource.Create(_connectionString);                
            }
        }

        public string GetConnectionString()
        {
            return _connectionString ?? throw new InvalidOperationException("Data source is not initialized.");
        }

        public async Task<ARMReport?> GetReportById(int id)
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(@"SELECT 
                    ""REPORT_ID"",       -- 0 int
                    ""PARENT_ID"",       -- 1 int
                    ""UNIQUE_ID"",       -- 2 int
                    ""NAME"",            -- 3 string
                    ""IS_FOLDER"",       -- 4 bool
                    ""IS_DELETE"",       -- 5 bool
                    ""MODIFY_DATE"",     -- 6 timestamp (DateTime)
                    ""DESCRIPTION"",     -- 7 string (nullable)
                    ""BINARY_BODY""      -- 8 byte[] (nullable)
                FROM public.""Rep_REPORTS"" 
                WHERE ""REPORT_ID"" = @id", connection);

            command.Parameters.AddWithValue("id", id);

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ARMReport
                {
                    ARMReportID = reader.GetInt32(0),
                    ParentID = reader.GetInt32(1),
                    UniqueID = reader.GetInt32(2),
                    Name = reader.IsDBNull(3) ? "Unnamed Report" : reader.GetString(3),
                    isFolder = reader.GetBoolean(4),
                    isDelete = reader.GetBoolean(5),
                    ModifyDate = reader.IsDBNull(6) ?
                        string.Empty :
                        reader.GetDateTime(6).ToString("yyyy-MM-dd HH:mm:ss"),
                    Description = reader.IsDBNull(7) ? null : reader.GetString(7),
                    reportData = reader.IsDBNull(8) ? null : (byte[])reader[8],                    
                };
            }
            return null;
        }

        public async Task<List<ARMReport>> GetAllReportsAsync()
        {            
            var reports = new List<ARMReport>();            
            try 
            {
                await using var connection = await dataSource.OpenConnectionAsync();
                await using var command = new NpgsqlCommand(@" SELECT * FROM public.""Rep_REPORTS"" 
                                                               where ""UNIQUE_ID"" is not null 
                                                             ", connection);
                {
                    await using var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        var report = new ARMReport
                        {
                            ARMReportID = reader.GetInt32(reader.GetOrdinal("REPORT_ID")),
                            UniqueID = reader.GetInt32(reader.GetOrdinal("UNIQUE_ID")),
                            Name = reader.IsDBNull(reader.GetOrdinal("NAME")) ? null : reader.GetString(reader.GetOrdinal("NAME")),
                            Description = reader.IsDBNull(reader.GetOrdinal("DESCRIPTION")) ? null : reader.GetString(reader.GetOrdinal("DESCRIPTION")),
                            ModifyDate = reader.IsDBNull(reader.GetOrdinal("MODIFY_DATE"))
                                        ? null
                                        : reader.GetDateTime(reader.GetOrdinal("MODIFY_DATE")).ToString("yyyy-MM-dd HH:mm:ss"),
                            CodeAssociatePO = reader.GetInt32(reader.GetOrdinal("UNIQUE_ID")),
                            ParentID = reader.GetInt32(reader.GetOrdinal("PARENT_ID")),
                            reportData = (byte[])reader["BINARY_BODY"]
                        };
                        reports.Add(report);
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while fetching reports from the database.");
                throw;
            }

            return reports;
        }

        public async Task<Boolean> InsertReportAsync(ARMReport r)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync();
                string sql = "INSERT INTO \"Rep_REPORTS\" (\"PARENT_ID\", \"UNIQUE_ID\", \"NAME\", \"IS_FOLDER\", \"IS_DELETE\", \"MODIFY_DATE\", \"DESCRIPTION\", \"BINARY_BODY\") " +
                             "VALUES (@parent_id, @unique_id, @name, @is_folder, @is_delete, @modify_date, @description, @binary_body)";

                await using var command = new NpgsqlCommand(sql, connection);
                // Добавление параметров
                command.Parameters.AddWithValue("parent_id", r.ParentID);
                command.Parameters.AddWithValue("unique_id", r.UniqueID);
                command.Parameters.AddWithValue("name", r.Name);
                command.Parameters.AddWithValue("is_folder", r.isFolder);
                command.Parameters.AddWithValue("is_delete", r.isDelete);
                command.Parameters.AddWithValue("modify_date", DateTime.Now);
                command.Parameters.AddWithValue("description", r.Description);
                command.Parameters.AddWithValue("binary_body", NpgsqlTypes.NpgsqlDbType.Bytea, r.reportData); 

                // Выполнение запроса                
                int rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while fetching report into the database.");
                throw;
            }
        }

        public async Task<Boolean> UpdateReportAsync(ARMReport r)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync();
                string sql = "UPDATE \"Rep_REPORTS\" SET " +
                              "\"PARENT_ID\" = @parent_id, " +
                              "\"UNIQUE_ID\" = @unique_id, " +
                              "\"NAME\" = @name, " +
                              "\"IS_FOLDER\" = @is_folder, " +
                              "\"IS_DELETE\" = @is_delete, " +
                              "\"MODIFY_DATE\" = @modify_date, " +
                              "\"DESCRIPTION\" = @description, " +
                              "\"BINARY_BODY\" = @binary_body " +
                              "WHERE \"REPORT_ID\" = @report_id";

                await using var command = new NpgsqlCommand(sql, connection);
                // Добавление параметров
                command.Parameters.AddWithValue("parent_id", r.ParentID);
                command.Parameters.AddWithValue("unique_id", r.UniqueID); 
                command.Parameters.AddWithValue("name", r.Name);
                command.Parameters.AddWithValue("is_folder", r.isFolder);
                command.Parameters.AddWithValue("is_delete", r.isDelete);
                command.Parameters.AddWithValue("modify_date", DateTime.Now);
                command.Parameters.AddWithValue("description", r.Description);
                command.Parameters.AddWithValue("binary_body", NpgsqlTypes.NpgsqlDbType.Bytea, r.reportData);
                command.Parameters.AddWithValue("report_id", r.ARMReportID);

                // Выполнение запроса                
                int rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while updating report in the database.");
                throw;
            }
        }        

        public async Task<bool> DeleteReportAsync(int reportId)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync();
                string sql = "DELETE FROM \"Rep_REPORTS\" WHERE \"REPORT_ID\" = @reportId";
                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("reportId", reportId);

                int rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while deleting report from the database.");
                throw;
            }
        }

        public async Task ListenForReportsChangeAsync(Action<string> notificationHandler)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync();
                await using (var command = new NpgsqlCommand("LISTEN report_change;", connection))
                {
                    await command.ExecuteNonQueryAsync();
                }

                connection.Notification += (o, e) => notificationHandler(e.Payload);

                while (true)
                {
                    await connection.WaitAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while listening for notifications.");
                throw;
            }
        }

    }

}
