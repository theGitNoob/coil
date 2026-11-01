using System;
using Godot;

namespace Coil.Presentation;

/// <summary>
/// The stand-in art every renderer draws with until the atlas lands (A-02): a
/// white disc, generated rather than loaded, so <c>assets/</c> stays empty and
/// the placeholder stays out of the APK and out of A-01/A-02's way. Renderers
/// tint it; nothing here decides a palette.
/// </summary>
internal static class PlaceholderDisc
{
    /// <summary>
    /// Edge of the generated disc, in pixels. An implementation detail of the
    /// placeholder, not a tunable — A-02 deletes it along with the rest of the
    /// stand-in art.
    /// </summary>
    public const int TextureSize = 64;

    public static ImageTexture Create()
    {
        Image image = Image.CreateEmpty(TextureSize, TextureSize, false, Image.Format.Rgba8);

        float centre = TextureSize * 0.5f;
        float radius = centre - 1f;

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float distance = new Vector2(x + 0.5f - centre, y + 0.5f - centre).Length();

                // One pixel of linear falloff, so the placeholder's edge is not
                // a staircase at the zoom the camera will use.
                float alpha = Math.Clamp(radius - distance, 0f, 1f);

                image.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
