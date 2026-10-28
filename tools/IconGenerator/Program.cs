using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Itmam.App.Tray;

// Renders the Itmam icon (src/Itmam.App/Tray/AppIconArt.cs):
//   dotnet run --project tools/IconGenerator -- src/Itmam.App/Assets/Itmam.ico
//   dotnet run --project tools/IconGenerator -- --store src/Itmam.Package/Images
switch (args)
{
    case ["--store", var folder]:
        WriteStoreImages(folder);
        return 0;
    case [var output] when !output.StartsWith('-'):
        WriteIco(output);
        return 0;
    default:
        Console.Error.WriteLine("Usage: IconGenerator <output.ico> | --store <folder>");
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
