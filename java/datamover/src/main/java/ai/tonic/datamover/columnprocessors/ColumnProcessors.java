package ai.tonic.datamover.columnprocessors;

public class ColumnProcessors {
    public static ColumnProcessor create(String type)
    {
        return switch (type)
        {
            case "HelloWorld" -> new HelloWorldProcessor();
            case "Encryption" -> new EncryptionProcessor();
            default -> throw new IllegalArgumentException(type);
        };
    }

}
