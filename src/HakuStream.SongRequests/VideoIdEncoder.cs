using System.Text;

namespace HakuStream.SongRequests;

public static class VideoIdEncoder
{
    public static string ToFilesystemSafe(string videoId)
    {
        var sb = new StringBuilder(videoId.Length * 2);

        foreach (var c in videoId)
        {
            if (c == '_')
            {
                sb.Append("__");
            }
            else if (char.IsUpper(c))
            {
                sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
