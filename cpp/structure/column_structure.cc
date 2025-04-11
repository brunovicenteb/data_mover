#include <structure/column_structure.hh>

column_structure::column_structure(
  const std::string& name,
  const size_t& index,
  const std::string& type
) :
  name(name),
  index(index),
  type(type) {
}