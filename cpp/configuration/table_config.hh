#ifndef DATA_MOVER_TABLE_CONFIG_HH
#define DATA_MOVER_TABLE_CONFIG_HH

#include <string>

#include <toml++/toml.hpp>

class table_config {
public:
  const std::string schema_name;
  const std::string table_name;
  const std::optional<uint32_t> limit;

private:
  table_config(
    const std::string& schema_name,
    const std::string& table_name,
    const std::optional<uint32_t>& limit
  );

public:
  static table_config from_toml(const toml::table& toml);
};

#endif
