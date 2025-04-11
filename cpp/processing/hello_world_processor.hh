#ifndef DATA_MOVER_HELLO_WORLD_PROCESSOR_HH
#define DATA_MOVER_HELLO_WORLD_PROCESSOR_HH

#include <processing/abstract_processor.hh>

class hello_world_processor : public abstract_processor {
public:
  hello_world_processor() = default;
  ~hello_world_processor() override = default;

public:
  std::string process_value(const std::string& value) const override;
};

#endif