using System;

namespace Microsoft.AspNetCore.Razor.Hosting
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RazorCompiledItemAttribute(Type type, string kind, string identifier) : Attribute
    {
        public Type Type { get; } = type;
        public string Kind { get; } = kind;
        public string Identifier { get; } = identifier;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RazorSourceChecksumAttribute(string checksumAlgorithm, string checksum, string identifier) : Attribute
    {
        public string ChecksumAlgorithm { get; } = checksumAlgorithm;
        public string Checksum { get; } = checksum;
        public string Identifier { get; } = identifier;
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RazorCompiledItemMetadataAttribute(string key, string value) : Attribute
    {
        public string Key { get; } = key;
        public string Value { get; } = value;
    }
}

namespace Microsoft.AspNetCore.Mvc.ApplicationParts
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class RelatedAssemblyAttribute(string assemblyName) : Attribute
    {
        public string AssemblyName { get; } = assemblyName;
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class ProvideApplicationPartFactoryAttribute(string typeName) : Attribute
    {
        public string TypeName { get; } = typeName;
    }
}

namespace Microsoft.AspNetCore.Mvc.Razor.Internal
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class RazorInjectAttribute : Attribute;
}

namespace Microsoft.AspNetCore.Mvc.ViewFeatures
{
    public interface IModelExpressionProvider;
}

namespace Microsoft.AspNetCore.Mvc
{
    public interface IUrlHelper;
    public interface IViewComponentHelper;
}

namespace Microsoft.AspNetCore.Mvc.Rendering
{
    public interface IHtmlHelper<TModel>;
    public interface IJsonHelper;
}
