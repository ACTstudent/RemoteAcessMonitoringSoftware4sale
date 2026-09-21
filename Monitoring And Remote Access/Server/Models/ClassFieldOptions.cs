namespace Server.Models;

// Shared by the administrator and teacher create/edit forms. Keep saved values
// selectable even when they predate the standard choices.
public record ClassFieldOptions(string Id, string Name, string? Value, IEnumerable<string?> Existing)
{
    public IEnumerable<string> Choices()
    {
        IEnumerable<string> standard = Name switch
        {
            "AcademicYear" => Enumerable.Range(DateTime.Today.Year - 2, 8).Select(y => $"{y}-{y + 1}"),
            "GradeLevel" => new[] { "Kindergarten" }.Concat(Enumerable.Range(1, 6).Select(g => $"Grade {g}")),
            "Schedule" => new[] { "Mon–Fri", "Mon/Wed/Fri", "Tue/Thu", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" }
                .SelectMany(day => Enumerable.Range(7, 10).Select(hour =>
                    $"{day} {new TimeOnly(hour, 0):h:mm tt} – {new TimeOnly(hour + 1, 0):h:mm tt}")),
            _ => Array.Empty<string>()
        };
        return standard.Concat(Existing).Append(Value).Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
