#include <structure/database_structure.hh>

#include <utility/connection_from_config.hh>

#include <iostream>

database_structure::database_structure(
  const std::string& name,
  const std::unordered_map<std::string, schema_structure>& schemas
) :
  name(name),
  schemas(schemas) {
}

database_structure database_structure::from_config(
  const database_config& database_config,
  const std::list<table_config>& table_configs
) {
  auto connection = connection_from_config(database_config);

  using column_lookup_t = std::unordered_map<std::string, column_structure>;
  using table_lookup_t = std::unordered_map<std::string, column_lookup_t>;
  using schema_lookup_t = std::unordered_map<std::string, table_lookup_t>;

  auto schema_lookup = schema_lookup_t();
  for (const auto& table_config : table_configs) {
    const auto& schema_name = table_config.schema_name;
    const auto& table_name = table_config.table_name;

    auto command = std::stringstream();
    command << R"(SELECT "column_name", "ordinal_position", "data_type" )";
    command << R"(FROM "information_schema"."columns" )";
    command << R"(WHERE )";
    command << R"("table_schema" = ')" << schema_name << "' AND ";
    command << R"("table_name" = ')" << table_name << "'";

    auto work = pqxx::work(connection);
    auto result = work.exec(command.str());
    work.commit();

    for (const auto& row : result) {
      const auto& column_name = row["column_name"].as<std::string>();
      const auto& column_index = row["ordinal_position"].as<size_t>();
      const auto& column_type =row["data_type"].as<std::string>();

      auto schema_it = schema_lookup.find(schema_name);
      if (schema_it == schema_lookup.end()) {
        schema_it = schema_lookup.emplace(schema_name, table_lookup_t()).first;
      }

      auto& table_lookup = schema_it->second;
      auto table_it = table_lookup.find(table_name);
      if (table_it == table_lookup.end()) {
        table_it = table_lookup.emplace(table_name, column_lookup_t()).first;
      }

      auto& column_lookup = table_it->second;
      auto column_it = column_lookup.find(column_name);
      if (column_it == column_lookup.end()) {
        column_lookup.emplace(column_name, column_structure(column_name, column_index, column_type));
      }
    }
  }

  auto schema_map = std::unordered_map<std::string, schema_structure>();
  for(const auto& [schema_name, table_lookup] : schema_lookup) {
    auto table_map = std::unordered_map<std::string, table_structure>();
    for (const auto& [table_name, column_lookup] : table_lookup) {
      auto column_map = std::unordered_map<std::string, column_structure>();
      for (const auto& [column_name, column_structure] : column_lookup) {
        column_map.emplace(column_name, column_structure);
      }
      table_map.emplace(table_name, table_structure(table_name, column_map));
    }
    schema_map.emplace(schema_name, schema_structure(schema_name, table_map));
  }
  return {database_config.database, schema_map};
}