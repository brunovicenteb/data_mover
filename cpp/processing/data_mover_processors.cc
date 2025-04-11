#include <processing/data_mover_processors.hh>
#include <processing/hello_world_processor.hh>
#include <processing/encryption_processor.hh>

const data_mover_processors::column_map& data_mover_processors::processors_for_schema_and_table(
  const std::string& schema,
  const std::string& table
) const {
  auto schema_it = _lookup.find(schema);
  if (schema_it == _lookup.end()) {
    return _empty;
  }

  const auto& table_map = schema_it->second;
  auto table_it = table_map.find(table);
  if (table_it == table_map.end()) {
    return _empty;
  }

  return table_it->second;
}

data_mover_processors data_mover_processors::from_config(const data_mover_config& config) {
  auto processors = data_mover_processors();

  for (const auto& column_config : config.columns) {
    const auto& schema = column_config.schema_name;
    const auto& table = column_config.table_name;
    const auto& column = column_config.column_name;
    const auto& processor = column_config.processor;

    auto& schema_map = processors._lookup;
    auto schema_it = schema_map.find(schema);
    if (schema_it == schema_map.end()) {
      schema_it = schema_map.emplace(schema, table_map()).first;
    }

    auto& table_map = schema_it->second;
    auto table_it = table_map.find(table);
    if (table_it == table_map.end()) {
      table_it = table_map.emplace(table, column_map()).first;
    }

    auto& column_map = table_it->second;
    auto column_it = column_map.find(column);
    if (column_it == column_map.end()) {
      if (processor == "HelloWorld") {
        column_map.emplace(column, new hello_world_processor());
      } else if (processor == "Encryption") {
        column_map.emplace(column, new encryption_processor());
      } else {
        throw std::invalid_argument("No processor named " + processor + " exists.");
      }
    }
  }

  return processors;
}