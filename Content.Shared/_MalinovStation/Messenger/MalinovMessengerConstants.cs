namespace Content.Shared._MalinovStation.Messenger;

/// <summary>
/// Constants for the Malinov Messenger device network protocol.
/// </summary>
public static class MalinovMessengerConstants
{
    public const string CommandAnnounce = "announce";
    public const string CommandMessage = "msg";
    public const string CommandAck = "ack";
    public const string CommandDirectoryRequest = "directory_req";
    public const string CommandDirectory = "directory";
}
