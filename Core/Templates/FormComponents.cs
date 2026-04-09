using CosmoApiServer.Core.Templates;
using System.Buffers;
using System.Runtime.CompilerServices;
using CosmoApiServer.Core.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Microsoft.AspNetCore.Components
{
    // ── EventCallback ────────────────────────────────────────────────────────────

    /// <summary>
    /// A callback that can be used by parent components to respond to child events.
    /// </summary>
    public readonly struct EventCallback
    {
        private readonly Func<ValueTask>? _delegate;

        public EventCallback(Func<ValueTask>? callback) => _delegate = callback;
        public EventCallback(Action? callback) => _delegate = callback is not null ? () => { callback(); return ValueTask.CompletedTask; } : null;

        public bool HasDelegate => _delegate is not null;

        public ValueTask InvokeAsync() => _delegate?.Invoke() ?? ValueTask.CompletedTask;
        public ValueTask InvokeAsync(object? arg) => InvokeAsync();

        public static readonly EventCallback Empty = new((Func<ValueTask>?)null);
        public static readonly EventCallbackFactory Factory = new();

        public static implicit operator EventCallback(Func<ValueTask> callback) => new(callback);
        public static implicit operator EventCallback(Action callback) => new(callback);
    }

    /// <summary>
    /// A callback that carries a value, used by parent components to respond to child events.
    /// </summary>
    public readonly struct EventCallback<TValue>
    {
        private readonly Func<TValue, ValueTask>? _delegate;

        public EventCallback(Func<TValue, ValueTask>? callback) => _delegate = callback;
        public EventCallback(Action<TValue>? callback) => _delegate = callback is not null ? (v) => { callback(v); return ValueTask.CompletedTask; } : null;

        public bool HasDelegate => _delegate is not null;

        public ValueTask InvokeAsync(TValue arg) => _delegate?.Invoke(arg) ?? ValueTask.CompletedTask;

        public static readonly EventCallback<TValue> Empty = new((Func<TValue, ValueTask>?)null);

        public static implicit operator EventCallback<TValue>(Func<TValue, ValueTask> callback) => new(callback);
        public static implicit operator EventCallback<TValue>(Action<TValue> callback) => new(callback);
    }

    /// <summary>
    /// Factory for creating EventCallback instances. Used by Razor-generated code.
    /// </summary>
    public sealed class EventCallbackFactory
    {
        public EventCallback Create(object receiver, Action callback) => new EventCallback(callback);
        public EventCallback Create(object receiver, Func<ValueTask> callback) => new EventCallback(callback);
        public EventCallback<TValue> Create<TValue>(object receiver, Action callback) => new EventCallback<TValue>(_ => { callback(); return ValueTask.CompletedTask; });
        public EventCallback<TValue> Create<TValue>(object receiver, Action<TValue> callback) => new EventCallback<TValue>(callback);
        public EventCallback<TValue> Create<TValue>(object receiver, Func<ValueTask> callback) => new EventCallback<TValue>(_ => callback());
        public EventCallback<TValue> Create<TValue>(object receiver, Func<TValue, ValueTask> callback) => new EventCallback<TValue>(callback);
    }

    // ── CascadingValue ───────────────────────────────────────────────────────────

    /// <summary>
    /// Supplies a cascading value to all descendant components.
    /// Usage in .razor: &lt;CascadingValue Value="@myValue"&gt;@ChildContent&lt;/CascadingValue&gt;
    /// </summary>
    public class CascadingValue<TValue> : CosmoApiServer.Core.Templates.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public TValue? Value { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string? Name { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public Microsoft.AspNetCore.Components.RenderFragment? ChildContent { get; set; }

        /// <summary>Internal storage for the cascading value, accessible by child components.</summary>
        internal Dictionary<Type, object?> CascadingValues { get; } = new();

        protected override async ValueTask BuildRenderTreeAsync(IBufferWriter<byte> buffer)
        {
            // Store the value so child components can find it via FindCascadingValue
            if (Value is not null)
            {
                CascadingValues[typeof(TValue)] = Value;
            }

            if (ChildContent is not null && _activeBuilder is not null)
            {
                ChildContent(_activeBuilder);
                await _activeBuilder.ProcessAsync();
            }
        }
    }

    // ── ValidationSummary ────────────────────────────────────────────────────────

    /// <summary>
    /// Displays all validation errors as a list.
    /// Usage: &lt;ValidationSummary /&gt;
    /// </summary>
    public class ValidationSummary : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public string CssClass { get; set; } = "validation-errors";

        [Microsoft.AspNetCore.Components.Parameter]
        public string ListCssClass { get; set; } = "text-danger";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            var errors = FindModelState();
            if (errors is null || errors.Count == 0) return;

            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", CssClass);
            builder.OpenElement(2, "ul");
            builder.AddAttribute(3, "class", ListCssClass);

            int seq = 10;
            foreach (var kvp in errors)
            {
                builder.OpenElement(seq++, "li");
                builder.AddContent(seq++, kvp.Value);
                builder.CloseElement();
            }

            builder.CloseElement(); // ul
            builder.CloseElement(); // div
        }

        private Dictionary<string, string>? FindModelState()
        {
            var current = Parent;
            while (current is not null)
            {
                if (current.ModelState.Count > 0) return current.ModelState;
                if (current is EditForm form && form.Context?.ValidationMessages.Count > 0)
                    return form.Context.ValidationMessages;
                current = current.Parent;
            }
            return null;
        }
    }

    // ── EditForm ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a form that validates a model on submission.
    /// Analogous to Blazor's &lt;EditForm&gt; component.
    /// </summary>
    public class EditForm : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public object? Model { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public CosmoApiServer.Core.Templates.EditContext? EditContext { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string? Action { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string Method { get; set; } = "post";

        [Microsoft.AspNetCore.Components.Parameter]
        public string? CssClass { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public EventCallback<CosmoApiServer.Core.Templates.EditContext> OnValidSubmit { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public EventCallback<CosmoApiServer.Core.Templates.EditContext> OnInvalidSubmit { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public Microsoft.AspNetCore.Components.RenderFragment<CosmoApiServer.Core.Templates.EditContext>? ChildContent { get; set; }

        /// <summary>The edit context for the form, available to child input components.</summary>
        public CosmoApiServer.Core.Templates.EditContext Context { get; private set; } = null!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            Context = EditContext ?? (Model is not null ? new CosmoApiServer.Core.Templates.EditContext(Model) : new CosmoApiServer.Core.Templates.EditContext(new object()));

            // In SSR, if it's a POST and we have a valid model, trigger callbacks
            if (HttpContext?.Request.Method == CosmoApiServer.Core.Http.HttpMethod.POST)
            {
                if (Context.Validate())
                {
                    OnValidSubmit.InvokeAsync(Context);
                }
                else
                {
                    OnInvalidSubmit.InvokeAsync(Context);
                }
            }

            builder.OpenElement(0, "form");
            builder.AddAttribute(1, "method", Method);
            if (Action is not null)
                builder.AddAttribute(2, "action", Action);
            if (CssClass is not null)
                builder.AddAttribute(3, "class", CssClass);

            if (ChildContent is not null)
                ChildContent(Context)(builder);

            builder.CloseElement();
        }
    }

    // ── InputText ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders an &lt;input type="text"&gt; bound to a model property.
    /// </summary>
    public class InputText : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public string? Value { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public EventCallback<string> ValueChanged { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string Id { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string Name { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string? Placeholder { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string CssClass { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string Type { get; set; } = "text";

        [Microsoft.AspNetCore.Components.Parameter]
        public bool Disabled { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public bool Required { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public Dictionary<string, object>? AdditionalAttributes { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "type", Type);
            if (!string.IsNullOrEmpty(Id)) builder.AddAttribute(2, "id", Id);
            if (!string.IsNullOrEmpty(Name)) builder.AddAttribute(3, "name", Name);
            builder.AddAttribute(4, "value", Value ?? string.Empty);
            if (!string.IsNullOrEmpty(Placeholder)) builder.AddAttribute(5, "placeholder", Placeholder);
            if (!string.IsNullOrEmpty(CssClass)) builder.AddAttribute(6, "class", CssClass);
            if (Disabled) builder.AddAttribute(7, "disabled", true);
            if (Required) builder.AddAttribute(8, "required", true);

            if (AdditionalAttributes is not null)
            {
                int seq = 10;
                foreach (var kvp in AdditionalAttributes)
                    builder.AddAttribute(seq++, kvp.Key, kvp.Value);
            }

            builder.CloseElement();
        }
    }

    // ── InputNumber ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders an &lt;input type="number"&gt; bound to a numeric model property.
    /// </summary>
    public class InputNumber<TValue> : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public TValue? Value { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public EventCallback<TValue> ValueChanged { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string Id { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string Name { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string CssClass { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string? Min { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string? Max { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string? Step { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public bool Disabled { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public bool Required { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "type", "number");
            if (!string.IsNullOrEmpty(Id)) builder.AddAttribute(2, "id", Id);
            if (!string.IsNullOrEmpty(Name)) builder.AddAttribute(3, "name", Name);
            builder.AddAttribute(4, "value", Value?.ToString() ?? string.Empty);
            if (!string.IsNullOrEmpty(CssClass)) builder.AddAttribute(5, "class", CssClass);
            if (Min is not null) builder.AddAttribute(6, "min", Min);
            if (Max is not null) builder.AddAttribute(7, "max", Max);
            if (Step is not null) builder.AddAttribute(8, "step", Step);
            if (Disabled) builder.AddAttribute(9, "disabled", true);
            if (Required) builder.AddAttribute(10, "required", true);
            builder.CloseElement();
        }
    }


    // ── InputDate ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders an <input type="date"> bound to a model property.
    /// </summary>
    public class InputDate<TValue> : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public TValue? Value { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public EventCallback<TValue> ValueChanged { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string Id { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string Name { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string CssClass { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public bool Disabled { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public bool Required { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string Type { get; set; } = "date";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "type", Type);
            if (!string.IsNullOrEmpty(Id)) builder.AddAttribute(2, "id", Id);
            if (!string.IsNullOrEmpty(Name)) builder.AddAttribute(3, "name", Name);
            
            string formattedValue = string.Empty;
            if (Value is DateTime dt) formattedValue = BindConverter.FormatValue(dt, Type == "datetime-local" ? "yyyy-MM-ddTHH:mm" : "yyyy-MM-dd");
            else if (Value is DateTimeOffset dto) formattedValue = BindConverter.FormatValue(dto, Type == "datetime-local" ? "yyyy-MM-ddTHH:mm" : "yyyy-MM-dd");
            else formattedValue = Value?.ToString() ?? string.Empty;

            builder.AddAttribute(4, "value", formattedValue);
            if (!string.IsNullOrEmpty(CssClass)) builder.AddAttribute(5, "class", CssClass);
            if (Disabled) builder.AddAttribute(6, "disabled", true);
            if (Required) builder.AddAttribute(7, "required", true);
            builder.CloseElement();
        }
    }

    // ── InputSelect ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a &lt;select&gt; element bound to a model property.
    /// </summary>
    public class InputSelect<TValue> : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public TValue? Value { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public EventCallback<TValue> ValueChanged { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string Id { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string Name { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string CssClass { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public bool Disabled { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public bool Required { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public Microsoft.AspNetCore.Components.RenderFragment? ChildContent { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "select");
            if (!string.IsNullOrEmpty(Id)) builder.AddAttribute(1, "id", Id);
            if (!string.IsNullOrEmpty(Name)) builder.AddAttribute(2, "name", Name);
            builder.AddAttribute(3, "value", Value?.ToString() ?? string.Empty);
            if (!string.IsNullOrEmpty(CssClass)) builder.AddAttribute(4, "class", CssClass);
            if (Disabled) builder.AddAttribute(5, "disabled", true);
            if (Required) builder.AddAttribute(6, "required", true);

            if (ChildContent is not null)
                builder.AddContent(7, ChildContent);

            builder.CloseElement();
        }
    }

    // ── InputTextArea ────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a &lt;textarea&gt; element bound to a model property.
    /// </summary>
    public class InputTextArea : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public string? Value { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public EventCallback<string> ValueChanged { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string Id { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string Name { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string? Placeholder { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string CssClass { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public int Rows { get; set; } = 3;

        [Microsoft.AspNetCore.Components.Parameter]
        public bool Disabled { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public bool Required { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "textarea");
            if (!string.IsNullOrEmpty(Id)) builder.AddAttribute(1, "id", Id);
            if (!string.IsNullOrEmpty(Name)) builder.AddAttribute(2, "name", Name);
            if (!string.IsNullOrEmpty(Placeholder)) builder.AddAttribute(3, "placeholder", Placeholder);
            if (!string.IsNullOrEmpty(CssClass)) builder.AddAttribute(4, "class", CssClass);
            builder.AddAttribute(5, "rows", Rows.ToString());
            if (Disabled) builder.AddAttribute(6, "disabled", true);
            if (Required) builder.AddAttribute(7, "required", true);
            builder.AddContent(8, Value ?? string.Empty);
            builder.CloseElement();
        }
    }

    // ── InputCheckbox ────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders an &lt;input type="checkbox"&gt; bound to a boolean model property.
    /// </summary>
    public class InputCheckbox : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public bool Value { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public EventCallback<bool> ValueChanged { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string Id { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string Name { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string CssClass { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public bool Disabled { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "type", "checkbox");
            if (!string.IsNullOrEmpty(Id)) builder.AddAttribute(2, "id", Id);
            if (!string.IsNullOrEmpty(Name)) builder.AddAttribute(3, "name", Name);
            builder.AddAttribute(4, "checked", Value);
            if (!string.IsNullOrEmpty(CssClass)) builder.AddAttribute(5, "class", CssClass);
            if (Disabled) builder.AddAttribute(6, "disabled", true);
            // Hidden field for form submission (unchecked checkboxes don't submit)
            builder.CloseElement();
            if (!string.IsNullOrEmpty(Name))
            {
                builder.OpenElement(7, "input");
                builder.AddAttribute(8, "type", "hidden");
                builder.AddAttribute(9, "name", Name);
                builder.AddAttribute(10, "value", Value ? "true" : "false");
                builder.CloseElement();
            }
        }
    }

    // ── ValidationMessage ────────────────────────────────────────────────────────

    /// <summary>
    /// Displays a validation message for a specific field.
    /// Usage: &lt;ValidationMessage For="FieldName" /&gt;
    /// </summary>
    public class ValidationMessage : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public string For { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public string CssClass { get; set; } = "validation-message text-danger";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            // Walk up the parent chain to find a ModelState or EditContext
            var errors = FindModelState();
            if (errors is null || !errors.TryGetValue(For, out var message)) return;

            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", CssClass);
            builder.AddContent(2, message);
            builder.CloseElement();
        }

        private Dictionary<string, string>? FindModelState()
        {
            var current = Parent;
            while (current is not null)
            {
                if (current.ModelState.Count > 0) return current.ModelState;
                if (current is EditForm form && form.Context?.ValidationMessages.Count > 0)
                    return form.Context.ValidationMessages;
                current = current.Parent;
            }
            return null;
        }
    }
}

namespace CosmoApiServer.Core.Templates
{

// ── FieldIdentifier ──────────────────────────────────────────────────────────

/// <summary>
/// Uniquely identifies a single field on a model, used for change tracking and validation.
/// </summary>
public readonly struct FieldIdentifier : IEquatable<FieldIdentifier>
{
    /// <summary>The model object that owns the field.</summary>
    public object Model { get; }

    /// <summary>The name of the field (property name).</summary>
    public string FieldName { get; }

    public FieldIdentifier(object model, string fieldName)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
    }

    /// <summary>Creates a FieldIdentifier from a model and property name.</summary>
    public static FieldIdentifier Create(object model, string fieldName) => new(model, fieldName);

    public bool Equals(FieldIdentifier other) =>
        ReferenceEquals(Model, other.Model) &&
        string.Equals(FieldName, other.FieldName, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is FieldIdentifier other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Model), FieldName);
    public static bool operator ==(FieldIdentifier left, FieldIdentifier right) => left.Equals(right);
    public static bool operator !=(FieldIdentifier left, FieldIdentifier right) => !left.Equals(right);
}

// ── FieldState ───────────────────────────────────────────────────────────────

/// <summary>
/// Tracks the state of a single form field: modification, validation, original value.
/// </summary>
public sealed class FieldState
{
    /// <summary>Whether the field value has been changed from its original value.</summary>
    public bool IsModified { get; internal set; }

    /// <summary>The original value when the field was first tracked.</summary>
    public object? OriginalValue { get; internal set; }

    /// <summary>The current value of the field.</summary>
    public object? CurrentValue { get; internal set; }

    /// <summary>Validation error message, if any.</summary>
    public string? ValidationMessage { get; internal set; }

    /// <summary>Whether the field has a validation error.</summary>
    public bool IsInvalid => ValidationMessage is not null;

    /// <summary>Whether the field has been validated and has no errors.</summary>
    public bool IsValid { get; internal set; }
}

// ── FieldCssClassProvider ────────────────────────────────────────────────────

/// <summary>
/// Determines CSS classes for form fields based on their state.
/// Override to customize the CSS class logic.
/// </summary>
public class FieldCssClassProvider
{
    /// <summary>Default instance that uses "modified", "valid", "invalid" classes.</summary>
    public static FieldCssClassProvider Default { get; } = new();

    /// <summary>Returns the CSS class string for the given field state.</summary>
    public virtual string GetFieldCssClass(EditContext editContext, FieldIdentifier fieldIdentifier)
    {
        var state = editContext.GetFieldState(fieldIdentifier);
        if (state is null) return string.Empty;

        var classes = new List<string>(3);
        if (state.IsModified) classes.Add("modified");
        if (state.IsValid) classes.Add("valid");
        if (state.IsInvalid) classes.Add("invalid");
        return string.Join(' ', classes);
    }
}

// ── EditContext ───────────────────────────────────────────────────────────────

/// <summary>
/// Tracks the state of an edit form, including validation, modification tracking,
/// and field-level change detection. This is the core of the form system.
/// </summary>
public sealed class EditContext
{
    private readonly Dictionary<FieldIdentifier, FieldState> _fieldStates = new();
    private readonly Dictionary<string, object?> _originalValues = new(StringComparer.OrdinalIgnoreCase);
    private bool _snapshotTaken;

    /// <summary>The model being edited.</summary>
    public object Model { get; }

    /// <summary>Validation error messages keyed by field name.</summary>
    public Dictionary<string, string> ValidationMessages { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Provides CSS classes for fields based on their state.</summary>
    public FieldCssClassProvider CssClassProvider { get; set; } = FieldCssClassProvider.Default;

    /// <summary>Fired when a field value changes.</summary>
    public event Action<FieldIdentifier>? OnFieldChanged;

    /// <summary>Fired when validation is requested.</summary>
    public event Action? OnValidationRequested;

    /// <summary>Fired when validation state changes.</summary>
    public event Action? OnValidationStateChanged;

    public EditContext(object model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        TakeSnapshot();
    }

    // ── Snapshot / Original Values ───────────────────────────────────────

    /// <summary>
    /// Takes a snapshot of all current property values as the "original" baseline.
    /// Call this after loading data to establish the clean state.
    /// </summary>
    public void TakeSnapshot()
    {
        _originalValues.Clear();
        foreach (var prop in Model.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            try { _originalValues[prop.Name] = prop.GetValue(Model); }
            catch { /* skip properties that throw */ }
        }
        _snapshotTaken = true;

        // Reset all field states to unmodified
        foreach (var kvp in _fieldStates)
            kvp.Value.IsModified = false;
    }

    // ── Change Detection ─────────────────────────────────────────────────

    /// <summary>
    /// Notifies the EditContext that a field value has changed.
    /// Compares against the original snapshot to determine modification state.
    /// </summary>
    public void NotifyFieldChanged(FieldIdentifier fieldIdentifier)
    {
        var state = GetOrCreateFieldState(fieldIdentifier);

        // Read current value from model
        var prop = Model.GetType().GetProperty(fieldIdentifier.FieldName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (prop is not null)
        {
            state.CurrentValue = prop.GetValue(Model);

            // Compare against original
            if (_originalValues.TryGetValue(fieldIdentifier.FieldName, out var original))
            {
                state.IsModified = !Equals(state.CurrentValue, original);
            }
            else
            {
                state.IsModified = true;
            }
        }
        else
        {
            state.IsModified = true;
        }

        OnFieldChanged?.Invoke(fieldIdentifier);
    }

    /// <summary>
    /// Notifies change by field name (convenience overload).
    /// </summary>
    public void NotifyFieldChanged(string fieldName) =>
        NotifyFieldChanged(new FieldIdentifier(Model, fieldName));

    /// <summary>Returns true if ANY field has been modified from its original value.</summary>
    public bool IsModified()
    {
        // First check tracked fields
        foreach (var kvp in _fieldStates)
            if (kvp.Value.IsModified) return true;

        // Also do a live comparison for fields not yet tracked
        if (_snapshotTaken)
        {
            foreach (var prop in Model.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                try
                {
                    var current = prop.GetValue(Model);
                    if (_originalValues.TryGetValue(prop.Name, out var original))
                    {
                        if (!Equals(current, original)) return true;
                    }
                }
                catch { /* skip */ }
            }
        }

        return false;
    }

    /// <summary>Returns true if the specified field has been modified.</summary>
    public bool IsModified(FieldIdentifier fieldIdentifier)
    {
        if (_fieldStates.TryGetValue(fieldIdentifier, out var state))
            return state.IsModified;

        // Live comparison
        var prop = Model.GetType().GetProperty(fieldIdentifier.FieldName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (prop is null) return false;

        try
        {
            var current = prop.GetValue(Model);
            return _originalValues.TryGetValue(fieldIdentifier.FieldName, out var original)
                ? !Equals(current, original)
                : false;
        }
        catch { return false; }
    }

    /// <summary>Returns true if the specified field (by name) has been modified.</summary>
    public bool IsModified(string fieldName) => IsModified(new FieldIdentifier(Model, fieldName));

    /// <summary>Marks all fields as unmodified and retakes the snapshot.</summary>
    public void MarkAsUnmodified()
    {
        foreach (var kvp in _fieldStates)
            kvp.Value.IsModified = false;
        TakeSnapshot();
    }

    /// <summary>Marks a specific field as unmodified.</summary>
    public void MarkAsUnmodified(FieldIdentifier fieldIdentifier)
    {
        if (_fieldStates.TryGetValue(fieldIdentifier, out var state))
        {
            state.IsModified = false;
            state.OriginalValue = state.CurrentValue;
        }

        // Update the snapshot for this field
        var prop = Model.GetType().GetProperty(fieldIdentifier.FieldName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (prop is not null)
        {
            try { _originalValues[fieldIdentifier.FieldName] = prop.GetValue(Model); }
            catch { /* skip */ }
        }
    }

    /// <summary>Marks a specific field as unmodified (by name).</summary>
    public void MarkAsUnmodified(string fieldName) =>
        MarkAsUnmodified(new FieldIdentifier(Model, fieldName));

    /// <summary>Gets the names of all fields that have been modified.</summary>
    public IEnumerable<string> GetModifiedFields()
    {
        var modified = new List<string>();
        foreach (var prop in Model.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            var fi = new FieldIdentifier(Model, prop.Name);
            if (IsModified(fi)) modified.Add(prop.Name);
        }
        return modified;
    }

    // ── Validation ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates the model using DataAnnotations and populates ValidationMessages.
    /// Also updates field-level validation state.
    /// </summary>
    public bool Validate()
    {
        OnValidationRequested?.Invoke();
        ValidationMessages.Clear();
        var result = CosmoApiServer.Core.Controllers.ModelValidator.Validate(Model, ValidationMessages);

        // Update field states with validation results
        foreach (var kvp in _fieldStates)
        {
            var fieldName = kvp.Key.FieldName;
            if (ValidationMessages.TryGetValue(fieldName, out var msg))
            {
                kvp.Value.ValidationMessage = msg;
                kvp.Value.IsValid = false;
            }
            else
            {
                kvp.Value.ValidationMessage = null;
                kvp.Value.IsValid = true;
            }
        }

        // Create field states for any newly-validated fields
        foreach (var kvp in ValidationMessages)
        {
            var fi = new FieldIdentifier(Model, kvp.Key);
            var state = GetOrCreateFieldState(fi);
            state.ValidationMessage = kvp.Value;
            state.IsValid = false;
        }

        OnValidationStateChanged?.Invoke();
        return result;
    }

    /// <summary>Gets whether a specific field has a validation error.</summary>
    public bool HasError(string fieldName) => ValidationMessages.ContainsKey(fieldName);

    /// <summary>Gets the error message for a specific field, or null.</summary>
    public string? GetError(string fieldName) =>
        ValidationMessages.TryGetValue(fieldName, out var msg) ? msg : null;

    // ── Field State Access ───────────────────────────────────────────────

    /// <summary>Gets the state for a specific field, or null if not yet tracked.</summary>
    public FieldState? GetFieldState(FieldIdentifier fieldIdentifier) =>
        _fieldStates.TryGetValue(fieldIdentifier, out var state) ? state : null;

    /// <summary>Gets the state for a specific field by name.</summary>
    public FieldState? GetFieldState(string fieldName) =>
        GetFieldState(new FieldIdentifier(Model, fieldName));

    /// <summary>Gets the CSS class string for a field based on its current state.</summary>
    public string FieldCssClass(FieldIdentifier fieldIdentifier) =>
        CssClassProvider.GetFieldCssClass(this, fieldIdentifier);

    /// <summary>Gets the CSS class string for a field by name.</summary>
    public string FieldCssClass(string fieldName) =>
        FieldCssClass(new FieldIdentifier(Model, fieldName));

    private FieldState GetOrCreateFieldState(FieldIdentifier fieldIdentifier)
    {
        if (!_fieldStates.TryGetValue(fieldIdentifier, out var state))
        {
            state = new FieldState();
            if (_originalValues.TryGetValue(fieldIdentifier.FieldName, out var original))
                state.OriginalValue = original;
            _fieldStates[fieldIdentifier] = state;
        }
        return state;
    }
}

// ── BindConverter ────────────────────────────────────────────────────────────

/// <summary>
/// Provides type conversion for @bind directives in Razor.
/// The Razor compiler emits calls to this class for @bind expressions.
/// </summary>
public static class BindConverter
{
    public static bool TryConvertTo<T>(object? value, out T? result)
    {
        try
        {
            if (value is T typed) { result = typed; return true; }
            if (value is null) { result = default; return true; }
            result = (T)System.Convert.ChangeType(value, typeof(T));
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    public static string FormatValue(string? value) => value ?? string.Empty;
    public static string FormatValue(int value) => value.ToString();
    public static string FormatValue(long value) => value.ToString();
    public static string FormatValue(float value) => value.ToString();
    public static string FormatValue(double value) => value.ToString();
    public static string FormatValue(decimal value) => value.ToString();
    public static string FormatValue(bool value) => value ? "true" : "false";
    public static string FormatValue(DateTime value, string? format = null) =>
        format is not null ? value.ToString(format) : value.ToString("yyyy-MM-dd");
    public static string FormatValue(DateTimeOffset value, string? format = null) =>
        format is not null ? value.ToString(format) : value.ToString("yyyy-MM-dd");
    public static string FormatValue<T>(T value) => value?.ToString() ?? string.Empty;
}
}
