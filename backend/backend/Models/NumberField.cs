namespace backend.Models;

public class NumberField : Field
{
    public decimal Min { get; set; }
    public decimal Max { get; set; }

    public override bool Validate(string value)
    {
        if (!decimal.TryParse(value, out var number)) return false;
        return number >= Min && number <= Max;
    }
}
