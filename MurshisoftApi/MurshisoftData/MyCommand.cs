using Microsoft.Data.SqlClient;
using MurshisoftData.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurshisoftData;

public static class MyCommand
{
    public static DataTable? GetDataTableFromJson(string? json)
    {
        if (json == null) return null;
        var ser = new Serializer();
        return ser.DeserializeJson<SerializableDataTable>(json).ToDataTable();
    }
    public static async Task<DataTableJson> GetTableJson(SqlDataReader reader)
    {
        var table = new DataTable();
        await Task.Run(() => table.Load(reader));
        var json = DataTableToJson(table);
        return new DataTableJson(json);
    }
    public static async Task<T?> GetJsonSerialized<T>(SqlDataReader reader, CancellationToken token = default)
    {
        var jsonResult = new StringBuilder();
        if (!reader.HasRows)
        {
            return default;
        }
        else
        {
            while (await reader.ReadAsync(token))
            {
                jsonResult.Append(reader[0].ToString());
            }
            try
            {
                var json = jsonResult.ToString();
                return JsonSerializer.Deserialize<T>(json);
            }
            catch { throw; }
        }
    }
    public static SqlCommand CmdProc(string proc, SqlConnection con)
    {
        var cmd = new SqlCommand
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandText = proc,
            CommandTimeout = 100,
            Connection = con
        };
        return cmd;
    }
    //public static SqlCommand CmdProc(string proc)
    //{
    //    var con = new SqlConnection(MySettingsPOS.ConnectionString);
    //    var cmd = new SqlCommand
    //    {
    //        CommandType = System.Data.CommandType.StoredProcedure,
    //        CommandText = proc,
    //        CommandTimeout = 100,
    //        Connection = con
    //    };
    //    return cmd;
    //}

    public static SqlCommand CmdText(string text, SqlConnection con)
    {
        var cmd = new SqlCommand
        {
            CommandType = System.Data.CommandType.Text,
            CommandText = text,
            CommandTimeout = 100,
            Connection = con
        };
        return cmd;
    }
    public static async Task<T?> ResponseToData<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"{error} is {response.StatusCode}");
        }
        using var stream = await response.Content.ReadAsStreamAsync();
        if (stream?.Length == 0) return default;
        return await JsonSerializer.DeserializeAsync<T>(stream!, option);
    }
    public static async Task ValidateResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"{error} is {response.StatusCode}");
        }
    }
    public static async Task<int> ResponseToInt(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"{error} is {response.StatusCode}");
        }
        string body = await response.Content.ReadAsStringAsync();
        if (int.TryParse(body, out int result))
            return result;
        return -1;
    }
    public static async Task<decimal> ResponseToDecimal(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"{error} is {response.StatusCode}");
        }
        string body = await response.Content.ReadAsStringAsync();
        if (decimal.TryParse(body, out var result))
            return result;
        return -1;
    }
    public static async Task<DateTime> ResponseToDate(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"{error} is {response.StatusCode}");
        }
        string body = await response.Content.ReadAsStringAsync();
        if (DateTime.TryParse(body, out var result))
            return result;
        return DateTime.Today;
    }
    public static async Task<bool> ResponseToBool(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"{error} is {response.StatusCode}");
        }
        string body = await response.Content.ReadAsStringAsync();
        if (bool.TryParse(body, out var result))
            return result;
        return false;
    }
    public static async Task<string> ResponseToString(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"{error} is {response.StatusCode}");
        }
        return MyTrim(await response.Content.ReadAsStringAsync());
    }
    public static string MyTrim(string text)
    {
        if(text == null)return string.Empty;
        char[] charsToTrim = { '"', ' ', '\t' };
        return text.Trim(charsToTrim);
    }
    public static async Task<T?> GetJsonSerialized<T>(SqlDataReader reader)
    {
        var jsonResult = new StringBuilder();
        if (!reader.HasRows)
        {
            return default;
        }
        else
        {
            while (await reader.ReadAsync())
            {
                jsonResult.Append(reader[0].ToString());
            }
            return JsonSerializer.Deserialize<T>(jsonResult.ToString(), option);
        }
    }
    public static async IAsyncEnumerable<T?> GetJsonSerializedByRow<T>(SqlDataReader reader)
    {
        if (!reader.HasRows)
        {
           yield return default;
        }
        else
        {
            while (await reader.ReadAsync())
            {
               yield return JsonSerializer.Deserialize<T>(reader[0].ToString(), option);
            }
        }
    }

    public static async Task<string> GetJson(SqlDataReader reader, CancellationToken token = default)
    {
        var jsonResult = new StringBuilder();
        if (!reader.HasRows)
        {
            jsonResult.Append("[]");
        }
        else
        {
            while (await reader.ReadAsync(token))
            {
                jsonResult.Append(reader.GetValue(0).ToString());
            }
        }
        return jsonResult.ToString();
    }

    public static string GetJson2(SqlDataReader reader)
    {
        var jsonResult = new StringBuilder();
        if (!reader.HasRows)
        {
            jsonResult.Append("[]");
        }
        else
        {
            while (reader.Read())
            {
                jsonResult.Append(reader.GetValue(0).ToString());
            }
        }
        return jsonResult.ToString();
    }
    public static JsonSerializerOptions option = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    public static string ToJson<T>(T source)
    {
        return JsonSerializer.Serialize(source, option);
    }

    public static string DataTableToJson(DataTable table)
    {
        SerializableDataTable sdt = SerializableDataTable.FromDataTable(table);
        var ser = new Serializer();
        return ser.SerializeJson(sdt, true);
    }
}