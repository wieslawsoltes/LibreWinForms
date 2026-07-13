namespace System.Windows.Forms.Design.Behavior
{
    public enum SnapLineType
    {
        Top,
        Bottom,
        Left,
        Right,
        Horizontal,
        Vertical,
        Baseline
    }

    public enum SnapLinePriority
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Always = 4
    }

    public sealed class SnapLine
    {
        internal const string Margin = "Margin";
        internal const string MarginRight = Margin + ".Right";
        internal const string MarginLeft = Margin + ".Left";
        internal const string MarginBottom = Margin + ".Bottom";
        internal const string MarginTop = Margin + ".Top";
        internal const string Padding = "Padding";
        internal const string PaddingRight = Padding + ".Right";
        internal const string PaddingLeft = Padding + ".Left";
        internal const string PaddingBottom = Padding + ".Bottom";
        internal const string PaddingTop = Padding + ".Top";

        public SnapLine(SnapLineType type, int offset)
            : this(type, offset, filter: null, SnapLinePriority.Low)
        {
        }

        public SnapLine(SnapLineType type, int offset, string? filter)
            : this(type, offset, filter, SnapLinePriority.Low)
        {
        }

        public SnapLine(SnapLineType type, int offset, SnapLinePriority priority)
            : this(type, offset, filter: null, priority)
        {
        }

        public SnapLine(SnapLineType type, int offset, string? filter, SnapLinePriority priority)
        {
            SnapLineType = type;
            Offset = offset;
            Filter = filter;
            Priority = priority;
        }

        public string? Filter { get; }

        public bool IsHorizontal => SnapLineType is SnapLineType.Top
            or SnapLineType.Bottom
            or SnapLineType.Horizontal
            or SnapLineType.Baseline;

        public bool IsVertical => SnapLineType is SnapLineType.Left
            or SnapLineType.Right
            or SnapLineType.Vertical;

        public int Offset { get; private set; }

        public SnapLinePriority Priority { get; }

        public SnapLineType SnapLineType { get; }

        public void AdjustOffset(int adjustment)
        {
            Offset += adjustment;
        }

        public static bool ShouldSnap(SnapLine line1, SnapLine line2)
        {
            if (line1.SnapLineType != line2.SnapLineType)
            {
                return false;
            }

            if (line1.Filter is null && line2.Filter is null)
            {
                return true;
            }

            if (line1.Filter is null || line2.Filter is null)
            {
                return false;
            }

            if (line1.Filter.Contains(Margin))
            {
                return (line1.Filter.Equals(MarginRight) && (line2.Filter.Equals(MarginLeft) || line2.Filter.Equals(PaddingRight)))
                    || (line1.Filter.Equals(MarginLeft) && (line2.Filter.Equals(MarginRight) || line2.Filter.Equals(PaddingLeft)))
                    || (line1.Filter.Equals(MarginTop) && (line2.Filter.Equals(MarginBottom) || line2.Filter.Equals(PaddingTop)))
                    || (line1.Filter.Equals(MarginBottom) && (line2.Filter.Equals(MarginTop) || line2.Filter.Equals(PaddingBottom)));
            }

            if (line1.Filter.Contains(Padding))
            {
                return (line1.Filter.Equals(PaddingLeft) && line2.Filter.Equals(MarginLeft))
                    || (line1.Filter.Equals(PaddingRight) && line2.Filter.Equals(MarginRight))
                    || (line1.Filter.Equals(PaddingTop) && line2.Filter.Equals(MarginTop))
                    || (line1.Filter.Equals(PaddingBottom) && line2.Filter.Equals(MarginBottom));
            }

            return line1.Filter.Equals(line2.Filter);
        }

        public override string ToString()
        {
            return $"SnapLine: {{type = {SnapLineType}, offset = {Offset}, priority = {Priority}, filter = {Filter ?? "<null>"}}}";
        }
    }
}
