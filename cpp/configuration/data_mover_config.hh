#ifndef DATA_MOVER_DATA_MOVER_CONFIG_HH
#define DATA_MOVER_DATA_MOVER_CONFIG_HH

#include <configuration/database_config.hh>
#include <configuration/table_config.hh>
#include <configuration/column_config.hh>

#include <list>

class data_mover_config {
public:
  const database_config source;
  const database_config destination;
  const std::list<table_config> tables;
  const std::list<column_config> columns;

private:
  data_mover_config(
    const database_config& source,
    const database_config& destination,
    const std::list<table_config>& tables,
    const std::list<column_config>& columns
  );

public:
  static data_mover_config from_file(const std::string& file);
};

#endif