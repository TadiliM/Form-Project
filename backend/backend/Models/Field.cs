using backend.Models.Enums;

namespace backend.Models;

public abstract class Field
{
    public Guid Id { get; set; }
    public Guid FormId { get; set; }
    public Form? Form { get; set; }

    public string Label { get; set; } = string.Empty;
    public FieldType Type { get; set; }
    public bool IsRequired { get; set; }
    public int Order { get; set; }

    /// <summary>Validates the raw submitted value against this concrete field type's own rules.</summary>
    public abstract bool Validate(string value);
}
