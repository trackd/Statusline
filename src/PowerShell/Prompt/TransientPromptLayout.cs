namespace StatusLine;

/// <summary>Locates the previous prompt relative to the accepted input's newline.</summary>
internal static class TransientPromptLayout {
    internal static int? GetRow(string line, int inputColumn, int width, int endRow, int contextRows, string continuation) {
        if (width < 2 || inputColumn < 0 || inputColumn >= width || endRow < 0) {
            return null;
        }
        // PSReadLine's character-width rules differ between releases. Retain the full prompt
        // rather than guessing for control characters, surrogate pairs or combining text.
        foreach (char character in continuation) {
            if (character is < ' ' or > '~') {
                return null;
            }
        }

        int rowCount = contextRows + 1;
        int column = inputColumn;
        for (int index = 0; index < line.Length; index++) {
            char character = line[index];
            if (character == '\n') {
                rowCount++;
                column = continuation.Length;
                if (column >= width) {
                    return null;
                }

                continue;
            }

            if (character is < ' ' or > '~') {
                return null;
            }

            column++;
            if (column == width) {
                column = 0;
                if (index + 1 == line.Length || line[index + 1] != '\n') {
                    rowCount++;
                }
            }
        }

        int row = endRow - rowCount;
        return row >= 0 ? row : null;
    }
}
