package ai.tonic.datamover.columnprocessors;

public class HelloWorldProcessor implements ColumnProcessor {
    @Override
    public Object processValue(Object input) {
        return "Hello, World!";
    }
}
