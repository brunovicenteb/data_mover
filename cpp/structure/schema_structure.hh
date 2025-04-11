#ifndef DATA_MOVER_SCHEMA_STRUCTURE_HH
#define DATA_MOVER_SCHEMA_STRUCTURE_HH

#include <structure/table_structure.hh>

#include <string>
#include <unordered_map>

class schema_structure {
public:
  const std::string name;
  const std::unordered_map<std::string, table_structure> tables;

private:
  schema_structure(
    const std::string& name,
    const std::unordered_map<std::string, table_structure>& tables
  );

public:
  friend class database_structure;
};

#endif