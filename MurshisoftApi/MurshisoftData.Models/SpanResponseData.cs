using MurshisoftData.Models.POS;
using PropertyChanged;

namespace MurshisoftData.Models;
[AddINotifyPropertyChangedInterface]
public class SpanResponseData 
{
    public string ResponseId { get; set; }
    public string CardNumber { get; set; }
    public string CardType { get; set; }
    public string DateAndTime { get; set; }
    public string AuthCode { get; set; }
    public string ResponseCode { get; set; }
    public string ReferenceNumber { get; set; }
    public string MerchantId { get; set; }
    public decimal Amount { get; set; }
    public string TerminalId { get; set; }
    public string TransactionId { get; set; } = MySettingsPOS.Language == "Arabic" ? "اضغط هنا اختيار فاتورة" : "Clic to select an Invoice";
    public string Notes { get; set; } = "";
    public int SpanTypeId
    {
        get
        {
            int id = 0;
            var start = CardType?.ToLower()?.Substring(0, 4) ?? "";
            switch (start)
            {
                case "mada":
                case "span":
                    return 1;
                case "visa": return 2;
                case "mast": return 3;
                default: return 0;
            }
        }
    }

}