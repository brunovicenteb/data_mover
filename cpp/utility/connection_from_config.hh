#ifndef DATA_MOVER_CONNECTION_FROM_CONFIG_HH
#define DATA_MOVER_CONNECTION_FROM_CONFIG_HH

#include <configuration/database_config.hh>

#include <pqxx/pqxx>

pqxx::connection connection_from_config(
  const database_config& config
);

#endif