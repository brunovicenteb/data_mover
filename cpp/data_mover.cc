#include <configuration/data_mover_config.hh>
#include <processing/data_mover_processors.hh>
#include <structure/data_mover_structure.hh>
#include <data_mover_actions.hh>

#include <iostream>

int main(int argc, char **argv) {
  if (argc < 2) {
    std::cout << "Usage: data_mover path/to/config.toml" << std::endl;
    return -1;
  }

  auto config = data_mover_config::from_file(argv[1]);
  auto structure = data_mover_structure::from_config(config);
  auto processors = data_mover_processors::from_config(config);

  data_mover_actions::truncate_tables(config);
  data_mover_actions::process_tables(config, structure, processors);

  return 0;
}