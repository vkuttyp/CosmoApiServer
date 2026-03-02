namespace WeatherApp.Models;

public sealed class AccountTrans
{
    public int Id { get; set; }
    public DateTime? TransDate { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? AccountNo { get; set; }
    public string? Reference { get; set; }
}
