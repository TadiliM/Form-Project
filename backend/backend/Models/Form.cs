namespace backend.Models;

public class Form
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Title { get; set; } = string.Empty;
    public string PublicUrlSlug { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Field> Fields { get; set; } = new();
    public List<FormResponse> Responses { get; set; } = new();

    public void AddField(Field field)
    {
        Fields.Add(field);
    }

    public void RemoveField(Guid fieldId)
    {
        Fields.RemoveAll(f => f.Id == fieldId);
    }

    public bool Validate()
    {
        return Fields.Count > 0;
    }
}