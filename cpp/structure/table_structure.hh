#ifndef DATA_MOVER_TABLE_STRUCTURE_HH
#define DATA_MOVER_TABLE_STRUCTURE_HH

#include <structure/column_structure.hh>

#include <string>
#include <unordered_map>

class table_structure {
public:
  const std::string name;
  const std::unordered_map<std::string, column_structure> columns;

private:
  table_structure(
    const std::string& name,
    const std::unordered_map<std::string, column_structure>& columns
  );

public:
  friend class database_structure;
};

#endif