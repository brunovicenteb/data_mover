#include <utility/connection_from_config.hh>

pqxx::connection connection_from_config(
  const database_config& config
) {
  auto options = std::stringstream{};

  options << "postgresql://" << config.user;
  if (!config.password.empty()) {
    options << ":" << config.password;
  }

  options << "@" << config.host;
  if (config.port != 5432) {
    options << ":" << config.port;
  }

  options << "/" << config.database;

  return pqxx::connection{options.str()};
}