using Npgsql;
using Tomlyn.Model;

namespace data_mover;

public record DbConfig(string Host, int Port, string User, string Password, string Database)
{
    public static DbConfig ReadConfigFrom(TomlTable config, string type)
    {
        var databaseConfig = config[type] as TomlTable;
        if (databaseConfig is null)
        {
            throw new ArgumentException("Configuration missing database type: " + type);
        }
        var host = (string) databaseConfig["host"];
        var port = Convert.ToInt32((long) databaseConfig["port"]);
        var user = (string) databaseConfig["user"];
        var password = (string) databaseConfig["password"];
        var database = (string) databaseConfig["database"];

        return new DbConfig(host, port, user, password, database);
    }

    public NpgsqlConnection Connection()
    {
        var stringBuilder = new NpgsqlConnectionStringBuilder
        {
            Database = Database,
            Host = Host,
            Port = Port,
            Username = User,
            Password = Password
        };

        return new NpgsqlConnection(stringBuilder.ToString());
    }
}