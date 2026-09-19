namespace backend.Models;

public class TextField : Field
{
    public int MaxLength { get; set; }

    public override bool Validate(string value)
    {
        if (IsRequired && string.IsNullOrWhiteSpace(value)) return false;
        return value.Length <= MaxLength;
    }
}
