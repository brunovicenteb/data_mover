#ifndef DATA_MOVER_DATA_MOVER_PROCESSORS_HH
#define DATA_MOVER_DATA_MOVER_PROCESSORS_HH

#include <configuration/data_mover_config.hh>
#include <processing/abstract_processor.hh>

#include <unordered_map>

class data_mover_processors {
public:
  using column_map = std::unordered_map<std::string, std::unique_ptr<abstract_processor>>;
  using table_map = std::unordered_map<std::string, column_map>;
  using schema_map = std::unordered_map<std::string, table_map>;

private:
  schema_map _lookup;
  column_map _empty;

private:
  data_mover_processors() = default;

public:
  const column_map& processors_for_schema_and_table(const std::string& schema, const std::string& table) const;

public:
  static data_mover_processors from_config(const data_mover_config& config);
};

#endif