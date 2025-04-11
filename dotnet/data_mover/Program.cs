using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using data_mover.ColumnProcessors;
using Npgsql;
using Npgsql.Schema;
using Tomlyn;
using Tomlyn.Model;

namespace data_mover;

static class Program
{
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
        foreach (var table in tablesToProcess)
        {
            var columns = columnsToProcess.Where(c => c.Key.Table == table.Table).ToFrozenDictionary();
            ProcessTable(table, columns, sourceDbConfig, destinationDbConfig);
        }
        Console.WriteLine("Total time processing tables: " + totalStopWatch.Elapsed.TotalSeconds + "s");
    }

    private static void ProcessTable(TableConfiguration table, IReadOnlyDictionary<DatabaseColumn, IColumnProcessor> columnsToProcess, DbConfig sourceDbConfig, DbConfig destinationDbConfig)
    {
        using var sourceConnection = sourceDbConfig.Connection();
        sourceConnection.Open();
        using var destinationConnection = destinationDbConfig.Connection();
        destinationConnection.Open();

        var query = table.Limit is null
            ? "SELECT * FROM " + table.Table
            : "SELECT * FROM " + table.Table + " LIMIT " + table.Limit;
        using var readCommand = sourceConnection.CreateCommand();
        readCommand.CommandText = query;
        using var reader = readCommand.ExecuteReader();
        var tablesColumns = reader.GetColumnSchema().ToImmutableArray();

        Console.WriteLine($"Starting table {table.Table}.");
        long count = 0;
        var sinceLastReport = Stopwatch.StartNew();
        var totalTime = Stopwatch.StartNew();
        while (reader.Read())
        {
            var row = ReadRow(reader, tablesColumns); // Task 1
            ProcessRow(row, columnsToProcess); // Task 2
            WriteRow(row, table.Table, tablesColumns, destinationConnection); // Task 3 this one should be reimplemented to use a batch of rows instead a single row
            // acolombi (acolombi@gmail.com)

            count += 1;
            if (sinceLastReport.Elapsed.TotalSeconds > 10)
            {
                Console.WriteLine($"Processing {table.Table} for {totalTime.Elapsed.TotalSeconds}s at row number {count}.");
                sinceLastReport.Restart();
            }
        }
        Console.WriteLine($"Table {table} processed in: " + totalTime.Elapsed.TotalSeconds + "s");
    }

    private static Dictionary<string, object> ReadRow(NpgsqlDataReader reader, IReadOnlyList<NpgsqlDbColumn> columns)
    {
        var retval = new Dictionary<string, object>(columns.Count);
        foreach (var column in columns)
        {
            int ordinal = (int) column.ColumnOrdinal!;
            retval[column.ColumnName] = reader.GetValue(ordinal);
        }

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

    private static void WriteRow(Dictionary<string, object> row, DatabaseTable table, IReadOnlyList<NpgsqlDbColumn> columns, NpgsqlConnection destinationConnection)
    {
        using var command = destinationConnection.CreateCommand();
        var parameterNames = string.Join(", ", Enumerable.Range(1, row.Count).Select(x => $"${x}"));
        command.CommandText = "INSERT INTO " + table + $" VALUES ({parameterNames})";

        foreach (var column in columns)
        {
            var parameter = command.CreateParameter();
            parameter.Value = row[column.ColumnName];
            command.Parameters.Add(parameter);
        }
        command.Prepare();

        var result = command.ExecuteNonQuery();
        if (result != 1)
        {
            throw new InvalidOperationException("No rows inserted!");
        }
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