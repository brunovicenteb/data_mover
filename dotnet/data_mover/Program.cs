using data_mover.ColumnProcessors;
using Npgsql;
using Npgsql.Schema;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace data_mover;

static class Program
{
    private const int BATCH_MAX_SIZE = 1_000;

    public static int Main(string[] args)
    {
        var config = ReadConfig(args);
        if (config is null)
        {
            PrintUsage();
            return 1;
        }

        var sourceDbConfig = DbConfig.ReadConfigFrom(config, "source");
        var destinationDbConfig = DbConfig.ReadConfigFrom(config, "destination");

        // get list of tables and columns to process
        var tablesToProcess = ReadTables(config);
        var columnsToProcess = ReadColumns(config);

        // truncate target tables
        TruncateDestinationDatabaseTables(tablesToProcess, destinationDbConfig);

        // process the tables.
        ProcessTables(tablesToProcess, columnsToProcess, sourceDbConfig, destinationDbConfig);

        return 0;
    }

    private static void ProcessTables(IReadOnlyList<TableConfiguration> tablesToProcess, IReadOnlyDictionary<DatabaseColumn, IColumnProcessor> columnsToProcess, DbConfig sourceDbConfig, DbConfig destinationDbConfig)
    {
        var totalStopWatch = Stopwatch.StartNew();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount - 1
        };

        var task = Task.Run(async () =>
         {
             await Parallel.ForEachAsync(tablesToProcess, parallelOptions, async (table, cancellationToken) =>
             {
                 var columns = columnsToProcess
                     .Where(c => c.Key.Table == table.Table)
                     .ToFrozenDictionary();

                 await ProcessTableAsync(table, columns, sourceDbConfig, destinationDbConfig);
             });
         });
        task.Wait();

        Console.WriteLine("Total time processing tables: " + totalStopWatch.Elapsed.TotalSeconds + "s");
    }

    private static async Task ProcessTableAsync(TableConfiguration table, IReadOnlyDictionary<DatabaseColumn, IColumnProcessor> columnsToProcess, DbConfig sourceDbConfig, DbConfig destinationDbConfig)
    {
        using var sourceConnection = sourceDbConfig.Connection();
        await sourceConnection.OpenAsync();
        using var destinationConnection = destinationDbConfig.Connection();
        await destinationConnection.OpenAsync();

        var query = table.Limit is null
            ? "SELECT * FROM " + table.Table
            : "SELECT * FROM " + table.Table + " LIMIT " + table.Limit;
        using var readCommand = sourceConnection.CreateCommand();
        readCommand.CommandText = query;
        using var reader = await readCommand.ExecuteReaderAsync();
        var tablesColumns = reader.GetColumnSchema().ToImmutableArray();

        Console.WriteLine($"Starting table {table.Table}.");
        long count = 0;
        var sinceLastReport = Stopwatch.StartNew();
        var totalTime = Stopwatch.StartNew();
        var batch = new List<Dictionary<string, object>>();
        while (await reader.ReadAsync())
        {
            var row = ReadRow(reader, tablesColumns);
            ProcessRow(row, columnsToProcess);
            await WriteRowAsync(row, table.Table, destinationConnection, batch);
            count += 1;
            if (sinceLastReport.Elapsed.TotalSeconds > 10)
            {
                Console.WriteLine($"Processing {table.Table} for {totalTime.Elapsed.TotalSeconds}s at row number {count}.");
                sinceLastReport.Restart();
            }
        }
        await FlushAsync(table.Table, destinationConnection, batch);
        Console.WriteLine($"Table {table} processed in: " + totalTime.Elapsed.TotalSeconds + "s");
    }

    private static Dictionary<string, object> ReadRow(NpgsqlDataReader reader, IReadOnlyList<NpgsqlDbColumn> columns)
    {
        var retval = new Dictionary<string, object>(columns.Count);
        var values = new object[columns.Count];
        reader.GetValues(values);
        foreach (var column in columns)
            retval[column.ColumnName] = values[column.ColumnOrdinal!.Value];
        return retval;
    }

    private static void ProcessRow(Dictionary<string, object> row, IReadOnlyDictionary<DatabaseColumn, IColumnProcessor> columnsToProcess)
    {
        foreach (var column in columnsToProcess)
        {
            var columnName = column.Key.Column;
            row[columnName] = column.Value.ProcessValue(row[columnName]);
        }
    }

    private static async Task WriteRowAsync(Dictionary<string, object> row, DatabaseTable table, NpgsqlConnection destinationConnection, List<Dictionary<string, object>> batch)
    {
        batch.Add(row);
        if (batch.Count >= BATCH_MAX_SIZE)
            await FlushAsync(table, destinationConnection, batch);
    }

    private static async Task FlushAsync(DatabaseTable table, NpgsqlConnection destinationConnection, List<Dictionary<string, object>> batch)
    {
        if (batch.Count == 0)
            return;

        int paramIndex = -1;
        var commandText = new StringBuilder();
        var parameters = new List<NpgsqlParameter>();

        foreach (var row in batch)
        {
            var parCount = -1;
            var pars = new string[row.Count];
            foreach (var dataRow in row)
            {
                parCount++;
                paramIndex++;
                pars[parCount] = $"@p{paramIndex}";
                parameters.Add(new NpgsqlParameter($"@p{paramIndex}", dataRow.Value));
            }
            var rowPars = string.Join(",", pars);
            commandText.AppendLine($"INSERT INTO {table} VALUES ({rowPars});");
        }

        using var command = destinationConnection.CreateCommand();
        command.CommandText = commandText.ToString();
        command.Parameters.AddRange(parameters.ToArray());
        command.Prepare();

        var result = await command.ExecuteNonQueryAsync();
        if (result != batch.Count)
            throw new InvalidOperationException("Wrong rows ammount inserted!");

        batch.Clear();
    }

    private static void TruncateDestinationDatabaseTables(IReadOnlyList<TableConfiguration> tablesToProcess, DbConfig destinationDbConfig)
    {
        foreach (var table in tablesToProcess)
        {
            TruncateTable(table.Table, destinationDbConfig);
        }
    }

    private static void TruncateTable(DatabaseTable table, DbConfig destinationDbConfig)
    {
        using var connection = destinationDbConfig.Connection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "TRUNCATE TABLE " + table.ToString();
        command.ExecuteNonQuery();
    }


    private static TomlTable? ReadConfig(string[] args)
    {
        try
        {
            var path = args[0];
            if (!File.Exists(path))
                return null;

            var fileContent = File.ReadAllText(path);
            return Toml.ToModel(fileContent, path);
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }

    private static IReadOnlyList<TableConfiguration> ReadTables(TomlTable config)
    {
        var retval = new List<TableConfiguration>();

        var tableArray = config["table"] as TomlTableArray;
        if (tableArray is null)
        {
            throw new ArgumentException("Array of 'table's is missing from config");
        }

        foreach (var table in tableArray)
        {
            var schemaName = table["schemaName"] as string;
            var tableName = table["tableName"] as string;
            var limit = table.ContainsKey("limit") ? table["limit"] as long? : null;
            if (schemaName is null || tableName is null)
            {
                throw new ArgumentException("Table must contain a 'schemaName' and 'tableName' property");
            }

            retval.Add(new TableConfiguration(new DatabaseTable(schemaName, tableName), limit));
        }

        return retval.ToImmutableArray();
    }

    private static IReadOnlyDictionary<DatabaseColumn, IColumnProcessor> ReadColumns(TomlTable config)
    {
        var retval = new Dictionary<DatabaseColumn, IColumnProcessor>();

        if (!config.ContainsKey("column"))
        {
            // no columns, then return empty config
            return retval;
        }

        var columnArray = config["column"] as TomlTableArray;
        if (columnArray is null)
        {
            throw new ArgumentException("Array of 'table's is missing from config");
        }

        foreach (var column in columnArray)
        {
            var schemaName = column["schemaName"] as string;
            var tableName = column["tableName"] as string;
            var columnName = column["columnName"] as string;
            var processor = column["processor"] as string;
            if (schemaName is null || tableName is null || columnName is null || processor is null)
            {
                throw new ArgumentException("Column must contain a 'schemaName', 'tableName','columnName' and 'processor', property");
            }

            var table = new DatabaseTable(schemaName, tableName);
            var columnProcessor = ColumnProcessors.ColumnProcessors.Create(processor);
            retval.Add(new DatabaseColumn(table, columnName), columnProcessor);
        }

        return retval.ToFrozenDictionary();
    }


    private static void PrintUsage()
    {
        Console.Error.WriteLine("Need to supply a TOML file configuration.");
        Console.Error.WriteLine("dotnet data_mover.dll <configuration.toml>");
    }
}