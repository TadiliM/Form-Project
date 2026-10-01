using System.Globalization;

namespace backend.Models.Dtos;

public class CreateCheckoutSessionResponseDto
{
    public string CheckoutUrl {get; set;} = string.Empty;
}