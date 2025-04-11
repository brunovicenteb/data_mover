#include <configuration/data_mover_config.hh>

data_mover_config::data_mover_config(
  const database_config& source,
  const database_config& destination,
  const std::list<table_config>& tables,
  const std::list<column_config>& columns
) :
  source(source),
  destination(destination),
  tables(tables),
  columns(columns) {
}

data_mover_config data_mover_config::from_file(const std::string& file) {
  const auto toml = toml::parse_file(file);

  if (!toml.contains("source")) {
    throw std::invalid_argument("Configuration missing description of source database");
  }
  const auto source_toml = toml["source"].as_table()->ref<toml::table>();
  const auto source_config = database_config::from_toml(source_toml);

  if (!toml.contains("destination")) {
    throw std::invalid_argument("Configuration missing description of destination database");
  }
  const auto destination_toml = toml["destination"].as_table()->ref<toml::table>();
  const auto destination_config = database_config::from_toml(destination_toml);

  if (!toml.contains("table")) {
    throw std::invalid_argument("Configuration missing descriptions of tables");
  }
  const auto tables_toml = toml["table"].as_array()->ref<toml::array>();
  auto tables_config = std::list<table_config>();
  for (const auto& table_toml : tables_toml) {
    auto table_config = table_config::from_toml(table_toml.as_table()->ref<toml::table>());
    tables_config.push_back(table_config);
  }

  if (!toml.contains("column")) {
    throw std::invalid_argument("Configuration missing descriptions of column");
  }
  const auto columns_toml = toml["column"].as_array()->ref<toml::array>();
  auto columns_config = std::list<column_config>();
  for (const auto& column_toml : columns_toml) {
    auto column_config = column_config::from_toml(column_toml.as_table()->ref<toml::table>());
    columns_config.push_back(column_config);
  }

  return {source_config, destination_config, tables_config, columns_config};
}