#include <processing/encryption_processor.hh>

#include <AES.h>

#include <codecvt>

std::string encryption_processor::process_value(const std::string& value) const {
  const auto padding = 16 - value.length() % 16;
  const auto padded = padding == 0
    ? std::string(value)
    : value + std::string(padding, '\0');
  const auto length = padded.length();

  const auto* input_raw = reinterpret_cast<const unsigned char*>(padded.c_str());
  auto aes = AES(AESKeyLength::AES_256);
  const auto* output_raw = aes.EncryptCBC(input_raw, length, _aes_key, _aes_iv);

  auto crypto_u16 = std::u16string(length, '\0');
  for (auto index = 0; index < length; ++index) {
    crypto_u16[index] = output_raw[index];
    crypto_u16[index] += 256;
  }
  auto crypto_u8 = std::wstring_convert<std::codecvt_utf8_utf16<char16_t>, char16_t>{}.to_bytes(crypto_u16);

  delete output_raw;
  return crypto_u8;
}
