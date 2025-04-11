#include <configuration/table_config.hh>

table_config::table_config(
  const std::string& schema_name,
  const std::string& table_name,
  const std::optional<uint32_t>& limit
) :
  schema_name(schema_name),
  table_name(table_name),
  limit(limit) {
}

table_config table_config::from_toml(const toml::table& toml) {
  const auto& schema_name = toml["schemaName"].as_string()->ref<std::string>();
  const auto& table_name = toml["tableName"].as_string()->ref<std::string>();
  const auto& limit = toml.contains("limit")
    ? std::optional<int>((uint32_t) toml["limit"].as_integer()->ref<int64_t>())
    : std::optional<int>(std::nullopt);

  return {schema_name, table_name, limit};
}