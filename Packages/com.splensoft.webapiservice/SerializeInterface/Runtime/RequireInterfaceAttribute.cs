using System;
using UnityEngine;

[AttributeUsage(AttributeTargets.Field)]
internal class RequireInterfaceAttribute : PropertyAttribute {
    internal readonly Type InterfaceType;

    internal RequireInterfaceAttribute(Type interfaceType) {
        Debug.Assert(interfaceType.IsInterface, $"{nameof(interfaceType)} needs to be an interface.");
        InterfaceType = interfaceType;
    }
}
