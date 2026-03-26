using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace CosmoApiServer.Core.Templates;

/// <summary>
/// A Zod-inspired fluent validation engine for .NET.
/// Zero-dependency and high-performance.
/// </summary>
public static class v
{
    public static StringSchema String() => new();
    public static NumberSchema Number() => new();
    public static BoolSchema Bool() => new();
    public static ObjectSchema<T> Object<T>() where T : new() => new();
}

public abstract class Schema<T, TSelf> where TSelf : Schema<T, TSelf>
{
    protected readonly List<Func<T, string?, string?>> _rules = new();
    protected string? _label;

    public TSelf Label(string label) { _label = label; return (TSelf)this; }

    public TSelf Refine(Func<T, bool> predicate, string message)
    {
        _rules.Add((val, label) => predicate(val) ? null : message.Replace("{label}", label ?? "Field"));
        return (TSelf)this;
    }

    public virtual ValidationResult SafeParse(T value, string? fieldName = null)
    {
        var result = new ValidationResult { IsValid = true };
        foreach (var rule in _rules)
        {
            var error = rule(value, _label ?? fieldName);
            if (error != null)
            {
                result.IsValid = false;
                result.Errors[fieldName ?? ""] = error;
                break; // Stop at first error per field
            }
        }
        return result;
    }
}

public class StringSchema : Schema<string?, StringSchema>
{
    public StringSchema Min(int length, string? message = null) => 
        Refine(s => (s?.Length ?? 0) >= length, message ?? "{label} must be at least " + length + " characters.");

    public StringSchema Max(int length, string? message = null) => 
        Refine(s => (s?.Length ?? 0) <= length, message ?? "{label} must be at most " + length + " characters.");

    public StringSchema Email(string? message = null) => 
        Refine(s => string.IsNullOrEmpty(s) || Regex.IsMatch(s, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"), message ?? "Invalid email format.");

    public StringSchema Required(string? message = null) => 
        Refine(s => !string.IsNullOrWhiteSpace(s), message ?? "{label} is required.");
}

public class NumberSchema : Schema<double, NumberSchema>
{
    public NumberSchema Min(double min, string? message = null) => 
        Refine(n => n >= min, message ?? "{label} must be at least " + min + ".");

    public NumberSchema Max(double max, string? message = null) => 
        Refine(n => n <= max, message ?? "{label} must be at most " + max + ".");
}

public class BoolSchema : Schema<bool, BoolSchema>
{
    public BoolSchema Required(string? message = null) => 
        Refine(b => b, message ?? "{label} must be checked.");
}

public class ObjectSchema<T> where T : new()
{
    private readonly Dictionary<string, object> _fieldSchemas = new(StringComparer.OrdinalIgnoreCase);

    public ObjectSchema<T> Field<TField>(string name, Schema<TField, AnySchema<TField>> schema)
    {
        _fieldSchemas[name] = schema;
        return this;
    }

    // Overloads for convenience
    public ObjectSchema<T> Field(string name, StringSchema schema) { _fieldSchemas[name] = schema; return this; }
    public ObjectSchema<T> Field(string name, NumberSchema schema) { _fieldSchemas[name] = schema; return this; }
    public ObjectSchema<T> Field(string name, BoolSchema schema) { _fieldSchemas[name] = schema; return this; }

    public ValidationResult SafeParse(T obj)
    {
        var result = new ValidationResult { IsValid = true };
        if (obj == null) return result;

        foreach (var kvp in _fieldSchemas)
        {
            var prop = typeof(T).GetProperty(kvp.Key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null) continue;

            var val = prop.GetValue(obj);
            
            // Dynamic dispatch to the correct SafeParse
            var schema = kvp.Value;
            var method = schema.GetType().GetMethod("SafeParse");
            if (method != null)
            {
                var subResult = (ValidationResult)method.Invoke(schema, [val, kvp.Key])!;
                if (!subResult.IsValid)
                {
                    result.IsValid = false;
                    foreach (var err in subResult.Errors) result.Errors[err.Key] = err.Value;
                }
            }
        }
        return result;
    }
}

public class AnySchema<T> : Schema<T, AnySchema<T>> { }

public class ValidationResult
{
    public bool IsValid { get; set; }
    public Dictionary<string, string> Errors { get; } = new(StringComparer.OrdinalIgnoreCase);
}
