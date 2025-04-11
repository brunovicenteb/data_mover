#ifndef DATA_MOVER_COLUMN_STRUCTURE_HH
#define DATA_MOVER_COLUMN_STRUCTURE_HH

#include <string>

class column_structure {
public:
  const std::string name;
  const size_t index;
  const std::string type;

private:
  column_structure(
    const std::string& name,
    const size_t& index,
    const std::string& type
  );

public:
  friend class database_structure;
};

#endif