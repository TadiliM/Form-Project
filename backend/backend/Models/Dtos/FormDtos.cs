namespace backend.Models.Dtos;

// Input DTOs: what the client sends to the API

/// <summary>
/// A field as sent by the client. A single DTO covers the three field types:
/// <see cref="Type"/> ("text" | "choice" | "number") decides which optional
/// properties (MaxLength / Options / Min / Max) are meaningful.
/// This keeps the JSON simpler than sending differently shaped objects.
/// </summary>
public class FieldRequestDto
{
    public string Type { get; set; } = "text";
    public string Label { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public int Order { get; set; }

    // Only used by "text" fields
    public int? MaxLength { get; set; }

    // Only used by "choice" fields
    public List<string>? Options { get; set; }

    // Only used by "number" fields
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
}

public class CreateFormRequest
{
    public string Title { get; set; } = string.Empty;
    public List<FieldRequestDto> Fields { get; set; } = new();
}

public class UpdateFormRequest
{
    public string Title { get; set; } = string.Empty;
    public List<FieldRequestDto> Fields { get; set; } = new();
}

/// <summary>An answer to one field: the field is referenced by its Id (returned by the public API).</summary>
public class AnswerRequestDto
{
    public Guid FieldId { get; set; }
    public string Value { get; set; } = string.Empty;
}

public class SubmitResponseRequest
{
    public List<AnswerRequestDto> Answers { get; set; } = new();
}

// Output DTOs: what the API returns to the client

public class FieldResponseDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public int Order { get; set; }
    public int? MaxLength { get; set; }
    public List<string>? Options { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
}

/// <summary>Lightweight form version for lists (fields are not detailed).</summary>
public class FormSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PublicUrlSlug { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int FieldCount { get; set; }
    public int ResponseCount { get; set; }
}

/// <summary>Detailed version, including the field list (used to render the form).</summary>
public class FormDetailDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PublicUrlSlug { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int ResponseCount { get; set; }
    public List<FieldResponseDto> Fields { get; set; } = new();
}

public class AnswerResponseDto
{
    public Guid FieldId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class FormResponseDto
{
    public Guid Id { get; set; }
    public DateTime SubmittedAt { get; set; }
    public List<AnswerResponseDto> Answers { get; set; } = new();
}
