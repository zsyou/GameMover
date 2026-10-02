namespace GameMover.Services
{
    public static class ByteSize
    {
        public static string Format(long bytes)
        {
            if (bytes < 0)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? bytes + " B" : value.ToString("0.##") + " " + units[unit];
        }
    }
}
