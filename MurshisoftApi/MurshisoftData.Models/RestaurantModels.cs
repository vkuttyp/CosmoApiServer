using PropertyChanged;

namespace MurshisoftData.Models;

[AddINotifyPropertyChangedInterface]

public class OnlineProvider
{
    public int id { get; set; }
    public string name { get; set; }
    public string AccountNo { get; set; }
    public decimal Percentage { get; set; }

}
