package ai.tonic.datamover;

import org.tomlj.TomlTable;

import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.SQLException;
import java.util.Objects;
import java.util.Properties;

public record DbConfig(String host, int port, String user, String password, String database)
{
    public static DbConfig readConfigFrom(TomlTable config, String type)
    {
        var databaseConfig = config.getTable(type);
        var host = databaseConfig.getString("host");
        var port = Objects.requireNonNull(databaseConfig.getLong("port")).intValue();
        var user = databaseConfig.getString("user");
        var password = databaseConfig.getString("password");
        var database = databaseConfig.getString("database");
        return new DbConfig(host, port, user, password, database);
    }

    public Connection connection()
    {
        String url = "jdbc:postgresql://" + host + ":" + port + "/" + database;
        Properties props = new Properties();
        props.setProperty("user", user);
        props.setProperty("password", password);
        try {
            return DriverManager.getConnection(url, props);
        } catch (SQLException e) {
            throw new RuntimeException(e);
        }
    }
}