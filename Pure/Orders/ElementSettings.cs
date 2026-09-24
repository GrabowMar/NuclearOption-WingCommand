namespace WingCommand
{
    /// <summary>Shape and doctrine an element flies when it differs from the wing's (spec WMC program §4). Element A always
    /// follows the wing; a merge forgets the element's own settings.</summary>
    internal sealed class ElementSettings
    {
        private readonly string[] shapes = new string[ElementRoster.MaxElements];
        private readonly WingDoctrine?[] doctrines = new WingDoctrine?[ElementRoster.MaxElements];

        public string ShapeOf(int e, string wingShape) => e > 0 && shapes[e] != null ? shapes[e] : wingShape;

        public WingDoctrine DoctrineOf(int e, WingDoctrine wing) => e > 0 && doctrines[e].HasValue ? doctrines[e].Value : wing;

        public void SetShape(int e, string id)
        {
            if (e > 0) shapes[e] = id;
        }

        public void SetDoctrine(int e, WingDoctrine d)
        {
            if (e > 0) doctrines[e] = d;
        }

        public void Forget(int e)
        {
            if (e <= 0) return;
            shapes[e] = null;
            doctrines[e] = null;
        }

        /// <summary>Forgets every element B-D with nobody in it (review P3 I2): a letter handed out again starts from the
        /// wing's settings however it emptied (merge, detach elsewhere, losses).</summary>
        public void ForgetEmpty(ElementRoster roster)
        {
            for (int e = 1; e < ElementRoster.MaxElements; e++)
                if (roster.Count(e) == 0) Forget(e);
        }

        public void Clear()
        {
            for (int e = 1; e < ElementRoster.MaxElements; e++) Forget(e);
        }
    }
}
