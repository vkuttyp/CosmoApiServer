using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MurshisoftData;
public class Serializer
{
    public static string DateTimeFormat
    {
        get
        {
            return _DateTimeFormat;
        }
        set
        {
            if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(DateTimeFormat));
            _DateTimeFormat = value;
        }
    }

    public bool IncludeNullProperties { get; set; } = false;

    public JsonSerializerOptions DefaultOptions
    {
        get
        {
            return _DefaultOptions;
        }
        set
        {
            if (value == null) throw new ArgumentNullException(nameof(DefaultOptions));
            _DefaultOptions = value;
        }
    }
    public List<JsonConverter> DefaultConverters
    {
        get
        {
            return _DefaultConverters;
        }
        set
        {
            if (value == null) throw new ArgumentNullException(nameof(DefaultConverters));
            _DefaultConverters = value;
        }
    }
    private static string _DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.ffffffZ";

    private JsonSerializerOptions _DefaultOptions = new JsonSerializerOptions
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private List<JsonConverter> _DefaultConverters = new List<JsonConverter>
        {
            new ExceptionConverter<Exception>(),
            new NameValueCollectionConverter(),
            new DateTimeConverter(),
            new IPAddressConverter(),
            new StrictEnumConverterFactory()
        };

    public T DeserializeJson<T>(byte[] json)
    {
        return DeserializeJson<T>(Encoding.UTF8.GetString(json));
    }
    public T DeserializeJson<T>(string json)
    {
        JsonSerializerOptions options = new JsonSerializerOptions(_DefaultOptions);

        foreach (JsonConverter converter in _DefaultConverters)
            options.Converters.Add(converter);

        return JsonSerializer.Deserialize<T>(json, options);
    }
    public string SerializeJson(object obj, bool pretty = true)
    {
        if (obj == null) return null;

        JsonSerializerOptions options = new JsonSerializerOptions(_DefaultOptions);

        foreach (JsonConverter converter in _DefaultConverters)
            options.Converters.Add(converter);

        if (!IncludeNullProperties) options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        if (!pretty)
        {
            options.WriteIndented = false;
            string json = JsonSerializer.Serialize(obj, options);
            options = null;
            return json;
        }
        else
        {
            options.WriteIndented = true;
            string json = JsonSerializer.Serialize(obj, options);
            options = null;
            return json;
        }
    }
    public T CopyObject<T>(object o)
    {
        if (o == null) return default(T);
        string json = SerializeJson(o, false);
        T ret = DeserializeJson<T>(json);
        return ret;
    }
    public class ExceptionConverter<TExceptionType> : JsonConverter<TExceptionType>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeof(Exception).IsAssignableFrom(typeToConvert);
        }
        public override TExceptionType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("Deserializing exceptions is not allowed");
        }
        public override void Write(Utf8JsonWriter writer, TExceptionType value, JsonSerializerOptions options)
        {
            var serializableProperties = value.GetType()
                .GetProperties()
                .Select(uu => new { uu.Name, Value = uu.GetValue(value) })
                .Where(uu => uu.Name != nameof(Exception.TargetSite));

            if (options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull)
            {
                serializableProperties = serializableProperties.Where(uu => uu.Value != null);
            }

            var propList = serializableProperties.ToList();

            if (propList.Count == 0)
            {
                // Nothing to write
                return;
            }

            writer.WriteStartObject();

            foreach (var prop in propList)
            {
                writer.WritePropertyName(prop.Name);
                JsonSerializer.Serialize(writer, prop.Value, options);
            }

            writer.WriteEndObject();
        }
    }
    public class NameValueCollectionConverter : JsonConverter<NameValueCollection>
    {
        public override NameValueCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected start of object");
            }

            var collection = new NameValueCollection();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return collection;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected property name");
                }

                string key = reader.GetString();

                reader.Read();
                if (reader.TokenType == JsonTokenType.Null)
                {
                    collection.Add(key, null);
                    continue;
                }

                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException("Expected string value");
                }

                string value = reader.GetString();

                // If the value contains commas, split it and add each value separately
                if (!string.IsNullOrEmpty(value) && value.Contains(","))
                {
                    var values = value.Split(',')
                                    .Select(v => v.Trim());
                    foreach (var v in values)
                    {
                        collection.Add(key, v);
                    }
                }
                else
                {
                    collection.Add(key, value);
                }
            }

            throw new JsonException("Expected end of object");
        }
        public override void Write(Utf8JsonWriter writer, NameValueCollection value, JsonSerializerOptions options)
        {
            if (value != null)
            {
                Dictionary<string, string> val = new Dictionary<string, string>();

                for (int i = 0; i < value.AllKeys.Count(); i++)
                {
                    string key = value.Keys[i];
                    string[] values = value.GetValues(key);
                    string formattedValue = null;

                    if (values != null && values.Length > 0)
                    {
                        int added = 0;

                        for (int j = 0; j < values.Length; j++)
                        {
                            if (!String.IsNullOrEmpty(values[j]))
                            {
                                if (added == 0) formattedValue += values[j];
                                else formattedValue += ", " + values[j];
                            }

                            added++;
                        }
                    }

                    val.Add(key, formattedValue);
                }

                System.Text.Json.JsonSerializer.Serialize(writer, val);
            }
        }
    }
    public class DateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string str = reader.GetString();

            DateTime val;
            if (DateTime.TryParse(str, out val)) return val;

            throw new FormatException("The JSON value '" + str + "' could not be converted to System.DateTime.");
        }
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString(_DateTimeFormat, CultureInfo.InvariantCulture));
        }
        private List<string> _AcceptedFormats = new List<string>
            {
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ssK",
                "yyyy-MM-dd HH:mm:ss.ffffff",
                "yyyy-MM-ddTHH:mm:ss.ffffff",
                "yyyy-MM-ddTHH:mm:ss.fffffffK",
                "yyyy-MM-dd",
                "MM/dd/yyyy HH:mm",
                "MM/dd/yyyy hh:mm tt",
                "MM/dd/yyyy H:mm",
                "MM/dd/yyyy h:mm tt",
                "MM/dd/yyyy HH:mm:ss"
            };
    }
    public class IntPtrConverter : JsonConverter<IntPtr>
    {
        public override IntPtr Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new InvalidOperationException("Properties of type IntPtr cannot be deserialized from JSON.");
        }
        public override void Write(Utf8JsonWriter writer, IntPtr value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
    public class IPAddressConverter : JsonConverter<IPAddress>
    {
        public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string str = reader.GetString();
            return IPAddress.Parse(str);
        }
        public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
    public class StrictEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string stringValue = reader.GetString();
                if (!Enum.TryParse<TEnum>(stringValue, ignoreCase: true, out var enumValue) ||
                    !Enum.IsDefined(typeof(TEnum), enumValue))
                {
                    throw new JsonException($"String value '{stringValue}' is not valid for enum type {typeof(TEnum).Name}");
                }
                return enumValue;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                int intValue = reader.GetInt32();
                // Explicitly get the defined values
                var definedValues = (int[])Enum.GetValues(typeof(TEnum));

                if (!Array.Exists(definedValues, x => x == intValue))
                {
                    throw new JsonException($"Integer value {intValue} is not defined in enum {typeof(TEnum).Name}");
                }

                return (TEnum)Enum.ToObject(typeof(TEnum), intValue);
            }

            throw new JsonException($"Cannot convert {reader.TokenType} to enum {typeof(TEnum).Name}");
        }
        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
    public class StrictEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsEnum;
        }
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var converterType = typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert);
            return (JsonConverter)Activator.CreateInstance(converterType);
        }
    }

}