using Microsoft.Data.SqlClient;
using MurshisoftData.Models.POS;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurshisoftData.Azatca;

public class DataAccess(string ConnectionString )
{

    public  ZatcaSetting GetZatcaSettings()
    {
        using var con=new SqlConnection(ConnectionString);
        using var cmd = MyCommand.CmdProc("zatca.ZatcaSettings_Select",con);
        cmd.Connection.Open();
        var reader = cmd.ExecuteReader();
        var json = MyCommand.GetJson2(reader);
        var data = JsonSerializer.Deserialize<ZatcaSetting>(json);
        return data;
    }

    public  async Task<string> ValidateInvoice(string transactionId)
    {
        using var con = new SqlConnection(ConnectionString);
        using var cmd = MyCommand.CmdProc("zatca.ValidateInvoice", con);
        cmd.Parameters.AddWithValue("@TransactionId", transactionId);
        cmd.Parameters.AddWithValue("@Language", MySettingsPOS.Language);
        cmd.Parameters.Add("@message", System.Data.SqlDbType.NVarChar, 100).Direction = System.Data.ParameterDirection.Output;
        await cmd.Connection.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
        var msg = cmd.Parameters["@message"].Value.ToString();
        return msg;
    }
    public  async Task<string> ValidateCustomer(string customerId, string vatNo)
    {
        using var con = new SqlConnection(ConnectionString);
        using var cmd = MyCommand.CmdProc("zatca.ValidateCustomer", con);
        cmd.Parameters.AddWithValue("@CustomerId", customerId);
        cmd.Parameters.AddWithValue("@VatNo", vatNo);
        cmd.Parameters.AddWithValue("@Language", MySettingsPOS.Language);
        cmd.Parameters.Add("@message", System.Data.SqlDbType.NVarChar, 200).Direction = System.Data.ParameterDirection.Output;
        await cmd.Connection.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
        var msg = cmd.Parameters["@message"].Value.ToString();
        return msg;
    }

}

