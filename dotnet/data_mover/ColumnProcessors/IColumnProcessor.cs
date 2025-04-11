namespace data_mover.ColumnProcessors;

public interface IColumnProcessor
{
    object ProcessValue(object input);
}