using UnityEngine;

namespace Anaglyph.Utils
{
    public static class ImageUtils
    {
        public static byte[] CropAndEncodeImage(
            Texture2D sourceTexture,
            int x,
            int y,
            int width,
            int height)
        {
            // Create a new texture for the cropped region
            Texture2D croppedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            // Get the pixels from the region we want to crop
            Color[] pixels = sourceTexture.GetPixels(x, y, width, height);

            // Set the pixels in our new texture
            croppedTexture.SetPixels(pixels);
            croppedTexture.Apply();

            // Encode to JPG
            byte[] jpgData = croppedTexture.EncodeToJPG(75); // 75 is the quality setting (0-100)

            // Clean up
            Object.Destroy(croppedTexture);

            return jpgData;
        }
    }
}