using SkiaSharp;

namespace RocLandSecurity.Services
{
    public static class ImageCompressor
    {
        private const int MaxWidth = 1280;
        private const int MaxHeight = 960;
        private const int JpegQuality = 78;   // 0-100, 78 = buen balance calidad/tamaño

        /// Redimensiona y recomprime una foto a JPEG optimizado.
        /// Mantiene la proporción original, nunca upscale.
        public static byte[] ComprimirFoto(byte[] original)
        {
            using var inputStream = new MemoryStream(original);
            using var skStream = new SKManagedStream(inputStream);
            using var codec = SKCodec.Create(skStream);

            if (codec == null) return original;

            // ── Leer orientación EXIF ──────────────────────────────────────────
            var orientacion = codec.EncodedOrigin;
            using var skBitmap = SKBitmap.Decode(codec);
            if (skBitmap == null) return original;

            // ── Aplicar rotación según EXIF ────────────────────────────────────
            using var bitmapOrientado = AplicarOrientacion(skBitmap, orientacion);

            // ── Calcular nuevo tamaño manteniendo proporción ───────────────────
            int w = bitmapOrientado.Width;
            int h = bitmapOrientado.Height;

            if (w > MaxWidth || h > MaxHeight)
            {
                double ratio = Math.Min((double)MaxWidth / w, (double)MaxHeight / h);
                w = (int)(w * ratio);
                h = (int)(h * ratio);
            }

            using var resized = bitmapOrientado.Resize(new SKImageInfo(w, h), SKFilterQuality.High);
            if (resized == null) return original;

            using var image = SKImage.FromBitmap(resized);
            using var output = new MemoryStream();
            image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality).SaveTo(output);

            return output.ToArray();
        }

        private static SKBitmap AplicarOrientacion(SKBitmap bitmap, SKEncodedOrigin origen)
        {
            // Si no hay rotación necesaria, devolver tal cual
            if (origen == SKEncodedOrigin.TopLeft || origen == SKEncodedOrigin.Default)
                return bitmap;

            var rotado = new SKBitmap(
                origen is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                        or SKEncodedOrigin.LeftBottom or SKEncodedOrigin.RightBottom
                    ? bitmap.Height  // ancho y alto se intercambian al rotar 90/270
                    : bitmap.Width,
                origen is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                        or SKEncodedOrigin.LeftBottom or SKEncodedOrigin.RightBottom
                    ? bitmap.Width
                    : bitmap.Height
            );

            using var canvas = new SKCanvas(rotado);
            canvas.Clear();

            var matrix = origen switch
            {
                SKEncodedOrigin.TopRight => SKMatrix.CreateScale(-1, 1),                          // Flip H
                SKEncodedOrigin.BottomRight => SKMatrix.CreateRotationDegrees(180,
                                                bitmap.Width / 2f, bitmap.Height / 2f),             // 180°
                SKEncodedOrigin.BottomLeft => SKMatrix.CreateScale(1, -1),                          // Flip V
                SKEncodedOrigin.LeftTop => CombinarMatrices(
                                                SKMatrix.CreateRotationDegrees(90),
                                                SKMatrix.CreateScale(-1, 1)),                        // Transponer
                SKEncodedOrigin.RightTop => SKMatrix.CreateRotationDegrees(90,
                                                bitmap.Height / 2f, bitmap.Height / 2f),            // 90° CW
                SKEncodedOrigin.RightBottom => CombinarMatrices(
                                                SKMatrix.CreateRotationDegrees(90),
                                                SKMatrix.CreateScale(-1, 1)),                        // Anti-transponer
                SKEncodedOrigin.LeftBottom => SKMatrix.CreateRotationDegrees(270,
                                                bitmap.Width / 2f, bitmap.Width / 2f),              // 270° CW
                _ => SKMatrix.Identity
            };

            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(bitmap, 0, 0);

            return rotado;
        }

        private static SKMatrix CombinarMatrices(SKMatrix a, SKMatrix b)
        {
            SKMatrix.Concat(ref a, a, b);
            return a;
        }
    }
}