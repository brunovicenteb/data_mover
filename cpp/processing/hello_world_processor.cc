#include <processing/hello_world_processor.hh>

std::string hello_world_processor::process_value(const std::string& value) const {
  return "Hello, World!";
}