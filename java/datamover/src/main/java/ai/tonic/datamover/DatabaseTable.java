package ai.tonic.datamover;

public record DatabaseTable(String schema, String table) {
    public String toString() {
        return "\"" + schema + "\".\"" + table + "\"";
    }
}
