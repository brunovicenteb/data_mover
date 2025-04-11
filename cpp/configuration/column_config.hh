#ifndef DATA_MOVER_COLUMN_CONFIG_HH
#define DATA_MOVER_COLUMN_CONFIG_HH

#include <string>

#include <toml++/toml.hpp>

class column_config {
public:
  const std::string schema_name;
  const std::string table_name;
  const std::string column_name;
  const std::string processor;

private:
  column_config(
    const std::string& schema_name,
    const std::string& table_name,
    const std::string& column_name,
    const std::string& processor
  );

public:
  static column_config from_toml(const toml::table& toml);
};

#endif