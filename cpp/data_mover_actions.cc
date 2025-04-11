#include <data_mover_actions.hh>

#include <utility/connection_from_config.hh>

#include <pqxx/pqxx>

#include <iostream>
#include <sstream>
#include <unordered_set>

void data_mover_actions::truncate_tables(
  const data_mover_config& config
) {
  auto connection = connection_from_config(config.destination);

  for(const auto& table_config : config.tables) {
    truncate_table(table_config, connection);
  }
}

void data_mover_actions::process_tables(
  const data_mover_config& config,
  const data_mover_structure& structure,
  const data_mover_processors& processors
) {
  const auto& source_structure = structure.source;
  auto source_connection = connection_from_config(config.source);
  auto destination_connection = connection_from_config(config.destination);

  for (const auto& table_config : config.tables) {
    const auto& schema_structure = source_structure.schemas.find(table_config.schema_name)->second;
    const auto& table_structure = schema_structure.tables.find(table_config.table_name)->second;

    process_table(
      table_config,
      table_structure,
      processors,
      source_connection,
      destination_connection
    );
  }
}

void data_mover_actions::truncate_table(
  const table_config& config,
  pqxx::connection& destination_connection
) {
  auto command = std::stringstream();
  command << "TRUNCATE TABLE " << config.schema_name << "." << config.table_name;

  auto work = pqxx::work(destination_connection);
  work.exec(command.str());
  work.commit();
}

void data_mover_actions::process_table(
  const table_config& config,
  const table_structure& structure,
  const data_mover_processors& data_mover_processors,
  pqxx::connection& source_connection,
  pqxx::connection& destination_connection
) {
  auto command = std::stringstream();
  command << "SELECT * FROM \"" << config.schema_name << "\".\"" << config.table_name << "\"";

  if (config.limit.has_value()) {
    command << " LIMIT " << config.limit.value();
  }

  auto work = pqxx::work(source_connection);
  auto result = work.exec(command.str());
  work.commit();

  const auto& column_processors = data_mover_processors.processors_for_schema_and_table(
    config.schema_name,
    config.table_name
  );

  std::cout << "processing table " << config.schema_name << "." << config.table_name << ":" << std::endl;

  auto start = std::chrono::high_resolution_clock::now();
  auto previous = start;
  auto current = start;
  auto count = 0L;

  for (const auto& raw : result) {
    auto row = std::unordered_map<std::string, std::string>();

    read_row(row, raw);
    process_row(row, column_processors);
    write_row(row, config, structure, destination_connection);

    ++count;

    current = std::chrono::high_resolution_clock::now();
    if (current - previous > std::chrono::seconds(2)) {
      previous = current;
      const auto seconds = (double) std::chrono::duration_cast<std::chrono::milliseconds>(current - start).count() / 1000;
      std::cout << "  " << count << " rows processed in " << seconds << " seconds (" << (double) count / seconds << " rows/sec)" << std::endl;
    }
  }

  const auto seconds = (double) std::chrono::duration_cast<std::chrono::milliseconds>(current - start).count() / 1000;
  std::cout << "processed table " << config.schema_name << "." << config.table_name << ": ";
  std::cout << count << " rows processed in " << seconds << " seconds (" << (double) count / seconds << " rows/sec)" << std::endl;
}

void data_mover_actions::read_row(
  std::unordered_map<std::string, std::string>& row,
  const pqxx::row& raw
) {
  for (const auto& field : raw) {
    row.emplace(field.name(), field.c_str());
  }
}

void data_mover_actions::process_row(
  std::unordered_map<std::string, std::string>& row,
  const std::unordered_map<std::string, std::unique_ptr<abstract_processor>>& column_processors
) {
  for(const auto& [column, processor] : column_processors) {
    auto cursor = row.find(column);
    auto& value = cursor->second;
    value = processor->process_value(value);
  }
}

void data_mover_actions::write_row(
  const std::unordered_map<std::string, std::string>& row,
  const table_config& config,
  const table_structure& structure,
  pqxx::connection& destination_connection
) {
  auto command = std::stringstream();
  command << "INSERT INTO \"" << config.schema_name << "\".\"" << config.table_name << "\" (";

  auto values = std::stringstream();
  values << "VALUES (";

  bool first = true;
  for (const auto& [column_name, column_value] : row) {
    const auto& column_structure = structure.columns.find(column_name)->second;

    if (!first) {
      command << ", ";
      values << ", ";
    } else {
      first = false;
    }

    command << column_name;

    if (column_structure.type == "integer") {
      if (column_value.empty()) {
        values << "NULL";
      } else {
        values << column_value;
      }
    } else {
      values << "'" << column_value << "'";
    }
  }

  command << ")";
  values << ")";

  command << " " << values.str();

  auto work = pqxx::work(destination_connection);
  auto result = work.exec(command.str());
  work.commit();
}