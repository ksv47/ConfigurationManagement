using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        string outputPath = args.Length > 0 ? args[0] : "app.ico";
        int[] sizes = { 16, 32, 48, 256 };

        using (var stream = new FileStream(outputPath, FileMode.Create))
        using (var writer = new BinaryWriter(stream))
        {
            // ICONDIR
            writer.Write((ushort)0);          // Reserved
            writer.Write((ushort)1);          // Type: icon
            writer.Write((ushort)sizes.Length); // Count

            // ICONDIRENTRY + image data
            var imageDatas = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                using (var bmp = DrawIcon(size))
                {
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        imageDatas[i] = ms.ToArray();
                    }
                }
            }

            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                byte dim = (byte)(size >= 256 ? 0 : size);
                writer.Write(dim);                    // Width
                writer.Write(dim);                    // Height
                writer.Write((byte)0);                // Color count
                writer.Write((byte)0);                // Reserved
                writer.Write((ushort)1);              // Planes
                writer.Write((ushort)32);             // Bit count
                writer.Write((uint)imageDatas[i].Length); // Size
                writer.Write((uint)offset);           // Offset
                offset += imageDatas[i].Length;
            }

            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write(imageDatas[i]);
            }
        }

        Console.WriteLine($"Icon generated: {outputPath}");
    }

    static Bitmap DrawIcon(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float s = size / 32f; // scale factor relative to 32x32 design

            // Background: rounded square
            using (var bgBrush = new SolidBrush(Color.FromArgb(37, 99, 235))) // #2563EB
            {
                float radius = 6f * s;
                var rect = new RectangleF(0, 0, size, size);
                using (var path = RoundedRect(rect, radius))
                {
                    g.FillPath(bgBrush, path);
                }
            }

            // Database cylinder (white)
            using (var whiteBrush = new SolidBrush(Color.White))
            {
                // Top ellipse
                g.FillEllipse(whiteBrush, 8*s, 9*s, 16*s, 6*s);
                // Body
                g.FillRectangle(whiteBrush, 8*s, 9*s, 16*s, 10*s);
                // Bottom curve
                using (var bottomPath = new GraphicsPath())
                {
                    bottomPath.AddArc(8*s, 9*s, 16*s, 6*s, 0, 180);
                    bottomPath.AddLine(24*s, 9*s, 24*s, 19*s);
                    bottomPath.AddArc(8*s, 13*s, 16*s, 6*s, 180, 180);
                    bottomPath.CloseFigure();
                    g.FillPath(whiteBrush, bottomPath);
                }
            }

            // Gear (amber) on top of database
            using (var gearBrush = new SolidBrush(Color.FromArgb(251, 191, 36))) // #FBBF24
            {
                // Gear teeth
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * 45.0 * Math.PI / 180.0;
                    float cx = 16*s + (float)(Math.Cos(angle) * 4.5*s);
                    float cy = 16*s + (float)(Math.Sin(angle) * 4.5*s);
                    g.FillEllipse(gearBrush, cx - 1.6f*s, cy - 1.6f*s, 3.2f*s, 3.2f*s);
                }
                // Gear body
                g.FillEllipse(gearBrush, 11*s, 11*s, 10*s, 10*s);
                // Center hole (background color)
                using (var holeBrush = new SolidBrush(Color.FromArgb(37, 99, 235)))
                {
                    g.FillEllipse(holeBrush, 13.8f*s, 13.8f*s, 4.4f*s, 4.4f*s);
                }
            }
        }
        return bmp;
    }

    static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}