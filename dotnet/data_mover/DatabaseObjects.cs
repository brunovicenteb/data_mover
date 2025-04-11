using System.Xml.Serialization;

namespace data_mover;

public record DatabaseTable(string Schema, string Table)
{
    public override string ToString() => $@"""{Schema}"".""{Table}""";
}

public record TableConfiguration(DatabaseTable Table, long? Limit);

public record DatabaseColumn(DatabaseTable Table, string Column);