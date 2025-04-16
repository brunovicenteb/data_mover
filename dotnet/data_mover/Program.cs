using data_mover.ColumnProcessors;
using Npgsql;
using Npgsql.Schema;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Tomlyn;
using Tomlyn.Model;

namespace data_mover;

public record DataBatch(DatabaseTable Table, IReadOnlyList<Dictionary<string, object>> Rows);

static class Program
{
    private const int BatchSize = 1000;
    private const int MaxDegreeOfParallelism = 4;

    public static async Task<int> Main(string[] args)
    {
        var config = ReadConfig(args);
        if (config is null)
        {
            PrintUsage();
            return 1;
        }

        var sourceDbConfig = DbConfig.ReadConfigFrom(config, "source");
        var destinationDbConfig = DbConfig.ReadConfigFrom(config, "destination");

        var tablesToProcess = ReadTables(config);
        var columnsToProcess = ReadColumns(config);

        await TruncateDestinationDatabaseTablesAsync(tablesToProcess, destinationDbConfig);

        await ProcessTablesAsync(sourceDbConfig, destinationDbConfig, tablesToProcess, columnsToProcess);

        return 0;
    }

    private static async Task ProcessTablesAsync(DbConfig sourceDbConfig, DbConfig destinationDbConfig,
        IReadOnlyList<TableConfiguration> tablesToProcess, IReadOnlyDictionary<DatabaseColumn, IColumnProcessor> columnsToProcess)
    {
        var totalStopWatch = Stopwatch.StartNew();

        var processingTasks = tablesToProcess.Select(table =>
        {
            var tableColumns = columnsToProcess
                .Where(c => c.Key.Table == table.Table)
                .ToFrozenDictionary();

            return ProcessTableAsync(table, sourceDbConfig, destinationDbConfig, tableColumns);
        });

        await Task.WhenAll(processingTasks);

        Console.WriteLine("Total time processing tables: " + totalStopWatch.Elapsed.TotalSeconds + "s");
    }

    private static async Task ProcessTableAsync(TableConfiguration tableConfig, DbConfig sourceDbConfig, DbConfig destinationDbConfig,
        IReadOnlyDictionary<DatabaseColumn, IColumnProcessor> columnsToProcess)
    {
        Console.WriteLine($"Starting processing table {tableConfig.Table}");

        // Channel for raw data (reading -> processing)
        var rawDataChannel = CreateChannel<Dictionary<string, object>>(100, true);

        // Channel for processed data (processing -> writing)
        var processedDataChannel = CreateChannel<DataBatch>(10, false);

        // Start pipeline
        var readTask = ReadFromSourceAsync(tableConfig, sourceDbConfig, rawDataChannel.Writer);

        var processTasks = Enumerable.Range(0, MaxDegreeOfParallelism)
            .Select(_ => ProcessDataAsync(tableConfig.Table, rawDataChannel.Reader, processedDataChannel.Writer, columnsToProcess))
            .ToArray();

        var writeTask = WriteToDestinationAsync(tableConfig.Table, destinationDbConfig, processedDataChannel.Reader);

        // Wait for steps to complete
        await readTask; // Ensures reading completion
        /* Console.Write("TODA A LEITURA COMPLETA."); */

        await Task.WhenAll(processTasks); // Ensures completion of processing
        processedDataChannel.Writer.Complete(); // // Sending remaining batches
        /* Console.Write("TODA O PROCESSAMENTO COMPLETO."); */

        await writeTask; // Ensures writing completion
        /* Console.Write("TODA A ESCRITA COMPLETA."); */

        Console.WriteLine($"Finished processing table {tableConfig.Table}");
    }

    private static async Task ReadFromSourceAsync(TableConfiguration tableConfig, DbConfig dbConfig, ChannelWriter<Dictionary<string, object>> output)
    {
        await using var connection = dbConfig.Connection();
        await connection.OpenAsync();

        var query = tableConfig.Limit is null
            ? $"SELECT * FROM {tableConfig.Table}"
            : $"SELECT * FROM {tableConfig.Table} LIMIT {tableConfig.Limit}";

        await using var command = new NpgsqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var columns = (await reader.GetColumnSchemaAsync()).ToImmutableArray();
        var count = 0L;
        var timer = Stopwatch.StartNew();
        var lastReport = Stopwatch.StartNew();

        try
        {
            while (await reader.ReadAsync())
            {
                var row = ReadRow(reader, columns);
                await output.WriteAsync(row);

                count++;
                if (lastReport.Elapsed.TotalSeconds > 10)
                {
                    Console.WriteLine($"Read {count} rows from {tableConfig.Table} in {timer.Elapsed.TotalSeconds}s");
                    lastReport.Restart();
                }
            }
        }
        finally
        {
            output.Complete();
            Console.WriteLine($"Finished reading {count} rows from {tableConfig.Table} in {timer.Elapsed.TotalSeconds}s");
        }
    }

    private static Dictionary<string, object> ReadRow(NpgsqlDataReader reader, IReadOnlyList<NpgsqlDbColumn> columns)
    {
        var row = new Dictionary<string, object>(columns.Count);
        foreach (var column in columns)
        {
            int ordinal = (int)column.ColumnOrdinal!;
            row[column.ColumnName] = reader.GetValue(ordinal);
        }
        return row;
    }

    private static async Task ProcessDataAsync(DatabaseTable table, ChannelReader<Dictionary<string, object>> input, ChannelWriter<DataBatch> output,
        IReadOnlyDictionary<DatabaseColumn, IColumnProcessor> columnsToProcess)
    {
        var batch = new List<Dictionary<string, object>>(BatchSize);

        await foreach (var row in input.ReadAllAsync())
        {
            ProcessRow(row, columnsToProcess);
            batch.Add(row);

            if (batch.Count >= BatchSize)
            {
                await output.WriteAsync(new DataBatch(table, batch.ToArray()));
                batch.Clear();
            }
        }

        // Sent any remains lines.
        if (batch.Count > 0)
            await output.WriteAsync(new DataBatch(table, batch.ToArray()));
    }

    private static void ProcessRow(Dictionary<string, object> row, IReadOnlyDictionary<DatabaseColumn, IColumnProcessor> columnsToProcess)
    {
        foreach (var column in columnsToProcess)
        {
            var columnName = column.Key.Column;
            row[columnName] = column.Value.ProcessValue(row[columnName]);
        }
    }

    private static async Task WriteToDestinationAsync(DatabaseTable table, DbConfig dbConfig, ChannelReader<DataBatch> input)
    {
        var taskList = new List<Task>();

        await foreach (var batch in input.ReadAllAsync())
        {
            var task = WriteBatchAsync(batch, table, dbConfig);
            taskList.Add(task);
        }

        await Task.WhenAll(taskList);
    }

    private static async Task WriteBatchAsync(DataBatch batch, DatabaseTable table, DbConfig dbConfig)
    {
        if (batch.Rows.Count == 0)
            return;

        int paramIndex = -1;
        var commandText = new StringBuilder();
        var parameters = new List<NpgsqlParameter>();

        foreach (var row in batch.Rows)
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

        await using var connection = dbConfig.Connection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = commandText.ToString();
        command.Parameters.AddRange(parameters.ToArray());
        command.Prepare();

        var result = await command.ExecuteNonQueryAsync();
        if (result != batch.Rows.Count)
            throw new InvalidOperationException("Wrong rows ammount inserted!");

        //Console.WriteLine($"Writed {batch.Rows.Count} rows from {table.Table}");
    }

    private static async Task TruncateDestinationDatabaseTablesAsync(
        IReadOnlyList<TableConfiguration> tablesToProcess,
        DbConfig destinationDbConfig)
    {
        var truncateTasks = tablesToProcess.Select(table =>
            TruncateTableAsync(table.Table, destinationDbConfig));

        await Task.WhenAll(truncateTasks);
    }

    private static async Task TruncateTableAsync(DatabaseTable table, DbConfig destinationDbConfig)
    {
        await using var connection = destinationDbConfig.Connection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"TRUNCATE TABLE {table}";
        await command.ExecuteNonQueryAsync();
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

    private static ImmutableArray<TableConfiguration> ReadTables(TomlTable config)
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
            return retval;
        }

        var columnArray = config["column"] as TomlTableArray;
        if (columnArray is null)
        {
            throw new ArgumentException("Array of 'column's is missing from config");
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

    private static Channel<T> CreateChannel<T>(int capacity, bool singleWriter)
    {
        return Channel.CreateBounded<T>(
            new BoundedChannelOptions(capacity)
            {
                SingleWriter = singleWriter,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.Wait
            });
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Need to supply a TOML file configuration.");
        Console.Error.WriteLine("dotnet data_mover.dll <configuration.toml>");
    }
}