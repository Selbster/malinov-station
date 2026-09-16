using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.ViewVariables;

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
        var underlyingType = Enum.GetUnderlyingType(enumType);
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

        var isFlags = enumType.IsDefined(typeof(FlagsAttribute), false);
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
                ValueChanged(Convert.ChangeType(Enum.ToObject(enumType, bits), underlyingType));
        }
    }

    private static ulong ToBits(object value)
    {
        return Type.GetTypeCode(Enum.GetUnderlyingType(value.GetType())) switch
        {
            TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 => Convert.ToUInt64(value),
            _ => unchecked((ulong) Convert.ToInt64(value)),
        };
    }
}
