using backend.Models.Dtos;

namespace backend.Services;

/// <summary>
/// Contract for the forms business logic.
/// Keeping the interface separate lets the controller depend on the contract only
/// (and lets ASP.NET inject the implementation, see Program.cs).
/// </summary>
public interface IFormService
{
    /// <summary>Creates a form with its fields. Enforces the Free plan limit.</summary>
    Task<FormSummaryDto> CreateAsync(Guid userId, CreateFormRequest request);

    /// <summary>Lists this user's forms (with field and response counts).</summary>
    Task<List<FormSummaryDto>> GetMyFormsAsync(Guid userId);

    /// <summary>Returns one of the caller's forms. Throws if the form does not belong to them.</summary>
    Task<FormDetailDto> GetMyFormAsync(Guid userId, Guid formId);

    /// <summary>Updates the title and/or the fields (fields are frozen once a response has been received).</summary>
    Task<FormDetailDto> UpdateAsync(Guid userId, Guid formId, UpdateFormRequest request);

    /// <summary>Deletes the form and everything that depends on it (fields, responses).</summary>
    Task DeleteAsync(Guid userId, Guid formId);

    /// <summary>Lists the responses received by one of the caller's forms.</summary>
    Task<List<FormResponseDto>> GetResponsesAsync(Guid userId, Guid formId);

    /// <summary>Public definition of a form, looked up by its sharing slug.</summary>
    Task<FormDetailDto> GetPublicFormAsync(string slug);

    /// <summary>Stores a response submitted by an anonymous respondent. Returns the created response Id.</summary>
    Task<Guid> SubmitResponseAsync(string slug, SubmitResponseRequest request);
}
