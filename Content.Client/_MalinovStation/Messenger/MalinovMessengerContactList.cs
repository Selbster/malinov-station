using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._MalinovStation.Messenger;

/// <summary>
///     Contact list for the Malinov Messenger. Conversations with unread messages
///     are drawn with a red row background in addition to the unread dot.
/// </summary>
public sealed class MalinovMessengerContactList : ItemList
{
    private static readonly StyleBoxFlat UnreadBackground = new()
    {
        BackgroundColor = new Color(0.62f, 0.10f, 0.10f, 0.45f),
    };

    private readonly HashSet<Item> _unread = new();

    public Item AddContactItem(string text, bool hasUnread)
    {
        var item = AddItem(text);
        if (hasUnread)
            _unread.Add(item);
        return item;
    }

    public new void Clear()
    {
        base.Clear();
        _unread.Clear();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        foreach (var item in _unread)
        {
            if (item.Region is { } region)
                UnreadBackground.Draw(handle, region, UIScale);
        }
    }
}