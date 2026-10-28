using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Itmam.App.Tray;

// Renders the Itmam icon (src/Itmam.App/Tray/AppIconArt.cs):
//   dotnet run --project tools/IconGenerator -- src/Itmam.App/Assets/Itmam.ico
//   dotnet run --project tools/IconGenerator -- --store src/Itmam.Package/Images
//   dotnet run --project tools/IconGenerator -- --listing docs/store/logos
switch (args)
{
    case ["--store", var folder]:
        WriteStoreImages(folder);
        return 0;
    case ["--listing", var folder]:
        WriteListingLogos(folder);
        WriteHeroArt(folder);
        return 0;
    case [var output] when !output.StartsWith('-'):
        WriteIco(output);
        return 0;
    default:
        Console.Error.WriteLine("Usage: IconGenerator <output.ico> | --store <folder> | --listing <folder>");
        return 2;
}

// A multi-size .ico (PNG-compressed entries, supported since Windows Vista).
static void WriteIco(string output)
{
    int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    var images = sizes.Select(size =>
    {
        using var bitmap = AppIconArt.Render(size);
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return (Size: size, Data: png.ToArray());
    }).ToArray();

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    using var file = File.Create(output);
    using var writer = new BinaryWriter(file);

    // ICONDIR
    writer.Write((ushort)0);            // reserved
    writer.Write((ushort)1);            // type: icon
    writer.Write((ushort)images.Length);

    // ICONDIRENTRY per image, then the PNG data
    var offset = 6 + (16 * images.Length);
    foreach (var (size, data) in images)
    {
        writer.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);           // palette colors
        writer.Write((byte)0);           // reserved
        writer.Write((ushort)1);         // color planes
        writer.Write((ushort)32);        // bits per pixel
        writer.Write(data.Length);
        writer.Write(offset);
        offset += data.Length;
    }

    foreach (var (_, data) in images)
    {
        writer.Write(data);
    }

    Console.WriteLine($"Wrote {output} ({string.Join(", ", sizes)} px)");
}

// MSIX visual assets (Package.appxmanifest), at every scale Windows asks for. Tiles keep a
// transparent background with the icon centered, so they follow the user's Start colors.
static void WriteStoreImages(string folder)
{
    Directory.CreateDirectory(folder);
    int[] scales = [100, 125, 150, 200, 400];
    (string Name, int Width, int Height, float IconShare)[] assets =
    [
        ("Square44x44Logo", 44, 44, 1f),     // app list, taskbar, title bar
        ("StoreLogo", 50, 50, 1f),           // Store and installer
        ("Square71x71Logo", 71, 71, 0.66f),  // small tile
        ("Square150x150Logo", 150, 150, 0.6f),
        ("Wide310x150Logo", 310, 150, 0.6f),
        ("Square310x310Logo", 310, 310, 0.5f),
        ("SplashScreen", 620, 300, 0.4f),
    ];

    var count = 0;
    foreach (var (name, width, height, share) in assets)
    {
        foreach (var scale in scales)
        {
            Save(Canvas(Scaled(width, scale), Scaled(height, scale), share), folder, $"{name}.scale-{scale}.png");
            count++;
        }
    }

    // Exact-size app icons for the taskbar and app list; "unplated" is drawn without a plate.
    foreach (var size in new[] { 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256 })
    {
        Save(AppIconArt.Render(size), folder, $"Square44x44Logo.targetsize-{size}.png");
        Save(AppIconArt.Render(size), folder, $"Square44x44Logo.targetsize-{size}_altform-unplated.png");
        Save(AppIconArt.Render(size), folder, $"Square44x44Logo.targetsize-{size}_altform-lightunplated.png");
        count += 3;
    }

    Console.WriteLine($"Wrote {count} images to {folder}");
}

static Bitmap Canvas(int width, int height, float iconShare)
{
    var canvas = new Bitmap(width, height);
    using var g = Graphics.FromImage(canvas);
    g.Clear(Color.Transparent);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    var size = (int)Math.Round(Math.Min(width, height) * iconShare);
    using var icon = AppIconArt.Render(size);
    g.DrawImage(icon, (width - size) / 2, (height - size) / 2, size, size);
    return canvas;
}

static void Save(Bitmap image, string folder, string fileName)
{
    using (image)
    {
        image.Save(Path.Combine(folder, fileName), ImageFormat.Png);
    }
}

// Windows expects 71 px at 125 % to be 89 px (88.75 rounded half up), 150 px at 125 % to be 188, …
static int Scaled(int pixels, int scale) => (int)Math.Round(pixels * scale / 100.0, MidpointRounding.AwayFromZero);

// Partner Center → Store listings → Store logos: the 300×300 tile icon, and box/poster art on the
// brand gradient (full bleed, no transparency) with the icon centered.
static void WriteListingLogos(string folder)
{
    Directory.CreateDirectory(folder);
    Save(AppIconArt.Render(300), folder, "app-tile-icon-300x300.png");
    Save(Art(1080, 1080, 0.56f), folder, "box-art-1080x1080.png");
    Save(Art(720, 1080, 0.62f), folder, "poster-art-720x1080.png");
    Console.WriteLine($"Wrote 3 listing logos to {folder}");
}

static Bitmap Art(int width, int height, float iconShare)
{
    var canvas = new Bitmap(width, height);
    using var g = Graphics.FromImage(canvas);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    using (var gradient = new LinearGradientBrush(new Rectangle(0, 0, width, height), Color.FromArgb(0x13, 0x8F, 0x9C), Color.FromArgb(0x08, 0x4A, 0x5E), 60f))
    {
        g.FillRectangle(gradient, 0, 0, width, height);
    }

    if (iconShare <= 0)
    {
        return canvas; // background only (the hero art draws its own content)
    }

    // A soft light behind the icon, then the icon.
    var size = (int)Math.Round(width * iconShare);
    var x = (width - size) / 2;
    var y = (height - size) / 2;
    using (var glow = new GraphicsPath())
    {
        glow.AddEllipse(x - size * 0.35f, y - size * 0.35f, size * 1.7f, size * 1.7f);
        using var brush = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(70, 0x9F, 0xF5, 0xEA), SurroundColors = [Color.FromArgb(0, 0x9F, 0xF5, 0xEA)] };
        g.FillPath(brush, glow);
    }

    using var icon = AppIconArt.Render(size);
    g.DrawImage(icon, x, y, size, size);
    return canvas;
}

// 16:9 Super hero art (top of the Store listing). The Store forbids the product name in it, so it shows
// the product instead: the suggestion bar above a word being typed, next to the icon.
static void WriteHeroArt(string folder)
{
    foreach (var scale in new[] { 1, 2 })
    {
        Save(Hero(1920 * scale, 1080 * scale), folder, $"super-hero-art-{1920 * scale}x{1080 * scale}.png");
    }
}

static Bitmap Hero(int width, int height)
{
    var s = width / 1920f;
    var canvas = Art(width, height, 0f); // gradient + glow only
    using var g = Graphics.FromImage(canvas);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

    // Icon on the right (Arabic reading start), the typing scene on the left of it.
    var iconSize = (int)(300 * s);
    using (var icon = AppIconArt.Render(iconSize))
    {
        g.DrawImage(icon, (int)(1320 * s), (height - iconSize) / 2, iconSize, iconSize);
    }

    using var rtl = new StringFormat { FormatFlags = StringFormatFlags.DirectionRightToLeft, Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center }; // Near = right edge in RTL
    using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.DirectionRightToLeft };

    // The typed line with its caret.
    using var lineFont = new Font("Segoe UI", 64 * s, FontStyle.Regular, GraphicsUnit.Pixel);
    var line = new RectangleF(260 * s, 560 * s, 960 * s, 110 * s);
    g.DrawString("التحيات والسلا", lineFont, Brushes.White, line, rtl);
    var typedWidth = g.MeasureString("التحيات والسلا", lineFont, PointF.Empty, rtl).Width;
    var caretX = line.Right - typedWidth - (6 * s);
    using (var caret = new SolidBrush(Color.FromArgb(0x5E, 0xEA, 0xD4)))
    {
        g.FillRectangle(caret, caretX, line.Top + (18 * s), 4 * s, line.Height - (36 * s));
    }

    // The suggestion bar above the word, like the real one: dark, first item highlighted, right edge at the caret.
    string[] words = ["والسلام", "والسلامة", "والسلاح"];
    using var barFont = new Font("Segoe UI", 40 * s, FontStyle.Regular, GraphicsUnit.Pixel);
    var itemWidths = words.Select(w => g.MeasureString(w, barFont).Width + (56 * s)).ToArray();
    var barWidth = itemWidths.Sum() + (16 * s);
    var bar = new RectangleF(caretX + (12 * s) - barWidth, 400 * s, barWidth, 104 * s);
    using (var path = RoundedRect(bar, 22 * s))
    using (var fill = new SolidBrush(Color.FromArgb(0x2C, 0x2C, 0x2C)))
    {
        g.FillPath(fill, path);
    }

    var x = bar.Right - (8 * s);
    for (var i = 0; i < words.Length; i++)
    {
        var item = new RectangleF(x - itemWidths[i], bar.Top + (8 * s), itemWidths[i], bar.Height - (16 * s));
        if (i == 0)
        {
            using var pick = RoundedRect(item, 14 * s);
            using var teal = new SolidBrush(Color.FromArgb(0x1A, 0x9E, 0xA0));
            g.FillPath(teal, pick);
        }

        g.DrawString(words[i], barFont, Brushes.White, item, center);
        x -= itemWidths[i];
    }

    return canvas;
}

static GraphicsPath RoundedRect(RectangleF r, float radius)
{
    var d = radius * 2;
    var path = new GraphicsPath();
    path.AddArc(r.Left, r.Top, d, d, 180, 90);
    path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}
