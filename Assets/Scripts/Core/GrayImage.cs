namespace ShapesKinect.Core
{
    /// <summary>
    /// A grey picture from the color camera, at a fraction of its size to keep finding cards in it
    /// quick: pixel (x, y) here covers <see cref="Scale"/> by <see cref="Scale"/> color pixels.
    /// Row 0 is the top.
    /// </summary>
    public sealed class GrayImage
    {
        public int Width { get; }
        public int Height { get; }
        public int Scale { get; }
        public byte[] Pixels { get; }
        /// <summary>Goes up with each new picture, so readers can tell when one arrives.</summary>
        public long Number { get; set; }

        public GrayImage(int width, int height, int scale)
        {
            Width = width;
            Height = height;
            Scale = scale;
            Pixels = new byte[width * height];
        }

        public byte this[int x, int y] => Pixels[y * Width + x];

        /// <summary>Where the middle of pixel (x, y) is in the full-size color picture.</summary>
        public System.Numerics.Vector2 ToColor(float x, float y) =>
            new System.Numerics.Vector2((x + 0.5f) * Scale - 0.5f, (y + 0.5f) * Scale - 0.5f);

        /// <summary>
        /// Fills this picture from a color picture <paramref name="width"/> by <paramref name="height"/>
        /// in RGBA bytes. Each pixel here is the average of two opposite corners of its
        /// <see cref="Scale"/> by <see cref="Scale"/> block, which is quick and plenty for finding cards.
        /// </summary>
        public void FillFromRgba(byte[] rgba, int width, int height)
        {
            var s = Scale;
            var rowBytes = width * 4;
            var across = (s - 1) * (rowBytes + 4);
            for (var y = 0; y < Height; y++)
            {
                var at = y * s * rowBytes;
                var to = y * Width;
                for (var x = 0; x < Width; x++, at += s * 4)
                {
                    var b = at + across;
                    // Red, twice green and blue, roughly as bright as the eye sees them.
                    var sum = rgba[at] + (rgba[at + 1] << 1) + rgba[at + 2] + rgba[b] + (rgba[b + 1] << 1) + rgba[b + 2];
                    Pixels[to + x] = (byte)(sum >> 3);
                }
            }
            Number++;
        }
    }
}
