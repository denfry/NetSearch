using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Output path: first arg, else repo-root NetSearch.ico (two levels up from tools/IconGen).
var outPath = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "NetSearch.ico"));

int[] sizes = { 16, 32, 48, 64, 256 };
var pngs = sizes.Select(s => (size: s, data: RenderPng(s))).ToList();
WriteIco(outPath, pngs);

// Self-verify: re-read ICONDIR and assert the image count.
using (var fs = File.OpenRead(outPath))
{
    var hdr = new byte[6];
    fs.ReadExactly(hdr, 0, 6);
    int count = BitConverter.ToUInt16(hdr, 4);
    if (count != sizes.Length) { Console.Error.WriteLine($"ICO verify FAILED: {count} != {sizes.Length}"); return 1; }
}
Console.WriteLine($"Wrote {outPath} ({pngs.Count} sizes)");
return 0;

static byte[] RenderPng(int s)
{
    using var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float u = s / 16f; // scale unit

        // Rounded accent tile background (accent #2D6CDF).
        using var bg = new SolidBrush(Color.FromArgb(0x2D, 0x6C, 0xDF));
        FillRoundedRect(g, bg, new RectangleF(0.5f * u, 0.5f * u, 15f * u, 15f * u), 3.2f * u);

        // Stacked-disk motif (three ellipses) in light tint, lower-left.
        using var disk = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
        using var diskEdge = new Pen(Color.FromArgb(120, 0, 0, 0), Math.Max(1f, 0.4f * u));
        for (int i = 0; i < 3; i++)
        {
            var y = (9.2f - i * 1.7f) * u;
            var e = new RectangleF(3.0f * u, y, 6.4f * u, 1.7f * u);
            g.FillEllipse(disk, e);
            g.DrawEllipse(diskEdge, e);
        }

        // Magnifier (ring + handle), upper-right.
        using var ringPen = new Pen(Color.White, Math.Max(1.4f, 1.6f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var ring = new RectangleF(7.6f * u, 2.2f * u, 5.0f * u, 5.0f * u);
        g.DrawEllipse(ringPen, ring);
        g.DrawLine(ringPen, 12.2f * u, 6.8f * u, 13.9f * u, 8.5f * u);
    }
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

static void FillRoundedRect(Graphics g, Brush b, RectangleF r, float radius)
{
    using var p = new GraphicsPath();
    float d = radius * 2;
    p.AddArc(r.X, r.Y, d, d, 180, 90);
    p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    p.CloseFigure();
    g.FillPath(b, p);
}

static void WriteIco(string path, List<(int size, byte[] data)> imgs)
{
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);
    w.Write((ushort)0);              // reserved
    w.Write((ushort)1);              // type = icon
    w.Write((ushort)imgs.Count);     // image count
    int offset = 6 + imgs.Count * 16;
    foreach (var (size, data) in imgs)
    {
        w.Write((byte)(size >= 256 ? 0 : size)); // width  (0 => 256)
        w.Write((byte)(size >= 256 ? 0 : size)); // height (0 => 256)
        w.Write((byte)0);            // palette
        w.Write((byte)0);            // reserved
        w.Write((ushort)1);          // color planes
        w.Write((ushort)32);         // bpp
        w.Write((uint)data.Length);  // bytes in resource
        w.Write((uint)offset);       // image offset
        offset += data.Length;
    }
    foreach (var (_, data) in imgs) w.Write(data);
}
