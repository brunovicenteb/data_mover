#include <structure/schema_structure.hh>

schema_structure::schema_structure(
  const std::string& name,
  const std::unordered_map<std::string, table_structure>& tables
) :
  name(name),
  tables(tables) {
}