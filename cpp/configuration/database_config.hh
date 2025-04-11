#ifndef DATA_MOVER_DATABASE_CONFIG_HH
#define DATA_MOVER_DATABASE_CONFIG_HH

#include <string>

#include <toml++/toml.hpp>

class database_config {
public:
  const std::string host;
  const uint32_t port;
  const std::string user;
  const std::string password;
  const std::string database;

private:
  database_config(
    const std::string& host,
    const uint32_t& port,
    const std::string& user,
    const std::string& password,
    const std::string& database
  );

public:
  static database_config from_toml(const toml::table& toml);
};

#endif
