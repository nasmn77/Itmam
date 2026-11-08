namespace Itmam.Core.Languages;

/// <summary>Which letters sit next to each other on a keyboard, as matching-key characters.</summary>
internal static class KeyboardNeighbors
{
    /// <param name="rows">The letter rows from top to bottom, each staggered half a key to the right of the one above; '\0' = a key to skip.</param>
    /// <param name="fold">The language's folding: letters sharing a key character (ة and ه) share their neighbors.</param>
    public static IReadOnlyDictionary<char, string> Build(string[] rows, Func<char, char> fold)
    {
        var neighbors = new Dictionary<char, HashSet<char>>();
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                Link(row, column, row, column + 1);
                Link(row, column, row + 1, column - 1);
                Link(row, column, row + 1, column);
            }
        }

        return neighbors.ToDictionary(n => n.Key, n => new string([.. n.Value.Order()]));

        void Link(int row, int column, int otherRow, int otherColumn)
        {
            if (otherRow >= rows.Length || otherColumn < 0 || otherColumn >= rows[otherRow].Length
                || rows[row][column] == '\0' || rows[otherRow][otherColumn] == '\0')
            {
                return;
            }

            var (a, b) = (fold(rows[row][column]), fold(rows[otherRow][otherColumn]));
            if (a != b)
            {
                Add(a, b);
                Add(b, a);
            }
        }

        void Add(char letter, char neighbor)
        {
            if (!neighbors.TryGetValue(letter, out var set))
            {
                neighbors[letter] = set = [];
            }

            set.Add(neighbor);
        }
    }
}
