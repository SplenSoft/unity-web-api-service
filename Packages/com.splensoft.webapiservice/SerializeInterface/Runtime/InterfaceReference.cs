using System;
using UnityEngine;
using Object = UnityEngine.Object;

[Serializable]
internal class InterfaceReference<TInterface, TObject> where TObject : Object where TInterface : class {
    [SerializeField, HideInInspector] TObject underlyingValue;

    internal TInterface Value {
        get => underlyingValue switch {
            null => null,
            TInterface @interface => @interface,
            _ => throw new InvalidOperationException($"{underlyingValue} needs to implement interface {nameof(TInterface)}.")
        };
        set => underlyingValue = value switch {
            null => null,
            TObject newValue => newValue,
            _ => throw new ArgumentException($"{value} needs to be of type {typeof(TObject)}.", string.Empty)
        };
    }

    internal TObject UnderlyingValue {
        get => underlyingValue;
        set => underlyingValue = value;
    }

    internal InterfaceReference() { }

    internal InterfaceReference(TObject target) => underlyingValue = target;

    internal InterfaceReference(TInterface @interface) => underlyingValue = @interface as TObject;

    public static implicit operator TInterface(InterfaceReference<TInterface, TObject> obj) => obj.Value;
}

[Serializable]
internal class InterfaceReference<TInterface> : InterfaceReference<TInterface, Object> where TInterface : class { }