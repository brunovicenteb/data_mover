#ifndef DATA_MOVER_DATA_MOVER_STRUCTURE_HH
#define DATA_MOVER_DATA_MOVER_STRUCTURE_HH

#include <configuration/data_mover_config.hh>
#include <structure/database_structure.hh>

class data_mover_structure {
public:
  const database_structure source;
  const database_structure destination;

private:
  data_mover_structure(
    const database_structure& source,
    const database_structure& destination
  );

public:
  static data_mover_structure from_config(const data_mover_config& config);
};

#endif