using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Models.Dtos;
using backend.Services;

namespace backend.Controllers;

[ApiController]
[Route("api/forms")]
[Authorize]  public class FormsController : ControllerBase
{
    private readonly IFormService _formService;

    public FormsController(IFormService formService)
    {
        _formService = formService;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // Creator endpoints (JWT required)

    /// <summary>Creates a form with its fields.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateFormRequest request)
    {
        try
        {
            var form = await _formService.CreateAsync(CurrentUserId, request);
            // 201 + Location header pointing to GET /api/forms/{id}
            return CreatedAtAction(nameof(GetById), new { id = form.Id }, form);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>Lists my forms (without their fields: see the detail endpoint).</summary>
    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var forms = await _formService.GetMyFormsAsync(CurrentUserId);
        return Ok(forms);
    }

    /// <summary>Detail of one of my forms, including its fields.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            return Ok(await _formService.GetMyFormAsync(CurrentUserId, id));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>Updates the title and fields (not allowed once responses exist).</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateFormRequest request)
    {
        try
        {
            return Ok(await _formService.UpdateAsync(CurrentUserId, id, request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>Deletes a form and everything that depends on it.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _formService.DeleteAsync(CurrentUserId, id);
            return NoContent();   // 204: deleted, nothing to return
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>All the responses received by one of my forms.</summary>
    [HttpGet("{id:guid}/responses")]
    public async Task<IActionResult> GetResponses(Guid id)
    {
        try
        {
            return Ok(await _formService.GetResponsesAsync(CurrentUserId, id));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // Public endpoints (no JWT: these are the respondents)

    /// <summary>
    /// Definition of the form to fill in, looked up by its sharing slug.
    /// This is the URL to send to respondents: GET /api/forms/public/{slug}
    /// </summary>
    [AllowAnonymous]
    [HttpGet("public/{slug}")]
    public async Task<IActionResult> GetPublic(string slug)
    {
        try
        {
            return Ok(await _formService.GetPublicFormAsync(slug));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>Receives a response: anyone can answer, without an account.</summary>
    [AllowAnonymous]
    [HttpPost("public/{slug}/responses")]
    public async Task<IActionResult> SubmitResponse(string slug, SubmitResponseRequest request)
    {
        try
        {
            var responseId = await _formService.SubmitResponseAsync(slug, request);
            return StatusCode(StatusCodes.Status201Created, new { id = responseId });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
