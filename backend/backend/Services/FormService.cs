using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Models;
using backend.Models.Dtos;
using backend.Models.Enums;

namespace backend.Services;

public class FormService : IFormService
{
    /// <summary>Maximum number of forms allowed for a Free plan account.</summary>
    public const int FreePlanMaxForms = 3;

    /// <summary>Maximum length of the title part of the public slug.</summary>
    private const int MaxSlugBaseLength = 60;

    private readonly AppDbContext _context;

    public FormService(AppDbContext context)
    {
        _context = context;
    }

    // Creator side (authenticated user)

    public async Task<FormSummaryDto> CreateAsync(Guid userId, CreateFormRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("The form title is required.");

        if (request.Fields.Count == 0)
            throw new InvalidOperationException("A form must contain at least one field.");

        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        // The plan is read from the database rather than from the JWT: the token's
        // "planType" claim is captured at login and can be stale after a plan change.
        if (user.PlanType == PlanType.Free)
        {
            var existingForms = await _context.Forms.CountAsync(f => f.UserId == userId);
            if (existingForms >= FreePlanMaxForms)
                throw new InvalidOperationException(
                    $"Free plan limit reached ({FreePlanMaxForms} forms). " +
                    "Upgrade to the Pro plan to create more.");
        }

        var form = new Form
        {
            UserId = userId,
            Title = request.Title.Trim(),
            PublicUrlSlug = BuildSlug(request.Title)
        };

        // Fields go through the model method (Form.AddField): business rules stay in the model.
        foreach (var fieldDto in request.Fields)
            form.AddField(BuildField(fieldDto));

        if (!form.Validate())
            throw new InvalidOperationException("A form must contain at least one valid field.");

        // Explicit Add: the form is new and must be tracked as an insert, otherwise
        // SaveChanges would have no entity to persist.
        _context.Forms.Add(form);
        await _context.SaveChangesAsync();

        return new FormSummaryDto
        {
            Id = form.Id,
            Title = form.Title,
            PublicUrlSlug = form.PublicUrlSlug,
            CreatedAt = form.CreatedAt,
            FieldCount = form.Fields.Count,
            ResponseCount = 0
        };
    }

    public async Task<List<FormSummaryDto>> GetMyFormsAsync(Guid userId)
    {
        // Projection translated to SQL: FieldCount and ResponseCount become COUNT subqueries,
        // so there is NO extra query per form (no N+1).
        return await _context.Forms
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FormSummaryDto
            {
                Id = f.Id,
                Title = f.Title,
                PublicUrlSlug = f.PublicUrlSlug,
                CreatedAt = f.CreatedAt,
                FieldCount = f.Fields.Count,
                ResponseCount = f.Responses.Count
            })
            .ToListAsync();
    }

    public async Task<FormDetailDto> GetMyFormAsync(Guid userId, Guid formId)
    {
        var form = await LoadOwnedFormAsync(userId, formId);
        return await ToDetailDtoAsync(form);
    }

    public async Task<FormDetailDto> UpdateAsync(Guid userId, Guid formId, UpdateFormRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("The form title is required.");

        if (request.Fields.Count == 0)
            throw new InvalidOperationException("A form must contain at least one field.");

        var form = await LoadOwnedFormAsync(userId, formId);

        // Once a form has received a response, its fields are frozen: a field with
        // existing answers cannot be deleted (Answer.FieldId uses ON DELETE RESTRICT,
        // see AppDbContext), and editing one would change the meaning of stored answers.
        var hasResponses = await _context.FormResponses.AnyAsync(r => r.FormId == form.Id);
        if (hasResponses)
            throw new InvalidOperationException(
                "This form has already received responses: its fields can no longer be modified.");

        form.Title = request.Title.Trim();

        // Full field replacement (safe here: no response exists yet).
        // RemoveRange marks the old fields for DELETE, Clear() empties the navigation list,
        // then the new fields are added and will be INSERTed.
        _context.Fields.RemoveRange(form.Fields);
        form.Fields.Clear();

        foreach (var fieldDto in request.Fields)
            form.AddField(BuildField(fieldDto));

        await _context.SaveChangesAsync();

        return await ToDetailDtoAsync(form);
    }

    public async Task DeleteAsync(Guid userId, Guid formId)
    {
        var form = await LoadOwnedFormAsync(userId, formId);

        // Why two SaveChanges? Because of Answer.FieldId and ON DELETE RESTRICT:
        // PostgreSQL refuses to delete a field still referenced by an answer.
        // So responses (and their answers, in cascade) are deleted first, then the form
        // (its fields follow in cascade).
        var responses = await _context.FormResponses
            .Where(r => r.FormId == form.Id)
            .ToListAsync();

        _context.FormResponses.RemoveRange(responses);
        await _context.SaveChangesAsync();

        _context.Forms.Remove(form);
        await _context.SaveChangesAsync();
    }

    public async Task<List<FormResponseDto>> GetResponsesAsync(Guid userId, Guid formId)
    {
        var form = await LoadOwnedFormAsync(userId, formId);

        // Field id -> label map, to display answers in a readable way.
        var labels = form.Fields.ToDictionary(f => f.Id, f => f.Label);

        var responses = await _context.FormResponses
            .Include(r => r.Answers)               // loads the answers in a single query
            .Where(r => r.FormId == form.Id)
            .OrderByDescending(r => r.SubmittedAt) // most recent first
            .ToListAsync();

        return responses.Select(r => new FormResponseDto
        {
            Id = r.Id,
            SubmittedAt = r.SubmittedAt,
            Answers = r.Answers.Select(a => new AnswerResponseDto
            {
                FieldId = a.FieldId,
                Label = labels.TryGetValue(a.FieldId, out var label) ? label : "(deleted field)",
                Value = a.Value
            }).ToList()
        }).ToList();
    }

    // Respondent side (public, no account)

    public async Task<FormDetailDto> GetPublicFormAsync(string slug)
    {
        var form = await _context.Forms
            .Include(f => f.Fields)
            .FirstOrDefaultAsync(f => f.PublicUrlSlug == slug)
            ?? throw new KeyNotFoundException("Form not found.");

        return await ToDetailDtoAsync(form);
    }

    public async Task<Guid> SubmitResponseAsync(string slug, SubmitResponseRequest request)
    {
        var form = await _context.Forms
            .Include(f => f.Fields)
            .FirstOrDefaultAsync(f => f.PublicUrlSlug == slug)
            ?? throw new KeyNotFoundException("Form not found.");

        // Security: every answer must target a field of THIS form.
        var knownFieldIds = form.Fields.Select(f => f.Id).ToHashSet();
        if (request.Answers.Any(a => !knownFieldIds.Contains(a.FieldId)))
            throw new InvalidOperationException(
                "A response references a field that does not belong to this form.");

        var response = new FormResponse { FormId = form.Id };

        foreach (var field in form.Fields)
        {
            var submitted = request.Answers.FirstOrDefault(a => a.FieldId == field.Id);

            if (submitted is null)
            {
                if (field.IsRequired)
                    throw new InvalidOperationException($"Field \"{field.Label}\" is required.");

                continue;   // optional field left empty: store nothing
            }

            var value = submitted.Value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(value) && !field.IsRequired)
                continue;   // optional left blank: no pointless Answer row

            // field.Validate is ABSTRACT: at runtime the override of the concrete type
            // (TextField, ChoiceField, NumberField) runs. That is polymorphism, and it is
            // what applies each type's own validation rules.
            if (!field.Validate(value))
                throw new InvalidOperationException($"Invalid value for field \"{field.Label}\".");

            response.Answers.Add(new Answer
            {
                FieldId = field.Id,
                Value = value
            });
        }

        if (!response.Validate())
            throw new InvalidOperationException("The form does not contain any answer to save.");

        _context.FormResponses.Add(response);   // explicit Add => INSERT
        await _context.SaveChangesAsync();

        return response.Id;
    }

    // Private helpers

    /// <summary>
    /// Loads a form and checks that it really belongs to this user.
    /// A form owned by somebody else also raises "not found" (not "forbidden"):
    /// this avoids revealing that other users' forms exist.
    /// </summary>
    private async Task<Form> LoadOwnedFormAsync(Guid userId, Guid formId)
    {
        var form = await _context.Forms
            .Include(f => f.Fields)
            .FirstOrDefaultAsync(f => f.Id == formId);

        if (form is null || form.UserId != userId)
            throw new KeyNotFoundException("Form not found.");

        return form;
    }

    private async Task<FormDetailDto> ToDetailDtoAsync(Form form)
    {
        var responseCount = await _context.FormResponses.CountAsync(r => r.FormId == form.Id);

        return new FormDetailDto
        {
            Id = form.Id,
            Title = form.Title,
            PublicUrlSlug = form.PublicUrlSlug,
            CreatedAt = form.CreatedAt,
            ResponseCount = responseCount,
            Fields = form.Fields
                .OrderBy(f => f.Order)
                .Select(ToFieldDto)
                .ToList()
        };
    }

    /// <summary>Maps a field entity to its DTO: the concrete type decides which properties are filled.</summary>
    private static FieldResponseDto ToFieldDto(Field field)
    {
        var dto = new FieldResponseDto
        {
            Id = field.Id,
            Label = field.Label,
            IsRequired = field.IsRequired,
            Order = field.Order
        };

        switch (field)
        {
            case TextField text:
                dto.Type = "text";
                dto.MaxLength = text.MaxLength;
                break;

            case ChoiceField choice:
                dto.Type = "choice";
                dto.Options = choice.Options;
                break;

            case NumberField number:
                dto.Type = "number";
                dto.Min = number.Min;
                dto.Max = number.Max;
                break;
        }

        return dto;
    }

    /// <summary>Builds the right Field subclass from the DTO (and validates its configuration).</summary>
    private static Field BuildField(FieldRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Label))
            throw new InvalidOperationException("Every field must have a label.");

        var type = dto.Type.Trim().ToLowerInvariant();
        Field field;

        if (type == "text")
        {
            var maxLength = dto.MaxLength ?? 500;
            if (maxLength <= 0)
                throw new InvalidOperationException("MaxLength must be greater than 0.");

            field = new TextField
            {
                Type = FieldType.Text,
                MaxLength = maxLength
            };
        }
        else if (type == "choice")
        {
            var options = (dto.Options ?? new List<string>())
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim())
                .ToList();

            if (options.Count == 0)
                throw new InvalidOperationException(
                    "A choice field must offer at least one option.");

            field = new ChoiceField
            {
                Type = FieldType.Choice,
                Options = options
            };
        }
        else if (type == "number")
        {
            var min = dto.Min ?? 0;
            var max = dto.Max ?? decimal.MaxValue;
            if (min > max)
                throw new InvalidOperationException("Min cannot be greater than Max.");

            field = new NumberField
            {
                Type = FieldType.Number,
                Min = min,
                Max = max
            };
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown field type: \"{dto.Type}\". Accepted values: text, choice, number.");
        }

        // Properties shared by every field (declared on the base Field class)
        field.Label = dto.Label.Trim();
        field.IsRequired = dto.IsRequired;
        field.Order = dto.Order;

        return field;
    }

    /// <summary>
    /// Builds the public slug from the title: accents stripped, spaces turned into dashes,
    /// then a random 8-character suffix so two identical titles never collide.
    /// Example: "My great form!" -> "my-great-form-3f9a12cd".
    /// </summary>
    private static string BuildSlug(string title)
    {
        // FormD splits letters from their accents ("é" becomes "e" + a combining accent),
        // which makes it possible to drop the accents right after.
        var normalized = title.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }

        var slug = builder.ToString().Trim('-');
        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");

        if (slug.Length == 0)
            slug = "form";

        if (slug.Length > MaxSlugBaseLength)
            slug = slug[..MaxSlugBaseLength].TrimEnd('-');

        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{slug}-{suffix}";
    }
}
