#include <configuration/column_config.hh>

column_config::column_config(
  const std::string& schema_name,
  const std::string& table_name,
  const std::string& column_name,
  const std::string& processor
) :
  schema_name(schema_name),
  table_name(table_name),
  column_name(column_name),
  processor(processor) {
}

column_config column_config::from_toml(const toml::table& toml) {
  const auto& schema_name = toml["schemaName"].as_string()->ref<std::string>();
  const auto& table_name = toml["tableName"].as_string()->ref<std::string>();
  const auto& column_name = toml["columnName"].as_string()->ref<std::string>();
  const auto& processor = toml["processor"].as_string()->ref<std::string>();

  return {schema_name, table_name, column_name, processor};
}