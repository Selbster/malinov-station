using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.ViewVariables;
using Robust.Shared.Utility;

namespace Content.Client._MalinovStation.ViewVariables;

/// <summary>
/// VV enum editor that preserves signed and unsigned values of every underlying integral type.
/// </summary>
internal sealed class MalinovEnumPropEditor : VVPropEditor
{
    protected override Control MakeUI(object? value)
    {
        var enumType = value!.GetType();
        var enumValues = Enum.GetValues(enumType);
        var enumNames = Enum.GetNames(enumType);
        var typeCode = ((Enum) value).GetTypeCode();
        var currentValue = ToBits(value);
        var valuesById = new Dictionary<int, ulong>();
        var idsByValue = new Dictionary<ulong, int>();
        var buttons = new Dictionary<ulong, Button>();
        var container = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        var options = new OptionButton { Disabled = ReadOnly };
        container.AddChild(options);

        for (var i = 0; i < enumValues.Length; i++)
        {
            var bits = ToBits(enumValues.GetValue(i)!);
            valuesById.Add(i, bits);
            idsByValue.TryAdd(bits, i);
            options.AddItem(enumNames[i], i);
        }

        var isFlags = enumType.HasCustomAttribute<FlagsAttribute>();
        var invalidId = -1;
        if (isFlags || !idsByValue.ContainsKey(currentValue))
        {
            invalidId = enumValues.Length;
            options.AddItem(string.Empty, invalidId);
        }

        if (isFlags)
        {
            var usedFlags = 0UL;
            foreach (var entry in enumValues)
            {
                var bits = ToBits(entry);
                if (bits == 0 || (bits & usedFlags) != 0)
                    continue;

                usedFlags |= bits;
                var button = new Button
                {
                    Text = enumNames[idsByValue[bits]],
                    ToggleMode = true,
                    Disabled = ReadOnly,
                };
                buttons.Add(bits, button);
                container.AddChild(button);
                if (!ReadOnly)
                    button.OnToggled += args => Select(args.Pressed ? currentValue | bits : currentValue & ~bits);
            }
        }

        if (!ReadOnly)
        {
            options.OnItemSelected += args =>
            {
                if (args.Id != invalidId)
                    Select(valuesById[args.Id]);
            };
        }

        Select(currentValue, false);
        return container;

        void Select(ulong bits, bool changeValue = true)
        {
            currentValue = bits;
            foreach (var (flags, button) in buttons)
                button.Pressed = (flags & bits) != 0;

            options.SelectId(idsByValue.TryGetValue(bits, out var id) ? id : invalidId);
            if (changeValue)
                ValueChanged(FromBits(bits, typeCode));
        }
    }

    private static ulong ToBits(object value)
    {
        // Boxed enums can be unboxed as their exact underlying type. Avoid Convert's
        // numeric overloads and reflection APIs that are unavailable in the content sandbox.
        return ((Enum) value).GetTypeCode() switch
        {
            TypeCode.Byte => (byte) value,
            TypeCode.SByte => unchecked((ulong) (sbyte) value),
            TypeCode.Int16 => unchecked((ulong) (short) value),
            TypeCode.UInt16 => (ushort) value,
            TypeCode.Int32 => unchecked((ulong) (int) value),
            TypeCode.UInt32 => (uint) value,
            TypeCode.Int64 => unchecked((ulong) (long) value),
            TypeCode.UInt64 => (ulong) value,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static object FromBits(ulong bits, TypeCode typeCode)
    {
        // Return separately so each value is boxed with its original integral type.
        switch (typeCode)
        {
            case TypeCode.Byte:
                return unchecked((byte) bits);
            case TypeCode.SByte:
                return unchecked((sbyte) bits);
            case TypeCode.Int16:
                return unchecked((short) bits);
            case TypeCode.UInt16:
                return unchecked((ushort) bits);
            case TypeCode.Int32:
                return unchecked((int) bits);
            case TypeCode.UInt32:
                return unchecked((uint) bits);
            case TypeCode.Int64:
                return unchecked((long) bits);
            case TypeCode.UInt64:
                return bits;
            default:
                throw new ArgumentOutOfRangeException(nameof(typeCode));
        }
    }
}
