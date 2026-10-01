using backend.Models.Dtos;
using backend.Models.Enums;
using backend.Services;
using Microsoft.EntityFrameworkCore;

namespace backend.Tests;

/// <summary>
/// Expected behavior of FormService — the business core of the application.
///
/// Each test describes an observable rule: what the creator gets, what a respondent
/// may submit, what is really kept in the database, and the refusals (type + message)
/// that controllers turn into HTTP responses.
/// No test depends on how the service is implemented internally.
/// </summary>
[Collection("postgres")]
public class FormServiceTests : ServiceTestBase
{
    public FormServiceTests(PostgresFixture postgres) : base(postgres) { }

    private FormService CreateService() => new(Context);

    // Creating a form

    [Fact]
    public async Task Create_WithATitleAndAValidField_ReturnsTheSummaryAndPersistsTheForm()
    {
        var user = await SeedUserAsync();

        var summary = await CreateService().CreateAsync(user.Id, FormRequest("Team survey", TextFieldDto("Name")));

        Assert.NotEqual(Guid.Empty, summary.Id);
        Assert.Equal("Team survey", summary.Title);
        Assert.False(string.IsNullOrWhiteSpace(summary.PublicUrlSlug));
        Assert.Equal(1, summary.FieldCount);
        Assert.Equal(0, summary.ResponseCount);

        var storedForm = await ReadAsync(db => db.Forms.SingleAsync(f => f.Id == summary.Id));
        Assert.Equal(user.Id, storedForm.UserId);
    }

    [Fact]
    public async Task Create_TitleWithAccentsAndPunctuation_ProducesAnAccentFreeWebSlug()
    {
        var user = await SeedUserAsync();

        var summary = await CreateService().CreateAsync(user.Id, FormRequest("École d'été 2026 !", TextFieldDto("Name")));

        // A slug usable in a URL: lowercase letters, digits and dashes only.
        Assert.Matches("^[a-z0-9-]+$", summary.PublicUrlSlug);
        Assert.StartsWith("ecole-d-ete-2026", summary.PublicUrlSlug);
    }

    [Fact]
    public async Task Create_TwoFormsWithTheSameTitle_HaveDistinctSlugsThatEachResolveToTheRightForm()
    {
        var user = await SeedUserAsync();
        var service = CreateService();

        var first = await service.CreateAsync(user.Id, FormRequest("My form", TextFieldDto("Name")));
        var second = await service.CreateAsync(user.Id, FormRequest("My form", TextFieldDto("Name")));

        Assert.NotEqual(first.PublicUrlSlug, second.PublicUrlSlug);

        var firstPublic = await service.GetPublicFormAsync(first.PublicUrlSlug);
        var secondPublic = await service.GetPublicFormAsync(second.PublicUrlSlug);

        Assert.Equal(first.Id, firstPublic.Id);
        Assert.Equal(second.Id, secondPublic.Id);
    }

    [Fact]
    public async Task Create_WithoutATitle_RefusesCreation()
    {
        var user = await SeedUserAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CreateAsync(user.Id, FormRequest("   ", TextFieldDto("Name"))));

        Assert.Equal("The form title is required.", exception.Message);
        Assert.Empty(await ReadAsync(db => db.Forms.ToListAsync()));
    }

    [Fact]
    public async Task Create_WithoutAnyField_RefusesCreation()
    {
        var user = await SeedUserAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CreateAsync(user.Id, FormRequest("Empty form")));

        Assert.Equal("A form must contain at least one field.", exception.Message);
        Assert.Empty(await ReadAsync(db => db.Forms.ToListAsync()));
    }

    [Fact]
    public async Task Create_WithAnUnknownUser_ThrowsNotFound()
    {
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService().CreateAsync(Guid.NewGuid(), FormRequest("Form", TextFieldDto("Name"))));

        Assert.Equal("User not found.", exception.Message);
    }

    [Fact]
    public async Task Create_FreePlan_AtTheFourthForm_RefusesWithTheLimitMessage()
    {
        var user = await SeedUserAsync(PlanType.Free);
        var service = CreateService();

        for (var i = 0; i < FormService.FreePlanMaxForms; i++)
            await service.CreateAsync(user.Id, FormRequest($"Form {i}", TextFieldDto("Name")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(user.Id, FormRequest("Extra form", TextFieldDto("Name"))));

        Assert.Equal(
            "Free plan limit reached (3 forms). Upgrade to the Pro plan to create more.",
            exception.Message);
        Assert.Equal(
            FormService.FreePlanMaxForms,
            await ReadAsync(db => db.Forms.CountAsync(f => f.UserId == user.Id)));
    }

    [Fact]
    public async Task Create_ProPlan_IsNotLimitedByTheFreePlan()
    {
        var user = await SeedUserAsync(PlanType.Pro);
        for (var i = 0; i < FormService.FreePlanMaxForms; i++)
            await SeedFormAsync(user.Id, $"Form {i}");

        var summary = await CreateService().CreateAsync(user.Id, FormRequest("Fourth form", TextFieldDto("Name")));

        Assert.NotEqual(Guid.Empty, summary.Id);
        Assert.Equal(4, await ReadAsync(db => db.Forms.CountAsync(f => f.UserId == user.Id)));
    }

    [Fact]
    public async Task Create_WithATextFieldWithoutMaxLength_AppliesTheDefaultOf500Characters()
    {
        var user = await SeedUserAsync();
        var service = CreateService();

        var summary = await service.CreateAsync(user.Id, FormRequest("Form", TextFieldDto("Name")));
        var detail = await service.GetMyFormAsync(user.Id, summary.Id);

        var field = Assert.Single(detail.Fields);
        Assert.Equal("text", field.Type);
        Assert.Equal(500, field.MaxLength);
        Assert.False(field.IsRequired);
    }

    [Fact]
    public async Task Create_WithATextFieldAndAnInvalidMaxLength_RefusesCreation()
    {
        var user = await SeedUserAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CreateAsync(user.Id, FormRequest("Form", TextFieldDto("Name", maxLength: 0))));

        Assert.Equal("MaxLength must be greater than 0.", exception.Message);
    }

    [Fact]
    public async Task Create_WithAChoiceFieldWithoutOptions_RefusesCreation()
    {
        var user = await SeedUserAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CreateAsync(user.Id, FormRequest("Form", ChoiceFieldDto("Color", new[] { "   ", "" }))));

        Assert.Equal("A choice field must offer at least one option.", exception.Message);
    }

    [Fact]
    public async Task Create_WithAChoiceField_KeepsTheProvidedOptionsWithoutEmptyValues()
    {
        var user = await SeedUserAsync();
        var service = CreateService();

        var summary = await service.CreateAsync(user.Id, FormRequest("Form",
            ChoiceFieldDto("Color", new[] { " Red ", "  ", "Blue" })));
        var field = Assert.Single((await service.GetMyFormAsync(user.Id, summary.Id)).Fields);

        Assert.Equal("choice", field.Type);
        Assert.Equal(new[] { "Red", "Blue" }, field.Options!);
    }

    [Fact]
    public async Task Create_WithANumberFieldWithoutBounds_AppliesZeroAndMaximumDefaults()
    {
        var user = await SeedUserAsync();
        var service = CreateService();

        var summary = await service.CreateAsync(user.Id, FormRequest("Form", NumberFieldDto("Age")));
        var field = Assert.Single((await service.GetMyFormAsync(user.Id, summary.Id)).Fields);

        Assert.Equal("number", field.Type);
        Assert.Equal(0m, field.Min);
        Assert.Equal(decimal.MaxValue, field.Max);
    }

    [Fact]
    public async Task Create_WithANumberFieldWhoseMinimumExceedsTheMaximum_RefusesCreation()
    {
        var user = await SeedUserAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CreateAsync(user.Id, FormRequest("Form", NumberFieldDto("Age", min: 10, max: 5))));

        Assert.Equal("Min cannot be greater than Max.", exception.Message);
    }

    [Fact]
    public async Task Create_WithAnUnknownFieldType_RefusesCreation()
    {
        var user = await SeedUserAsync();
        var unknownField = new FieldRequestDto { Type = "date", Label = "Date of birth" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CreateAsync(user.Id, FormRequest("Form", unknownField)));

        Assert.Equal("Unknown field type: \"date\". Accepted values: text, choice, number.", exception.Message);
    }

    [Fact]
    public async Task Create_WithAFieldWithoutALabel_RefusesCreation()
    {
        var user = await SeedUserAsync();
        var fieldWithoutLabel = new FieldRequestDto { Type = "text", Label = "  " };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CreateAsync(user.Id, FormRequest("Form", fieldWithoutLabel)));

        Assert.Equal("Every field must have a label.", exception.Message);
    }

    // Public read side (respondent, by slug)

    [Fact]
    public async Task GetPublicForm_WithAKnownSlug_ReturnsTheFieldsInTheirOrder()
    {
        var user = await SeedUserAsync();
        var service = CreateService();

        var summary = await service.CreateAsync(user.Id, FormRequest("Survey",
            TextFieldDto("Name", order: 0),
            ChoiceFieldDto("Color", new[] { "Red" }, order: 1),
            NumberFieldDto("Age", order: 2)));

        var publicForm = await service.GetPublicFormAsync(summary.PublicUrlSlug);

        Assert.Equal(summary.Id, publicForm.Id);
        Assert.Equal("Survey", publicForm.Title);
        Assert.Equal(new[] { "Name", "Color", "Age" }, publicForm.Fields.Select(f => f.Label));
        Assert.Equal(new[] { "text", "choice", "number" }, publicForm.Fields.Select(f => f.Type));
    }

    [Fact]
    public async Task GetPublicForm_WithAnUnknownSlug_ThrowsNotFound()
    {
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService().GetPublicFormAsync("slug-that-does-not-exist"));

        Assert.Equal("Form not found.", exception.Message);
    }

    // Listing and detail (creator side)

    [Fact]
    public async Task GetMyForms_ReturnsOnlyOwnFormsWithExactCounters()
    {
        var owner = await SeedUserAsync(PlanType.Pro, email: "owner@test.dev");
        var other = await SeedUserAsync(PlanType.Pro, email: "other@test.dev");

        var form = await SeedFormAsync(owner.Id, title: "With one response");
        await SeedFormAsync(other.Id, title: "Owned by the other user");
        await SeedResponseAsync(form.Id, new[] { (form.Fields[0].Id, "Alice") });

        var summaries = await CreateService().GetMyFormsAsync(owner.Id);

        var summary = Assert.Single(summaries);
        Assert.Equal("With one response", summary.Title);
        Assert.Equal(1, summary.FieldCount);
        Assert.Equal(1, summary.ResponseCount);
    }

    [Fact]
    public async Task GetMyForms_OrdersFormsFromMostRecentToOldest()
    {
        var user = await SeedUserAsync();
        var older = await SeedFormAsync(user.Id, "Older", createdAt: DateTime.UtcNow.AddDays(-2));
        var recent = await SeedFormAsync(user.Id, "Recent", createdAt: DateTime.UtcNow);

        var summaries = await CreateService().GetMyFormsAsync(user.Id);

        Assert.Equal(new[] { recent.Id, older.Id }, summaries.Select(s => s.Id));
    }

    [Fact]
    public async Task GetMyForm_WithAnUnknownId_ThrowsNotFound()
    {
        var user = await SeedUserAsync();

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService().GetMyFormAsync(user.Id, Guid.NewGuid()));

        Assert.Equal("Form not found.", exception.Message);
    }

    [Fact]
    public async Task GetMyForm_FormOfAnotherUser_ThrowsNotFound()
    {
        var owner = await SeedUserAsync(email: "owner@test.dev");
        var intruder = await SeedUserAsync(email: "intruder@test.dev");
        var form = await SeedFormAsync(owner.Id);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService().GetMyFormAsync(intruder.Id, form.Id));

        // Same message as for an unknown id: the existence of the form is not revealed.
        Assert.Equal("Form not found.", exception.Message);
    }

    [Fact]
    public async Task GetResponses_FormOfAnotherUser_ThrowsNotFound()
    {
        var owner = await SeedUserAsync(email: "owner@test.dev");
        var intruder = await SeedUserAsync(email: "intruder@test.dev");
        var form = await SeedFormAsync(owner.Id);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService().GetResponsesAsync(intruder.Id, form.Id));

        Assert.Equal("Form not found.", exception.Message);
    }

    [Fact]
    public async Task GetResponses_OrdersResponsesFromNewestToOldestAndShowsLabels()
    {
        var user = await SeedUserAsync();
        var form = await SeedFormAsync(user.Id, fieldLabel: "Name");
        var field = form.Fields[0];

        var older = await SeedResponseAsync(form.Id, new[] { (field.Id, "Alice") }, submittedAt: DateTime.UtcNow.AddHours(-2));
        var newer = await SeedResponseAsync(form.Id, new[] { (field.Id, "Bob") }, submittedAt: DateTime.UtcNow);

        var responses = await CreateService().GetResponsesAsync(user.Id, form.Id);

        Assert.Equal(new[] { newer.Id, older.Id }, responses.Select(r => r.Id));
        Assert.Equal("Name", responses[0].Answers.Single().Label);
        Assert.Equal("Bob", responses[0].Answers.Single().Value);
    }

    // Receiving a response (public)

    [Fact]
    public async Task SubmitResponse_WithAValidAnswer_SavesTheResponseAndReturnsItsId()
    {
        var user = await SeedUserAsync();
        var service = CreateService();
        var summary = await service.CreateAsync(user.Id, FormRequest("Survey", TextFieldDto("Name", isRequired: true)));
        var field = Assert.Single((await service.GetMyFormAsync(user.Id, summary.Id)).Fields);

        var responseId = await service.SubmitResponseAsync(summary.PublicUrlSlug, ResponseRequest((field.Id, "Alice")));

        Assert.NotEqual(Guid.Empty, responseId);

        var responses = await service.GetResponsesAsync(user.Id, summary.Id);
        var response = Assert.Single(responses);
        Assert.Equal(responseId, response.Id);
        var fieldAnswer = Assert.Single(response.Answers);
        Assert.Equal(field.Id, fieldAnswer.FieldId);
        Assert.Equal("Name", fieldAnswer.Label);
        Assert.Equal("Alice", fieldAnswer.Value);
    }

    [Fact]
    public async Task SubmitResponse_WithAnUnknownSlug_ThrowsNotFound()
    {
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService().SubmitResponseAsync("slug-that-does-not-exist", ResponseRequest((Guid.NewGuid(), "value"))));

        Assert.Equal("Form not found.", exception.Message);
    }

    [Fact]
    public async Task SubmitResponse_WithoutAnAnswerForARequiredField_RefusesAndNamesTheField()
    {
        var user = await SeedUserAsync();
        var form = await SeedFormAsync(user.Id, fieldLabel: "Name", fieldRequired: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().SubmitResponseAsync(form.PublicUrlSlug, ResponseRequest()));

        Assert.Equal("Field \"Name\" is required.", exception.Message);
        Assert.Empty(await ReadAsync(db => db.FormResponses.ToListAsync()));
    }

    [Fact]
    public async Task SubmitResponse_WithAFieldThatDoesNotBelongToTheForm_RefusesTheResponse()
    {
        var user = await SeedUserAsync();
        var form = await SeedFormAsync(user.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().SubmitResponseAsync(
                form.PublicUrlSlug,
                ResponseRequest((Guid.NewGuid(), "value"))));

        Assert.Equal("A response references a field that does not belong to this form.", exception.Message);
        Assert.Empty(await ReadAsync(db => db.FormResponses.ToListAsync()));
    }

    [Fact]
    public async Task SubmitResponse_WithAValueOutsideTheOptions_RefusesTheResponse()
    {
        var user = await SeedUserAsync();
        var service = CreateService();
        var summary = await service.CreateAsync(user.Id, FormRequest("Survey",
            ChoiceFieldDto("Color", new[] { "Red", "Blue" }, isRequired: true, order: 0)));
        var field = Assert.Single((await service.GetMyFormAsync(user.Id, summary.Id)).Fields);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SubmitResponseAsync(summary.PublicUrlSlug, ResponseRequest((field.Id, "Green"))));

        Assert.Equal("Invalid value for field \"Color\".", exception.Message);
    }

    [Fact]
    public async Task SubmitResponse_WithANumberOutsideTheBounds_RefusesTheResponse()
    {
        var user = await SeedUserAsync();
        var service = CreateService();
        var summary = await service.CreateAsync(user.Id, FormRequest("Survey",
            NumberFieldDto("Age", min: 0, max: 120, isRequired: true, order: 0)));
        var field = Assert.Single((await service.GetMyFormAsync(user.Id, summary.Id)).Fields);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SubmitResponseAsync(summary.PublicUrlSlug, ResponseRequest((field.Id, "200"))));

        Assert.Equal("Invalid value for field \"Age\".", exception.Message);
    }

    [Fact]
    public async Task SubmitResponse_WithTextLongerThanTheLimit_RefusesTheResponse()
    {
        var user = await SeedUserAsync();
        var service = CreateService();
        var summary = await service.CreateAsync(user.Id, FormRequest("Survey",
            TextFieldDto("Name", maxLength: 5, order: 0)));
        var field = Assert.Single((await service.GetMyFormAsync(user.Id, summary.Id)).Fields);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SubmitResponseAsync(summary.PublicUrlSlug, ResponseRequest((field.Id, "far too long"))));

        Assert.Equal("Invalid value for field \"Name\".", exception.Message);
    }

    [Fact]
    public async Task SubmitResponse_WithEmptyOptionalFields_DoesNotCreateAPointlessAnswer()
    {
        var user = await SeedUserAsync();
        var service = CreateService();
        var summary = await service.CreateAsync(user.Id, FormRequest("Survey",
            TextFieldDto("Name", isRequired: true, order: 0),
            TextFieldDto("Comment", order: 1)));

        var detail = await service.GetMyFormAsync(user.Id, summary.Id);
        var nameField = detail.Fields.Single(f => f.Label == "Name");
        var commentField = detail.Fields.Single(f => f.Label == "Comment");

        await service.SubmitResponseAsync(summary.PublicUrlSlug,
            ResponseRequest((nameField.Id, "Alice"), (commentField.Id, "   ")));

        var responses = await service.GetResponsesAsync(user.Id, summary.Id);
        var storedResponse = Assert.Single(responses);
        var onlyAnswer = Assert.Single(storedResponse.Answers);
        Assert.Equal("Name", onlyAnswer.Label);
        Assert.Equal("Alice", onlyAnswer.Value);
    }

    [Fact]
    public async Task SubmitResponse_WithoutAnyAnswerToSave_RefusesTheResponse()
    {
        var user = await SeedUserAsync();
        var service = CreateService();
        var summary = await service.CreateAsync(user.Id, FormRequest("Survey", TextFieldDto("Comment")));
        var field = Assert.Single((await service.GetMyFormAsync(user.Id, summary.Id)).Fields);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SubmitResponseAsync(summary.PublicUrlSlug, ResponseRequest((field.Id, "   "))));

        Assert.Equal("The form does not contain any answer to save.", exception.Message);
    }

    // Update

    [Fact]
    public async Task Update_WithANewTitleAndNewFields_ReplacesTheExistingFieldsCompletely()
    {
        var user = await SeedUserAsync();
        var form = await SeedFormAsync(user.Id, title: "Initial title", fieldLabel: "Old field");

        var detail = await CreateService().UpdateAsync(user.Id, form.Id, new UpdateFormRequest
        {
            Title = "Updated title",
            Fields =
            [
                ChoiceFieldDto("Color", new[] { "Red", "Blue" }, order: 0),
                TextFieldDto("Comment", order: 1)
            ]
        });

        Assert.Equal("Updated title", detail.Title);
        Assert.Equal(new[] { "Color", "Comment" }, detail.Fields.OrderBy(f => f.Order).Select(f => f.Label));
        Assert.DoesNotContain(detail.Fields, f => f.Label == "Old field");

        var storedFields = await ReadAsync(db => db.Fields.Where(f => f.FormId == form.Id).ToListAsync());
        Assert.Equal(2, storedFields.Count);
    }

    [Fact]
    public async Task Update_WithoutATitle_RefusesAndChangesNothing()
    {
        var user = await SeedUserAsync();
        var form = await SeedFormAsync(user.Id, title: "Initial title");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().UpdateAsync(user.Id, form.Id, new UpdateFormRequest
            {
                Title = "  ",
                Fields = [TextFieldDto("Name")]
            }));

        Assert.Equal("The form title is required.", exception.Message);
        Assert.Equal("Initial title", await ReadAsync(db => db.Forms.Where(f => f.Id == form.Id).Select(f => f.Title).SingleAsync()));
    }

    [Fact]
    public async Task Update_WithoutAnyField_RefusesAndChangesNothing()
    {
        var user = await SeedUserAsync();
        var form = await SeedFormAsync(user.Id, title: "Initial title");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().UpdateAsync(user.Id, form.Id, new UpdateFormRequest { Title = "New title" }));

        Assert.Equal("A form must contain at least one field.", exception.Message);
        Assert.Equal("Initial title", await ReadAsync(db => db.Forms.Where(f => f.Id == form.Id).Select(f => f.Title).SingleAsync()));
    }

    [Fact]
    public async Task Update_FormThatAlreadyReceivedAResponse_RefusesFieldChanges()
    {
        var user = await SeedUserAsync();
        var form = await SeedFormAsync(user.Id, title: "Initial title");
        await SeedResponseAsync(form.Id, new[] { (form.Fields[0].Id, "Alice") });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().UpdateAsync(user.Id, form.Id, new UpdateFormRequest
            {
                Title = "New title",
                Fields = [TextFieldDto("Other field")]
            }));

        Assert.Equal(
            "This form has already received responses: its fields can no longer be modified.",
            exception.Message);
    }

    [Fact]
    public async Task Update_FormOfAnotherUser_ThrowsNotFound()
    {
        var owner = await SeedUserAsync(email: "owner@test.dev");
        var intruder = await SeedUserAsync(email: "intruder@test.dev");
        var form = await SeedFormAsync(owner.Id);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService().UpdateAsync(intruder.Id, form.Id, new UpdateFormRequest
            {
                Title = "Hijack",
                Fields = [TextFieldDto("Name")]
            }));

        Assert.Equal("Form not found.", exception.Message);
    }

    // Delete

    [Fact]
    public async Task Delete_FormWithResponses_DeletesTheFormAndEverythingDependingOnIt()
    {
        // Answer.FieldId uses ON DELETE RESTRICT, so a naive delete would be rejected by
        // the database. The deletion must succeed and leave no field, response or answer.
        var user = await SeedUserAsync();
        var form = await SeedFormAsync(user.Id);
        await SeedResponseAsync(form.Id, new[] { (form.Fields[0].Id, "Alice") });

        await CreateService().DeleteAsync(user.Id, form.Id);

        Assert.Empty(await ReadAsync(db => db.Forms.Where(f => f.Id == form.Id).ToListAsync()));
        Assert.Empty(await ReadAsync(db => db.Fields.Where(f => f.FormId == form.Id).ToListAsync()));
        Assert.Empty(await ReadAsync(db => db.FormResponses.Where(r => r.FormId == form.Id).ToListAsync()));
        Assert.Empty(await ReadAsync(db => db.Answers.ToListAsync()));
    }

    [Fact]
    public async Task Delete_FormWithoutResponses_DeletesTheFormAndItsFields()
    {
        var user = await SeedUserAsync();
        var form = await SeedFormAsync(user.Id);

        await CreateService().DeleteAsync(user.Id, form.Id);

        Assert.Empty(await ReadAsync(db => db.Forms.Where(f => f.Id == form.Id).ToListAsync()));
        Assert.Empty(await ReadAsync(db => db.Fields.Where(f => f.FormId == form.Id).ToListAsync()));
    }

    [Fact]
    public async Task Delete_FormOfAnotherUser_ThrowsAndDeletesNothing()
    {
        var owner = await SeedUserAsync(email: "owner@test.dev");
        var intruder = await SeedUserAsync(email: "intruder@test.dev");
        var form = await SeedFormAsync(owner.Id);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService().DeleteAsync(intruder.Id, form.Id));

        Assert.Equal("Form not found.", exception.Message);
        Assert.NotNull(await ReadAsync(db => db.Forms.FirstOrDefaultAsync(f => f.Id == form.Id)));
    }
}
