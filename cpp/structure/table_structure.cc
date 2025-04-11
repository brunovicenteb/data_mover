#include <structure/table_structure.hh>

table_structure::table_structure(
  const std::string& name,
  const std::unordered_map<std::string, column_structure>& columns
) :
  name(name),
  columns(columns) {
}