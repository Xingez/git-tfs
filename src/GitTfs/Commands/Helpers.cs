
namespace GitTfs.Commands
{
    using global::GitTfs.Util;
    public static class Helpers
    {
        public static OptionSet Merge(this OptionSet options, params OptionSet[] others)
        {
            var merged = new OptionSet();
            Merge(merged, options);
            foreach (var other in others)
                Merge(merged, other);
            return merged;
        }

        private static void Merge(OptionSet target, OptionSet source)
        {
            foreach (var option in source)
            {
                if (!target.Contains(option.GetNames()[0]))
                    target.Add(option);
            }
        }
    }
}
