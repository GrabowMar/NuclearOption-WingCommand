using System.Globalization;

namespace WingCommand
{
    /// <summary>Paging a list into fixed pages (SUPPLY's tiles and bases, LOADOUT's tiles): an empty list still has one page, a
    /// page past the last is pulled back, and the label reads "1 / 2".</summary>
    internal static class Pages
    {
        public static int Count(int items, int perPage) => items <= 0 || perPage <= 0 ? 1 : (items + perPage - 1) / perPage;

        public static int Clamp(int page, int items, int perPage)
        {
            int last = Count(items, perPage) - 1;
            return page < 0 ? 0 : page > last ? last : page;
        }

        public static int First(int page, int perPage) => page * perPage;

        /// <summary>The page item <paramref name="index"/> sits on.</summary>
        public static int Of(int index, int perPage) => index <= 0 || perPage <= 0 ? 0 : index / perPage;

        public static string Label(int page, int pages) =>
            (page + 1).ToString(CultureInfo.InvariantCulture) + " / " + pages.ToString(CultureInfo.InvariantCulture);
    }
}
