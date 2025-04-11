package ai.tonic.datamover;

import javax.annotation.Nullable;

public record TableConfiguration(DatabaseTable table, @Nullable Long limit) {
}
