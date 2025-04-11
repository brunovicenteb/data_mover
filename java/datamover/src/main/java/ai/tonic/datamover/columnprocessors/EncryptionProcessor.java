package ai.tonic.datamover.columnprocessors;

import javax.crypto.Cipher;
import javax.crypto.spec.SecretKeySpec;
import java.security.Key;

public class EncryptionProcessor implements ColumnProcessor {
    private static final String ALGORITHM = "AES";

    @Override
    public Object processValue(Object input) {
        var plainText = input.toString();
        if (plainText != null) {
            var cipherText = encryptToHexString(plainText);
            return cipherText;
        } else {
            throw new IllegalArgumentException("Couldn't toString an input to the EncryptionProcessor");
        }
    }

    private static String encryptToHexString(String plainText) {
        try {
            String keyString = "0123456789ABCDEF"; // This is just an example
            Key key = new SecretKeySpec(keyString.getBytes(), ALGORITHM);
            Cipher cipher = Cipher.getInstance(ALGORITHM);
            cipher.init(Cipher.ENCRYPT_MODE, key);
            byte[] encryptedBytes = cipher.doFinal(plainText.getBytes());
            return byteArrayToHex(encryptedBytes);
        } catch (Exception e) {
            throw new RuntimeException("Error encrypting the string", e);
        }
    }

    public static String byteArrayToHex(byte[] a) {
        StringBuilder sb = new StringBuilder(a.length * 2);
        for (byte b : a) {
            sb.append(String.format("%02x", b));
        }
        return sb.toString();
    }
}
