#ifndef DATA_MOVER_DATA_MOVER_ACTIONS_HH
#define DATA_MOVER_DATA_MOVER_ACTIONS_HH

#include <configuration/data_mover_config.hh>
#include <configuration/table_config.hh>

#include <processing/data_mover_processors.hh>
#include <processing/abstract_processor.hh>

#include <structure/data_mover_structure.hh>

#include <pqxx/pqxx>

class data_mover_actions {
public:
  static void truncate_tables(
    const data_mover_config& config
  );

  static void process_tables(
    const data_mover_config& config,
    const data_mover_structure& structure,
    const data_mover_processors& processors
  );

protected:
  static void truncate_table(
    const table_config& config,
    pqxx::connection& destination_connection
  );

  static void process_table(
    const table_config& config,
    const table_structure& structure,
    const data_mover_processors& processors,
    pqxx::connection& source_connection,
    pqxx::connection& destination_connection
  );

  static void read_row(
    std::unordered_map<std::string, std::string>& row,
    const pqxx::row& raw
  );

  static void process_row(
    std::unordered_map<std::string, std::string>& row,
    const std::unordered_map<std::string, std::unique_ptr<abstract_processor>>& processors
  );

  static void write_row(
    const std::unordered_map<std::string, std::string>& row,
    const table_config& config,
    const table_structure& structure,
    pqxx::connection& destination_connection
  );
};

#endif