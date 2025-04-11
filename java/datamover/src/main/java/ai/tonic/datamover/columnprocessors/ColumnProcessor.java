package ai.tonic.datamover.columnprocessors;

public interface ColumnProcessor {
    Object processValue(Object input);
}
