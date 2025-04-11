#include <configuration/database_config.hh>

database_config::database_config(
  const std::string& host,
  const uint32_t& port,
  const std::string& user,
  const std::string& password,
  const std::string& database
) :
  host(host),
  port(port),
  user(user),
  password(password),
  database(database) {
}

database_config database_config::from_toml(const toml::table& toml) {
  if (!toml.contains("host")) {
    throw std::invalid_argument("Database configuration missing host");
  }
  const auto& host = toml["host"].as_string()->ref<std::string>();

  if (!toml.contains("port")) {
    throw std::invalid_argument("Database configuration missing port");
  }
  const auto& port = (uint32_t) toml["port"].as_integer()->ref<int64_t>();

  if (!toml.contains("user")) {
    throw std::invalid_argument("Database configuration missing user");
  }
  const auto& user = toml["user"].as_string()->ref<std::string>();

  if (!toml.contains("password")) {
    throw std::invalid_argument("Database configuration missing password");
  }
  const auto& password = toml["password"].as_string()->ref<std::string>();

  if (!toml.contains("database")) {
    throw std::invalid_argument("Database configuration missing database");
  }
  const auto& database = toml["database"].as_string()->ref<std::string>();

  return {host, port, user, password, database};
}