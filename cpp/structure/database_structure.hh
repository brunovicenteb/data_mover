#ifndef DATA_MOVER_DATABASE_STRUCTURE_HH
#define DATA_MOVER_DATABASE_STRUCTURE_HH

#include <structure/schema_structure.hh>

#include <configuration/database_config.hh>
#include <configuration/table_config.hh>

#include <list>
#include <string>
#include <unordered_map>

class database_structure {
public:
  const std::string name;
  const std::unordered_map<std::string, schema_structure> schemas;

private:
  database_structure(
    const std::string& name,
    const std::unordered_map<std::string, schema_structure>& schemas
  );

public:
  static database_structure from_config(
    const database_config& database_config,
    const std::list<table_config>& table_configs
  );
};

#endif