using System.Text;

namespace ForceBreak.Core;

/// <summary>Notes belong to the user, independently of schedules and overlay windows.</summary>
public sealed class OverlayNotes(string path)
{
    public string Text { get; set; } = "";
    public void Load() { if (File.Exists(path)) Text = File.ReadAllText(path, Encoding.UTF8); }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var bytes = Encoding.UTF8.GetBytes(Text);
            stream.Write(bytes); stream.Flush(true);
        }
        File.Move(temp, path, true);
    }
}
