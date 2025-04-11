package ai.tonic.datamover;

import ai.tonic.datamover.columnprocessors.ColumnProcessor;
import ai.tonic.datamover.columnprocessors.ColumnProcessors;
import com.google.common.base.Stopwatch;
import com.google.common.collect.ImmutableList;
import com.google.common.collect.ImmutableMap;
import org.tomlj.Toml;
import org.tomlj.TomlTable;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.sql.ResultSet;
import java.sql.ResultSetMetaData;
import java.sql.SQLException;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;

public class Main {
    public static void main(String[] args) {
        try {
            var config = readConfig(args);
            if (config == null) {
                printUsage();
                System.exit(1);
            }

            var sourceDbConfig = DbConfig.readConfigFrom(config, "source");
            var destinationDbConfig = DbConfig.readConfigFrom(config, "destination");

            // get list of tables and columns to process
            var tablesToProcess = readTables(config);
            var columnsToProcess = readColumns(config);

            // truncate target tables
            truncateDestinationDatabaseTables(tablesToProcess, destinationDbConfig);

            // process the tables.
            processTables(tablesToProcess, columnsToProcess, sourceDbConfig, destinationDbConfig);
            System.exit(0);
        }
        catch (RuntimeException e) {
            e.printStackTrace(System.out);
            System.exit(1);
        }
    }

    private static void truncateDestinationDatabaseTables(List<TableConfiguration> tablesToProcess, DbConfig destinationDbConfig)
    {
        for (var tableConfig : tablesToProcess)
        {
            truncateTable(tableConfig.table(), destinationDbConfig);
        }
    }

    private static void truncateTable(DatabaseTable table, DbConfig destinationDbConfig)
    {
        try(var connection = destinationDbConfig.connection();
            var statement = connection.createStatement()) {
            statement.execute("TRUNCATE TABLE " + table.toString());
        } catch (SQLException e) {
            throw new RuntimeException(e);
        }
    }

    private static void processTables(List<TableConfiguration> tablesToProcess, Map<DatabaseColumn, ColumnProcessor> columnsToProcess, DbConfig sourceDbConfig, DbConfig destinationDbConfig) {
        var totalStopWatch = Stopwatch.createStarted();
        for (var table : tablesToProcess) {
            var columns = columnsToProcess.entrySet().stream()
                    .filter(entry -> entry.getKey().table().equals(table.table()))
                    .collect(ImmutableMap.toImmutableMap(Map.Entry::getKey, Map.Entry::getValue));
            processTable(table, columns, sourceDbConfig, destinationDbConfig);
        }
        System.out.printf("Total time processing tables: %gs%n", totalStopWatch.elapsed().toMillis()/1000d);
    }


    private static void processTable(TableConfiguration table, Map<DatabaseColumn, ColumnProcessor> columnsToProcess, DbConfig sourceDbConfig, DbConfig destinationDbConfig) {
        var query = table.limit() == null
                ? "SELECT * FROM " + table.table()
                : "SELECT * FROM " + table.table() + " LIMIT " + table.limit();

        try (var sourceConnection = sourceDbConfig.connection();
            var destinationConnection = destinationDbConfig.connection();
            var readStatement = sourceConnection.createStatement()) {

            readStatement.setFetchSize(1000);
            var results = readStatement.executeQuery(query);
            var columnNames = getColumnNames(results.getMetaData());

            System.out.println("Starting table " + table.table());
            long count = 0;
            var sinceLastReport = Stopwatch.createStarted();
            var totalTime = Stopwatch.createStarted();
            while (results.next()) {
                var row = readRow(results, columnNames);
                processRow(row, columnsToProcess);
                writeRow(row, table.table(), columnNames, destinationConnection);

                count += 1;
                if (sinceLastReport.elapsed().toSeconds() > 10) {
                    System.out.printf("Processing %s for %gs at row number %d.%n", table.table().toString(), sinceLastReport.elapsed().toMillis()/1000d, count);
                    sinceLastReport.reset().start();
                }
            }
            System.out.printf("Table %s processed in: %gs%n", table, totalTime.elapsed().toMillis()/1000d);
        } catch (SQLException e) {
            throw new RuntimeException(e);
        }
    }

    private static List<String> getColumnNames(ResultSetMetaData metaData) throws SQLException {
        var retval = ImmutableList.<String>builder();
        for (int i = 1; i <= metaData.getColumnCount(); ++i) {
            retval.add(metaData.getColumnName(i));
        }
        return retval.build();
    }

    private static Map<String, Object> readRow(ResultSet reader, List<String> columns) throws SQLException {
        var retval = new HashMap<String, Object>(columns.size());
        for (var column : columns) {
            retval.put(column, reader.getObject(column));
        }
        return retval;
    }

    private static void processRow(Map<String, Object> row, Map<DatabaseColumn, ColumnProcessor> columnsToProcess) {
        for (var column : columnsToProcess.entrySet()) {
            var columnName = column.getKey().column();
            var newValue = column.getValue().processValue(row.get(columnName));
            row.put(columnName, newValue);
        }
    }

    private static void writeRow(Map<String, Object> row, DatabaseTable table, List<String> columns, java.sql.Connection destinationConnection) {
        var parameters = String.join(", ", Collections.nCopies(columns.size(), "?"));
        var query = "INSERT INTO " + table + " VALUES (" + parameters + ")";

        try (var statement = destinationConnection.prepareStatement(query)) {
            int columnIdx = 1;
            for (var column : columns) {
                statement.setObject(columnIdx++, row.get(column));
            }
            statement.executeUpdate();
        } catch (SQLException e) {
            throw new RuntimeException(e);
        }
    }

    private static TomlTable readConfig(String[] args) {
        try {
            var path = Paths.get(args[0]);
            if (!Files.exists(path))
                return null;
            return Toml.parse(path);
        }
        catch (IndexOutOfBoundsException e) {
            return null;
        }
        catch (RuntimeException | IOException e) {
            e.printStackTrace(System.out);
            return null;
        }
    }

    private static List<TableConfiguration> readTables(TomlTable config) {
        var retval = ImmutableList.<TableConfiguration>builder();

        var tableArray = config.getArray("table");
        if (tableArray == null){
            throw new IllegalArgumentException("Array of 'table's is missing from config");
        }

        for (int i = 0; i < tableArray.size(); ++i) {
            var table = tableArray.getTable(i);
            var schemaName = table.getString("schemaName");
            var tableName = table.getString("tableName");
            var limit = table.getLong("limit");
            if (schemaName == null || tableName == null){
                throw new IllegalArgumentException("Table must contain a 'schemaName' and 'tableName' property");
            }

            retval.add(new TableConfiguration(new DatabaseTable(schemaName, tableName), limit));
        }

        return retval.build();
    }

    private static Map<DatabaseColumn, ColumnProcessor> readColumns(TomlTable config) {
        var retval = ImmutableMap.<DatabaseColumn, ColumnProcessor>builder();

        if (!config.contains("column")) {
            // no columns, then return empty config
            return retval.build();
        }

        var columnArray = config.getArray("column");
        if (columnArray == null) {
            throw new IllegalArgumentException("Array of 'table's is missing from config");
        }

        for (int i = 0; i < columnArray.size(); ++i) {
            var column = columnArray.getTable(i);
            var schemaName = column.getString("schemaName");
            var tableName = column.getString("tableName");
            var columnName = column.getString("columnName");
            var processor = column.getString("processor");
            if (schemaName == null || tableName == null || columnName == null || processor == null) {
                throw new IllegalArgumentException("Column must contain a 'schemaName', 'tableName','columnName' and 'processor', property");
            }

            var table = new DatabaseTable(schemaName, tableName);
            var columnProcessor = ColumnProcessors.create(processor);
            retval.put(new DatabaseColumn(table, columnName), columnProcessor);
        }

        return retval.build();
    }

    private static void printUsage() {
        System.out.println("Need to supply a TOML file configuration as the first commandline argument.");
    }
}