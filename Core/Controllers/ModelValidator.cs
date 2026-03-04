using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace CosmoApiServer.Core.Controllers;

/// <summary>
/// High-performance model validator using precomputed validation delegates.
/// Avoids reflection at request time.
/// </summary>
public static class ModelValidator
{
    private static readonly ConcurrentDictionary<Type, Action<object, Dictionary<string, string>>> _validators = new();

    public static bool Validate(object model, Dictionary<string, string> modelState)
    {
        var type = model.GetType();
        var validator = _validators.GetOrAdd(type, BuildValidator);
        validator(model, modelState);
        return modelState.Count == 0;
    }

    private static Action<object, Dictionary<string, string>> BuildValidator(Type type)
    {
        var modelParam = Expression.Parameter(typeof(object), "model");
        var stateParam = Expression.Parameter(typeof(Dictionary<string, string>), "state");
        var castModel  = Expression.Convert(modelParam, type);

        var list = new List<Expression>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attrs = prop.GetCustomAttributes<ValidationAttribute>(true);
            if (!attrs.Any()) continue;

            var propAccess = Expression.Property(castModel, prop);
            var propName   = Expression.Constant(prop.Name);

            foreach (var attr in attrs)
            {
                // We pre-capture the attribute instance at startup
                var attrConst = Expression.Constant(attr);
                
                // Expression for: if (!attr.IsValid(value)) state[name] = attr.FormatErrorMessage(name);
                var valueExpr = Expression.Convert(propAccess, typeof(object));
                var isValidCall = Expression.Call(attrConst, typeof(ValidationAttribute).GetMethod("IsValid", [typeof(object)])!, valueExpr);
                
                var formatCall = Expression.Call(attrConst, typeof(ValidationAttribute).GetMethod("FormatErrorMessage", [typeof(string)])!, propName);
                
                var dictIndexer = typeof(Dictionary<string, string>).GetProperty("Item")!;
                var setDict = Expression.Call(stateParam, dictIndexer.SetMethod!, propName, formatCall);

                var ifNotValid = Expression.IfThen(Expression.Not(isValidCall), setDict);
                list.Add(ifNotValid);
            }
        }

        if (list.Count == 0) return (_, _) => { };

        var body = Expression.Block(list);
        return Expression.Lambda<Action<object, Dictionary<string, string>>>(body, modelParam, stateParam).Compile();
    }
}
