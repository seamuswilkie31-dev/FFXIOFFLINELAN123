namespace LocalXI.Core.Util;

/// <summary>
/// A minimal INI editor that changes individual keys and leaves every other line (comments, ordering) untouched.
/// Used for Ashita boot configs and MariaDB's my.ini.
/// </summary>
public sealed class IniFile
{
    private readonly List<string> _lines;

    public IniFile(IEnumerable<string> lines) => _lines = lines.ToList();

    public static IniFile Load(string path) => new(File.ReadAllLines(path));

    public string? Get(string section, string key)
    {
        var index = FindKey(section, key);
        if (index < 0)
        {
            return null;
        }

        var line = _lines[index];
        return line[(line.IndexOf('=') + 1)..].Trim();
    }

    public void Set(string section, string key, string value)
    {
        var entry = $"{key} = {value}";
        var keyIndex = FindKey(section, key);
        if (keyIndex >= 0)
        {
            _lines[keyIndex] = entry;
            return;
        }

        var sectionIndex = FindSection(section);
        if (sectionIndex < 0)
        {
            if (_lines.Count > 0 && !string.IsNullOrWhiteSpace(_lines[^1]))
            {
                _lines.Add("");
            }

            _lines.Add($"[{section}]");
            _lines.Add(entry);
            return;
        }

        // Append at the end of the section, before any blank lines that separate it from the next one.
        var insertAt = SectionEnd(sectionIndex);
        while (insertAt > sectionIndex + 1 && string.IsNullOrWhiteSpace(_lines[insertAt - 1]))
        {
            insertAt--;
        }

        _lines.Insert(insertAt, entry);
    }

    public void Save(string path) => File.WriteAllLines(path, _lines);

    public override string ToString() => string.Join(Environment.NewLine, _lines);

    private int FindSection(string section)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var trimmed = _lines[i].Trim();
            if (IsHeader(trimmed) && string.Equals(trimmed[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int SectionEnd(int sectionIndex)
    {
        var end = sectionIndex + 1;
        while (end < _lines.Count && !IsHeader(_lines[end].Trim()))
        {
            end++;
        }

        return end;
    }

    private int FindKey(string section, string key)
    {
        var sectionIndex = FindSection(section);
        if (sectionIndex < 0)
        {
            return -1;
        }

        var end = SectionEnd(sectionIndex);
        for (var i = sectionIndex + 1; i < end; i++)
        {
            var trimmed = _lines[i].TrimStart();
            if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                continue;
            }

            var equals = trimmed.IndexOf('=');
            if (equals > 0 && string.Equals(trimmed[..equals].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsHeader(string trimmed) => trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']';
}
