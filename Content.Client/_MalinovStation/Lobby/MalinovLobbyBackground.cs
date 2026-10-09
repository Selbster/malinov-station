using Robust.Client.Animations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Animations;

namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// The lobby background picture. A new picture can fade in over the old one instead of replacing it at once.
/// </summary>
public sealed class MalinovLobbyBackground : Control
{
    public const string FadeKey = "malinov-lobby-background-fade";
    public const float FadeSeconds = 1f;

    /// <summary>
    /// The picture in place.
    /// </summary>
    public TextureRect ShownLayer { get; }

    /// <summary>
    /// The next picture while it fades in over <see cref="ShownLayer"/>; hidden otherwise.
    /// </summary>
    public TextureRect IncomingLayer { get; }

    /// <summary>
    /// The picture shown once any fade ends.
    /// </summary>
    public Texture? Texture => IncomingLayer.Visible ? IncomingLayer.Texture : ShownLayer.Texture;

    public MalinovLobbyBackground()
    {
        ShownLayer = new TextureRect { Stretch = TextureRect.StretchMode.KeepAspectCovered };
        IncomingLayer = new TextureRect { Stretch = TextureRect.StretchMode.KeepAspectCovered, Visible = false };
        AddChild(ShownLayer);
        AddChild(IncomingLayer);

        IncomingLayer.AnimationCompleted += OnIncomingAnimationCompleted;
    }

    /// <summary>
    /// Shows <paramref name="texture"/>. With <paramref name="fade"/>, it fades in over the picture shown now;
    /// the first picture and the removal of the picture are always at once.
    /// </summary>
    public void SetTexture(Texture? texture, bool fade)
    {
        if (texture == Texture)
            return;

        FinishFade();
        if (!fade || texture == null || ShownLayer.Texture == null)
        {
            ShownLayer.Texture = texture;
            return;
        }

        var transparent = Color.White.WithAlpha(0f);
        IncomingLayer.Texture = texture;
        IncomingLayer.Modulate = transparent;
        IncomingLayer.Visible = true;
        IncomingLayer.PlayAnimation(new Animation
        {
            Length = TimeSpan.FromSeconds(FadeSeconds),
            AnimationTracks =
            {
                new AnimationTrackControlProperty
                {
                    Property = nameof(Modulate),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(transparent, 0f),
                        new AnimationTrackProperty.KeyFrame(Color.White, FadeSeconds, Easings.InOutSine),
                    },
                },
            },
        }, FadeKey);
    }

    private void OnIncomingAnimationCompleted(string key)
    {
        if (key == FadeKey)
            FinishFade();
    }

    /// <summary>
    /// Puts the incoming picture in place at once, if one is fading in.
    /// </summary>
    private void FinishFade()
    {
        if (!IncomingLayer.Visible)
            return;

        IncomingLayer.StopAnimation(FadeKey);
        ShownLayer.Texture = IncomingLayer.Texture;
        IncomingLayer.Visible = false;
        IncomingLayer.Texture = null;
    }
}
