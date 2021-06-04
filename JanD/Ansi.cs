namespace JanD
{
     public static class Ansi
    {
        public static readonly string DefaultForegroundColor = "\u001b[39m";
        public static readonly string DefaultBackgroundColor = "\u001b[49m";

        public static string ForegroundColor(string str, byte red, byte green, byte blue)
            => $"\x1b[38;2;{red};{green};{blue}m{str}{DefaultForegroundColor}";

        public static string BackgroundColor(string str, byte red, byte green, byte blue)
            => $"\x1b[48;2;{red};{green};{blue}m{str}{DefaultBackgroundColor}";
    }
}