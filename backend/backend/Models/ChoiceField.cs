namespace backend.Models;

public class ChoiceField : Field
{
    public List<string> Options { get; set; } = new();

    public override bool Validate(string value)
    {
        if (IsRequired && string.IsNullOrWhiteSpace(value)) return false;
        return Options.Contains(value);
    }
}
