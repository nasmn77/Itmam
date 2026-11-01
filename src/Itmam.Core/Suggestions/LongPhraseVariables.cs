using System.Globalization;
using System.Text;
using Itmam.Core.Text;

namespace Itmam.Core.Suggestions;

/// <summary>A value Itmam fills in by itself when a long phrase is inserted.</summary>
public enum AutoVariable
{
    Date,
    HijriDate,
    Day,
    Time,
}

/// <summary>One piece of a long phrase's text: plain text or a variable.</summary>
public abstract record VariablePart
{
    /// <summary>Text inserted as it is.</summary>
    public sealed record Literal(string Text) : VariablePart;

    /// <summary><c>{name}</c>: asked for when the phrase is inserted.</summary>
    public sealed record Input(string Name) : VariablePart;

    /// <summary><c>{{name}}</c> with a known name: filled in by Itmam.</summary>
    public sealed record Auto(AutoVariable Kind, string Raw) : VariablePart;

    /// <summary><c>{{name}}</c> with a name that is not one of the automatic ones (refused when saving).</summary>
    public sealed record UnknownAuto(string Name, string Raw) : VariablePart;
}

/// <summary>
/// Variables in long phrases (user decisions 1.2): <c>{name}</c> is asked for when the phrase is
/// inserted (the same name is asked once), <c>{{name}}</c> is filled in automatically and must be one
/// of the date, the Hijri date, the day or the time. Braces that do not form a variable stay as text.
/// Values typed by the user are never stored.
/// </summary>
public static class LongPhraseVariables
{
    /// <summary>Longest variable name, in characters.</summary>
    public const int MaxNameLength = 30;

    private static readonly (AutoVariable Kind, string Arabic, string English)[] AutoNames =
    [
        (AutoVariable.Date, "التاريخ", "date"),
        (AutoVariable.HijriDate, "التاريخ الهجري", "hijri date"),
        (AutoVariable.Day, "اليوم", "day"),
        (AutoVariable.Time, "الوقت", "time"),
    ];

    private static readonly string[] ArabicMonths =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    private static readonly string[] EnglishMonths =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    private static readonly string[] ArabicHijriMonths =
        ["محرم", "صفر", "ربيع الأول", "ربيع الآخر", "جمادى الأولى", "جمادى الآخرة", "رجب", "شعبان", "رمضان", "شوال", "ذو القعدة", "ذو الحجة"];

    private static readonly string[] EnglishHijriMonths =
        ["Muharram", "Safar", "Rabi al-Awwal", "Rabi al-Thani", "Jumada al-Awwal", "Jumada al-Thani", "Rajab", "Shaban", "Ramadan", "Shawwal", "Dhu al-Qadah", "Dhu al-Hijjah"];

    // Indexed by DayOfWeek (Sunday = 0).
    private static readonly string[] ArabicDays = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];

    private static readonly string[] EnglishDays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    /// <summary>The automatic variables, in the order they are offered.</summary>
    public static IReadOnlyList<AutoVariable> All { get; } = [.. AutoNames.Select(n => n.Kind)];

    /// <summary>The name written between the double braces, in the interface language.</summary>
    public static string NameOf(AutoVariable kind, bool arabic)
    {
        var names = AutoNames.First(n => n.Kind == kind);
        return arabic ? names.Arabic : names.English;
    }

    /// <summary>The text that inserts a variable asked for: <c>{name}</c>.</summary>
    public static string InputToken(string name) => "{" + Clean(name) + "}";

    /// <summary>The text that inserts an automatic variable: <c>{{name}}</c>.</summary>
    public static string AutoToken(AutoVariable kind, bool arabic) => "{{" + NameOf(kind, arabic) + "}}";

    /// <summary>A name of 1 to 30 characters on one line, without braces.</summary>
    public static bool IsValidName(string name)
    {
        var cleaned = Clean(name);
        return cleaned.Length is > 0 and <= MaxNameLength && cleaned.IndexOfAny(['{', '}']) < 0;
    }

    /// <summary>The text split into plain text and variables.</summary>
    public static IReadOnlyList<VariablePart> Parse(string text)
    {
        var parts = new List<VariablePart>();
        var literal = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{' && TryVariable(text, i, out var part, out var length))
            {
                if (literal.Length > 0)
                {
                    parts.Add(new VariablePart.Literal(literal.ToString()));
                    literal.Clear();
                }

                parts.Add(part);
                i += length;
            }
            else
            {
                literal.Append(text[i]);
                i++;
            }
        }

        if (literal.Length > 0)
        {
            parts.Add(new VariablePart.Literal(literal.ToString()));
        }

        return parts;
    }

    /// <summary>The names asked for, each once, in the order they first appear.</summary>
    public static IReadOnlyList<string> InputNames(string text) =>
        [.. Parse(text).OfType<VariablePart.Input>().Select(p => p.Name).Distinct(StringComparer.Ordinal)];

    /// <summary><c>{{name}}</c> variables whose name is not an automatic one (the phrase is not saved).</summary>
    public static IReadOnlyList<string> UnknownAutoVariables(string text) =>
        [.. Parse(text).OfType<VariablePart.UnknownAuto>().Select(p => p.Raw).Distinct(StringComparer.Ordinal)];

    /// <summary>The text has at least one variable asked for.</summary>
    public static bool HasInputs(string text) => Parse(text).Any(p => p is VariablePart.Input);

    /// <summary>
    /// The text to insert: automatic variables filled in, in Arabic when the phrase's own text has Arabic
    /// letters and in English otherwise; variables asked for replaced by <paramref name="values"/> (an
    /// empty value when missing), or kept as <c>{name}</c> when <paramref name="values"/> is null (preview).
    /// </summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string>? values, DateTime now)
    {
        var parts = Parse(text);
        var arabic = parts.OfType<VariablePart.Literal>().Any(p => p.Text.Any(ArabicText.IsArabicLetter));
        var result = new StringBuilder(text.Length);
        foreach (var part in parts)
        {
            result.Append(part switch
            {
                VariablePart.Literal literal => literal.Text,
                VariablePart.Input input when values is null => InputToken(input.Name),
                VariablePart.Input input => values.GetValueOrDefault(input.Name, string.Empty),
                VariablePart.Auto auto => Format(auto.Kind, now, arabic),
                VariablePart.UnknownAuto unknown => unknown.Raw,
                _ => string.Empty,
            });
        }

        return result.ToString();
    }

    /// <summary>The value of an automatic variable at <paramref name="now"/>.</summary>
    public static string Format(AutoVariable kind, DateTime now, bool arabic)
    {
        var inv = CultureInfo.InvariantCulture;
        switch (kind)
        {
            case AutoVariable.Date:
                return arabic
                    ? string.Create(inv, $"{now.Day} {ArabicMonths[now.Month - 1]} {now.Year}")
                    : string.Create(inv, $"{EnglishMonths[now.Month - 1]} {now.Day}, {now.Year}");
            case AutoVariable.HijriDate:
                var (day, month, year) = Hijri(now);
                return arabic
                    ? string.Create(inv, $"{day} {ArabicHijriMonths[month - 1]} {year} هـ")
                    : string.Create(inv, $"{day} {EnglishHijriMonths[month - 1]} {year} AH");
            case AutoVariable.Day:
                return (arabic ? ArabicDays : EnglishDays)[(int)now.DayOfWeek];
            default:
                var hour = now.Hour % 12 == 0 ? 12 : now.Hour % 12;
                var morning = now.Hour < 12;
                return string.Create(inv, $"{hour}:{now.Minute:00} ") + (arabic ? (morning ? "ص" : "م") : (morning ? "AM" : "PM"));
        }
    }

    // Umm al-Qura (Saudi Arabia's calendar), or the arithmetic Hijri calendar outside its range (1900–2077).
    private static (int Day, int Month, int Year) Hijri(DateTime date)
    {
        Calendar calendar = new UmAlQuraCalendar();
        if (date < calendar.MinSupportedDateTime || date > calendar.MaxSupportedDateTime)
        {
            calendar = new HijriCalendar();
        }

        return (calendar.GetDayOfMonth(date), calendar.GetMonth(date), calendar.GetYear(date));
    }

    // {{name}} or {name} starting at text[start]; braces and line breaks are not allowed inside, and a
    // brace right before or after (a typo such as {{name} or {{{name}}}) leaves the whole thing as text.
    private static bool TryVariable(string text, int start, out VariablePart part, out int length)
    {
        part = null!;
        length = 0;
        if (start > 0 && text[start - 1] == '{')
        {
            return false;
        }

        var isDouble = start + 1 < text.Length && text[start + 1] == '{';
        var nameStart = start + (isDouble ? 2 : 1);
        var close = text.IndexOf(isDouble ? "}}" : "}", nameStart, StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        var name = text[nameStart..close];
        if (name.IndexOfAny(['{', '}', '\n', '\r']) >= 0 || !IsValidName(name))
        {
            return false;
        }

        length = close + (isDouble ? 2 : 1) - start;
        if (start + length < text.Length && text[start + length] == '}')
        {
            return false;
        }

        var raw = text.Substring(start, length);
        var cleaned = Clean(name);
        if (!isDouble)
        {
            part = new VariablePart.Input(cleaned);
        }
        else if (AutoNames.FirstOrDefault(n => n.Arabic == cleaned || string.Equals(n.English, cleaned, StringComparison.OrdinalIgnoreCase)) is { Arabic: not null } known)
        {
            part = new VariablePart.Auto(known.Kind, raw);
        }
        else
        {
            part = new VariablePart.UnknownAuto(cleaned, raw);
        }

        return true;
    }

    private static string Clean(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
