namespace backend.Models;

public class FormResponse
{
    public Guid Id { get; set; }
    public Guid FormId { get; set; }
    public Form? Form { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public List<Answer> Answers { get; set; } = new();

    public bool Validate()
    {
        return Answers.Count > 0;
    }
}
