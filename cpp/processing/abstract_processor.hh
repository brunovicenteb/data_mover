#ifndef DATA_MOVER_ABSTRACT_PROCESSOR_HH
#define DATA_MOVER_ABSTRACT_PROCESSOR_HH

#include <string>

class abstract_processor {
public:
  abstract_processor() = default;
  virtual ~abstract_processor() = default;

public:
  virtual std::string process_value(const std::string& value) const = 0;
};

#endif
