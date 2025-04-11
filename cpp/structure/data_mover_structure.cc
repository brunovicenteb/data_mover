#include <structure/data_mover_structure.hh>

data_mover_structure::data_mover_structure(
  const database_structure& source,
  const database_structure& destination
) :
  source(source),
  destination(destination) {
}

data_mover_structure data_mover_structure::from_config(const data_mover_config& config) {
  const auto& source_config = config.source;
  const auto& destination_config = config.destination;
  const auto& table_configs = config.tables;

  const auto source_structure = database_structure::from_config(source_config, table_configs);
  const auto destination_structure = database_structure::from_config(destination_config, table_configs);

  return {source_structure, destination_structure};
}