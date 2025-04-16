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
    private const int WRITE_BATCH_SIZE = 1000;
    private const int READ_PAGE_SIZE = 10000;
    private const int MAX_DEGREE_OF_PARALLELISM = 4;
    private const int MAX_READER_TASKS = 4;
    private const string DEFAULT_PK_NAME = "_label_key";

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
        var rawDataChannel = CreateChannel<Dictionary<string, object>>(1000, true);

        // Channel for processed data (processing -> writing)
        var processedDataChannel = CreateChannel<DataBatch>(10, false);

        // Start pipeline
        var readTask = ReadFromSourceAsync(tableConfig, sourceDbConfig, rawDataChannel.Writer);

        var processTasks = Enumerable.Range(0, MAX_DEGREE_OF_PARALLELISM)
            .Select(_ => ProcessDataAsync(tableConfig.Table, rawDataChannel.Reader, processedDataChannel.Writer, columnsToProcess))
            .ToArray();

        var writeTask = WriteToDestinationAsync(tableConfig.Table, destinationDbConfig, processedDataChannel.Reader);

        await readTask; // Ensures reading completion

        await Task.WhenAll(processTasks); // Ensures completion of processing
        processedDataChannel.Writer.Complete();

        await writeTask; // Ensures writing completion

        Console.WriteLine($"Finished processing table {tableConfig.Table}");
    }

    private static async Task ReadFromSourceAsync(TableConfiguration tableConfig, DbConfig dbConfig, ChannelWriter<Dictionary<string, object>> output)
    {
        var primaryKey = await GetPrimaryKeyColumnAsync(tableConfig, dbConfig);
        if (primaryKey == null)
        {
            Console.WriteLine($"Warning: No primary key found for table {tableConfig.Table}, falling back to offset pagination");
            return;
        }

        var (minId, maxId) = await GetMinMaxIdsAsync(tableConfig, dbConfig, primaryKey);

        // If the table is empty
        if (minId == null || maxId == null)
        {
            output.Complete();
            return;
        }

        // Split the range of IDs between the tasks
        // I made some tests and if we increase a lot the MAX_READER_TASKS const with a small Postgres, the DB don't suport too many threads.
        var ranges = SplitIdRange(minId, maxId, MAX_READER_TASKS); 

        Console.WriteLine($"Processing {tableConfig.Table} using keyset pagination on column {primaryKey}");

        var readTasks = ranges.Select(range => Task.Run(() => ReadIdRangeAsync(
                tableConfig, dbConfig, output, primaryKey, range.Start, range.End)));

        await Task.WhenAll(readTasks);
        output.Complete();
    }

    private static async Task ReadIdRangeAsync(TableConfiguration tableConfig, DbConfig dbConfig, ChannelWriter<Dictionary<string, object>> output, string primaryKey, object rangeStart, object rangeEnd)
    {
        await using var connection = dbConfig.Connection();
        await connection.OpenAsync();

        var baseQuery = tableConfig.Limit is null
            ? $"SELECT * FROM {tableConfig.Table} WHERE {primaryKey} > @start AND {primaryKey} <= @end ORDER BY {primaryKey}"
            : $"SELECT * FROM {tableConfig.Table} WHERE {primaryKey} > @start AND {primaryKey} <= @end ORDER BY {primaryKey} LIMIT {tableConfig.Limit}";

        object lastId = rangeStart;
        bool hasMoreData = true;

        while (hasMoreData)
        {
            await using var command = new NpgsqlCommand(baseQuery, connection);
            command.Parameters.AddWithValue("@start", lastId);
            command.Parameters.AddWithValue("@end", rangeEnd);

            await using var reader = await command.ExecuteReaderAsync();
            var columns = (await reader.GetColumnSchemaAsync()).ToImmutableArray();

            int pageCount = 0;
            while (await reader.ReadAsync())
            {
                var row = ReadRow(reader, columns);
                await output.WriteAsync(row);
                lastId = row[primaryKey];
                pageCount++;
            }

            hasMoreData = pageCount >= READ_PAGE_SIZE;
        }
    }

    private static List<(object Start, object End)> SplitIdRange(object minId, object maxId, int partitions)
    {
        var ranges = new List<(object, object)>();

        if (minId is not IComparable)
            throw new NotSupportedException("Keyset pagination only supports comparable types (numbers, dates)");

        dynamic rangeSize = ((dynamic)maxId - (dynamic)minId) / partitions;
        dynamic current = minId;

        for (int i = 0; i < partitions; i++)
        {
            dynamic rangeStart = i == 0 ? current - 1 : current;
            dynamic rangeEnd = (i == partitions - 1) ? maxId : current + rangeSize;
            ranges.Add((rangeStart, rangeEnd));
            current = rangeEnd;
        }

        return ranges;
    }

    private static async Task<(object minId, object maxId)> GetMinMaxIdsAsync(TableConfiguration tableConfig, DbConfig dbConfig, string primaryKey)
    {
        await using var connection = dbConfig.Connection();
        await connection.OpenAsync();

        var query = tableConfig.Limit is null
            ? $"SELECT MIN({primaryKey}), MAX({primaryKey}) FROM {tableConfig.Table}"
            : $"SELECT MIN({primaryKey}), MAX({primaryKey}) FROM (SELECT {primaryKey} FROM {tableConfig.Table} LIMIT {tableConfig.Limit}) AS subquery";

        await using var command = new NpgsqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return (reader.IsDBNull(0) ? null! : reader.GetValue(0),
                    reader.IsDBNull(1) ? null! : reader.GetValue(1));
        }

        return (null!, null!);
    }

    private static async Task<string?> GetPrimaryKeyColumnAsync(TableConfiguration tableConfig, DbConfig dbConfig)
    {
        await using var connection = dbConfig.Connection();
        await connection.OpenAsync();

        var query = $@"SELECT a.attname FROM pg_index i
            JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE i.indrelid = to_regclass($1)
            AND i.indisprimary";

        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue(tableConfig.Table.ToString());

        await using var reader = await command.ExecuteReaderAsync();

        // This dummy default key is because we don't have any PK on example tables.
        return await reader.ReadAsync() ? reader.GetString(0) : DEFAULT_PK_NAME;
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
        var batch = new List<Dictionary<string, object>>(WRITE_BATCH_SIZE);

        await foreach (var row in input.ReadAllAsync())
        {
            ProcessRow(row, columnsToProcess);
            batch.Add(row);

            if (batch.Count >= WRITE_BATCH_SIZE)
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
    }

    private static async Task TruncateDestinationDatabaseTablesAsync(IReadOnlyList<TableConfiguration> tablesToProcess, DbConfig destinationDbConfig)
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