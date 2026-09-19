namespace backend.Models;

public class Answer
{
    public Guid Id { get; set; }
    public Guid FormResponseId { get; set; }
    public FormResponse? FormResponse { get; set; }

    public Guid FieldId { get; set; }
    public Field? Field { get; set; }

    public string Value { get; set; } = string.Empty;
}
