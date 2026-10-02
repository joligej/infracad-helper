using System.ComponentModel;

namespace NlcsLegenda.Plugin;

// Toont één onderliggend instellingenobject maar met alleen de properties die bij een tabblad
// horen. Zo kan elk tabblad een eigen PropertyGrid op hetzelfde object tonen; get/set gaan
// rechtstreeks naar het echte object, dus wijzigingen op welk tabblad dan ook landen op
// dezelfde instellingen.
internal sealed class FilteredSettings : ICustomTypeDescriptor
{
    private readonly object _target;
    private readonly Func<PropertyDescriptor, bool> _include;

    public FilteredSettings(object target, Func<PropertyDescriptor, bool> include)
    {
        _target = target;
        _include = include;
    }

    public PropertyDescriptorCollection GetProperties() => GetProperties(null);

    public PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
    {
        var filtered = TypeDescriptor.GetProperties(_target, attributes)
            .Cast<PropertyDescriptor>()
            .Where(_include)
            .ToArray();
        return new PropertyDescriptorCollection(filtered);
    }

    public object GetPropertyOwner(PropertyDescriptor? pd) => _target;

    public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(_target);
    public string? GetClassName() => TypeDescriptor.GetClassName(_target);
    public string? GetComponentName() => TypeDescriptor.GetComponentName(_target);
    public TypeConverter GetConverter() => TypeDescriptor.GetConverter(_target);
    public EventDescriptor? GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(_target);
    public PropertyDescriptor? GetDefaultProperty() => null;
    public object? GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(_target, editorBaseType);
    public EventDescriptorCollection GetEvents() => TypeDescriptor.GetEvents(_target);
    public EventDescriptorCollection GetEvents(Attribute[]? attributes)
        => attributes is null ? TypeDescriptor.GetEvents(_target) : TypeDescriptor.GetEvents(_target, attributes);
}
