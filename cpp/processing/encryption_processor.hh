#ifndef DATA_MOVER_ENCRYPTION_PROCESSOR_HH
#define DATA_MOVER_ENCRYPTION_PROCESSOR_HH

#include <processing/abstract_processor.hh>

class encryption_processor : public abstract_processor {
public:
  encryption_processor() = default;
  ~encryption_processor() override = default;

public:
  std::string process_value(const std::string& value) const override;

private:
  static constexpr unsigned char _aes_key[] = "0123456789ABCDEF";
  static constexpr unsigned char _aes_iv[] = "ABCDEF9876543210";
};

#endif