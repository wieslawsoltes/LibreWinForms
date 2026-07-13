using System.Collections;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using System.Windows.Forms.Design.Behavior;

namespace System.ComponentModel.Design
{
    internal enum PortableDesignerPointerOperation
    {
        None,
        Move,
        ResizeLeft,
        ResizeTop,
        ResizeRight,
        ResizeBottom,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight
    }

    internal sealed class PortableDesignerLayoutService
    {
        private const int SnapLineTolerance = 8;
        private const int MinGridSize = 2;
        private const int MaxGridSize = 200;

        private readonly PortableDesignerHost _host;
        private SnapLineSnapshot[] _candidateLines = new SnapLineSnapshot[16];
        private SnapLineSnapshot[] _targetLines = new SnapLineSnapshot[64];
        private int _candidateLineCount;
        private int _targetLineCount;
        private Control? _candidate;
        private Control? _parent;
        private Point _candidateDisplayOffset;
        private Size _candidateInitialSize;
        private DesignerLayoutOptions _options = DesignerLayoutOptions.Default;
        private bool _toolPlacementActive;

        internal PortableDesignerLayoutService(PortableDesignerHost host)
        {
            _host = host;
        }

        internal void BeginManipulation(Control candidate, Control parent)
        {
            Reset();
            _candidate = candidate;
            _parent = parent;
            _candidateInitialSize = candidate.Size;
            _options = ReadOptions();

            if (!_options.UseSnapLines)
                return;

            if (TryTranslatePoint(candidate, parent, Point.Empty, out Point displayedOrigin))
            {
                _candidateDisplayOffset = new Point(
                    displayedOrigin.X - candidate.Left,
                    displayedOrigin.Y - candidate.Top);
            }

            CacheCandidateLines(candidate);
            CacheTargetLines(parent, candidate);
        }

        internal Rectangle GetManipulatedBounds(
            Control control,
            Rectangle initialBounds,
            PortableDesignerPointerOperation operation,
            int deltaX,
            int deltaY)
        {
            bool useSnapLines = _options.UseSnapLines && !IsAltPressed();
            bool useGrid = !useSnapLines && _options.SnapToGrid;
            int minimumWidth = Math.Max(1, control.MinimumSize.Width);
            int minimumHeight = Math.Max(1, control.MinimumSize.Height);
            if (useGrid)
            {
                minimumWidth = Math.Max(minimumWidth, _options.GridSize.Width);
                minimumHeight = Math.Max(minimumHeight, _options.GridSize.Height);
            }

            Rectangle bounds = CalculateRawBounds(
                initialBounds,
                operation,
                deltaX,
                deltaY,
                minimumWidth,
                minimumHeight);
            if (useSnapLines)
                return SnapManipulatedBounds(bounds, operation, minimumWidth, minimumHeight);
            if (useGrid)
                return SnapManipulatedBoundsToGrid(bounds, operation, minimumWidth, minimumHeight);
            return bounds;
        }

        internal void EndManipulation(Control? candidate)
        {
            if (candidate is null || ReferenceEquals(candidate, _candidate))
                Reset();
        }

        internal void BeginToolPlacement(Control parent)
        {
            Reset();
            _parent = parent;
            _toolPlacementActive = true;
            _options = ReadOptions();
            if (_options.UseSnapLines)
                CacheTargetLines(parent, candidate: null);
        }

        internal Rectangle GetToolBounds(Control parent, Point start, Point end)
        {
            EnsureToolPlacement(parent);
            bool useSnapLines = _options.UseSnapLines && !IsAltPressed();
            bool useGrid = !useSnapLines && _options.SnapToGrid;
            if (useGrid)
                return CreateGridToolBounds(start, end);

            Rectangle bounds = CreateBounds(start, end);
            return useSnapLines ? SnapToolBounds(bounds) : bounds;
        }

        internal Point GetToolLocation(Control parent, Point point)
        {
            EnsureToolPlacement(parent);
            bool useSnapLines = _options.UseSnapLines && !IsAltPressed();
            if (!useSnapLines && _options.SnapToGrid)
            {
                return new Point(
                    SnapCoordinate(point.X, _options.GridSize.Width),
                    SnapCoordinate(point.Y, _options.GridSize.Height));
            }

            if (!useSnapLines)
                return point;

            SnapAdjustment adjustmentX = FindToolAdjustment(SnapLineType.Left, point.X);
            SnapAdjustment adjustmentY = FindToolAdjustment(SnapLineType.Top, point.Y);
            return new Point(point.X + adjustmentX.Value, point.Y + adjustmentY.Value);
        }

        internal void EndToolPlacement()
        {
            Reset();
        }

        internal static bool TryTranslatePoint(Control source, Control target, Point point, out Point translated)
        {
            Control sourceRoot = source;
            while (sourceRoot.Parent is Control sourceParent)
                sourceRoot = sourceParent;

            Control targetRoot = target;
            while (targetRoot.Parent is Control targetParent)
                targetRoot = targetParent;

            if (!ReferenceEquals(sourceRoot, targetRoot))
            {
                translated = Point.Empty;
                return false;
            }

            translated = target.PointToClient(source.PointToScreen(point));
            return true;
        }

        internal void Reset()
        {
            Array.Clear(_candidateLines, 0, _candidateLineCount);
            Array.Clear(_targetLines, 0, _targetLineCount);
            _candidateLineCount = 0;
            _targetLineCount = 0;
            _candidate = null;
            _parent = null;
            _candidateDisplayOffset = Point.Empty;
            _candidateInitialSize = Size.Empty;
            _options = DesignerLayoutOptions.Default;
            _toolPlacementActive = false;
        }

        private void EnsureToolPlacement(Control parent)
        {
            if (_toolPlacementActive && ReferenceEquals(_parent, parent))
                return;

            BeginToolPlacement(parent);
        }

        private Rectangle SnapManipulatedBounds(
            Rectangle bounds,
            PortableDesignerPointerOperation operation,
            int minimumWidth,
            int minimumHeight)
        {
            if (_candidateLineCount == 0 || _targetLineCount == 0)
                return bounds;

            Rectangle displayedBounds = bounds;
            displayedBounds.Offset(_candidateDisplayOffset);
            int adjustmentX = FindSnapLineAdjustment(displayedBounds, operation, horizontalAxis: true);
            int adjustmentY = FindSnapLineAdjustment(displayedBounds, operation, horizontalAxis: false);
            return ApplyAdjustments(bounds, operation, adjustmentX, adjustmentY, minimumWidth, minimumHeight);
        }

        private Rectangle SnapManipulatedBoundsToGrid(
            Rectangle bounds,
            PortableDesignerPointerOperation operation,
            int minimumWidth,
            int minimumHeight)
        {
            if (operation == PortableDesignerPointerOperation.Move)
            {
                return new Rectangle(
                    SnapCoordinate(bounds.Left, _options.GridSize.Width),
                    SnapCoordinate(bounds.Top, _options.GridSize.Height),
                    bounds.Width,
                    bounds.Height);
            }

            int left = bounds.Left;
            int top = bounds.Top;
            int right = bounds.Right;
            int bottom = bounds.Bottom;
            if (ChangesLeft(operation))
                left = Math.Min(SnapCoordinate(left, _options.GridSize.Width), right - minimumWidth);
            else if (ChangesRight(operation))
                right = Math.Max(SnapCoordinate(right, _options.GridSize.Width), left + minimumWidth);

            if (ChangesTop(operation))
                top = Math.Min(SnapCoordinate(top, _options.GridSize.Height), bottom - minimumHeight);
            else if (ChangesBottom(operation))
                bottom = Math.Max(SnapCoordinate(bottom, _options.GridSize.Height), top + minimumHeight);

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private Rectangle CreateGridToolBounds(Point start, Point end)
        {
            Point snappedStart = new(
                SnapCoordinate(start.X, _options.GridSize.Width),
                SnapCoordinate(start.Y, _options.GridSize.Height));
            Point snappedEnd = new(
                SnapCoordinate(end.X, _options.GridSize.Width),
                SnapCoordinate(end.Y, _options.GridSize.Height));

            if (start.X != end.X && snappedStart.X == snappedEnd.X)
                snappedEnd.X += end.X > start.X ? _options.GridSize.Width : -_options.GridSize.Width;
            if (start.Y != end.Y && snappedStart.Y == snappedEnd.Y)
                snappedEnd.Y += end.Y > start.Y ? _options.GridSize.Height : -_options.GridSize.Height;

            return CreateBounds(snappedStart, snappedEnd);
        }

        private Rectangle SnapToolBounds(Rectangle bounds)
        {
            if (bounds.IsEmpty || _targetLineCount == 0)
                return bounds;

            SnapAdjustment leftAdjustment = FindToolAdjustment(SnapLineType.Left, bounds.Left);
            SnapAdjustment rightAdjustment = FindToolAdjustment(SnapLineType.Right, bounds.Right - 1);
            SnapAdjustment topAdjustment = FindToolAdjustment(SnapLineType.Top, bounds.Top);
            SnapAdjustment bottomAdjustment = FindToolAdjustment(SnapLineType.Bottom, bounds.Bottom - 1);
            int adjustmentX = ChooseAxisAdjustment(leftAdjustment, rightAdjustment);
            int adjustmentY = ChooseAxisAdjustment(topAdjustment, bottomAdjustment);
            bounds.Offset(adjustmentX, adjustmentY);
            return bounds;
        }

        private int FindSnapLineAdjustment(
            Rectangle displayedBounds,
            PortableDesignerPointerOperation operation,
            bool horizontalAxis)
        {
            int bestAdjustment = 0;
            int bestDistance = SnapLineTolerance + 1;
            int bestPriority = int.MinValue;
            int bestOrder = int.MaxValue;

            for (int candidateIndex = 0; candidateIndex < _candidateLineCount; candidateIndex++)
            {
                SnapLine candidateLine = _candidateLines[candidateIndex].Line;
                if (horizontalAxis != candidateLine.IsVertical
                    || !CandidateLineMovesWithOperation(candidateLine, operation))
                {
                    continue;
                }

                int candidateCoordinate = GetCandidateCoordinate(candidateLine, displayedBounds);
                for (int targetIndex = 0; targetIndex < _targetLineCount; targetIndex++)
                {
                    SnapLineSnapshot target = _targetLines[targetIndex];
                    if (horizontalAxis != target.Line.IsVertical
                        || !SnapLine.ShouldSnap(candidateLine, target.Line))
                    {
                        continue;
                    }

                    int adjustment = target.Offset - candidateCoordinate;
                    int distance = Math.Abs(adjustment);
                    if (distance > SnapLineTolerance)
                        continue;

                    int priority = Math.Max((int)candidateLine.Priority, (int)target.Line.Priority);
                    int order = candidateIndex * _targetLineCount + targetIndex;
                    if (distance < bestDistance
                        || (distance == bestDistance && priority > bestPriority)
                        || (distance == bestDistance && priority == bestPriority && order < bestOrder))
                    {
                        bestAdjustment = adjustment;
                        bestDistance = distance;
                        bestPriority = priority;
                        bestOrder = order;
                    }
                }
            }

            return bestAdjustment;
        }

        private SnapAdjustment FindToolAdjustment(SnapLineType lineType, int coordinate)
        {
            int bestAdjustment = 0;
            int bestDistance = SnapLineTolerance + 1;
            int bestPriority = int.MinValue;
            bool found = false;
            for (int index = 0; index < _targetLineCount; index++)
            {
                SnapLineSnapshot target = _targetLines[index];
                if (target.Line.SnapLineType != lineType || target.Line.Filter is not null)
                    continue;

                int adjustment = target.Offset - coordinate;
                int distance = Math.Abs(adjustment);
                int priority = (int)target.Line.Priority;
                if (distance <= SnapLineTolerance
                    && (distance < bestDistance || (distance == bestDistance && priority > bestPriority)))
                {
                    bestAdjustment = adjustment;
                    bestDistance = distance;
                    bestPriority = priority;
                    found = true;
                }
            }

            return new SnapAdjustment(found, bestAdjustment);
        }

        private int GetCandidateCoordinate(SnapLine line, Rectangle displayedBounds)
        {
            int offset = line.Offset;
            if (IsRightAnchored(line))
                offset += displayedBounds.Width - _candidateInitialSize.Width;
            if (IsBottomAnchored(line))
                offset += displayedBounds.Height - _candidateInitialSize.Height;
            return line.IsVertical ? displayedBounds.Left + offset : displayedBounds.Top + offset;
        }

        private void CacheCandidateLines(Control candidate)
        {
            if (!TryGetSnapLines(_host.GetDesigner(candidate), out IList lines))
                return;

            for (int index = 0; index < lines.Count; index++)
            {
                if (lines[index] is SnapLine line)
                    AddCandidateLine(line);
            }
        }

        private void CacheTargetLines(Control parent, Control? candidate)
        {
            CacheTargetDesignerLines(parent, parent);
            for (int index = 0; index < parent.Controls.Count; index++)
            {
                Control target = parent.Controls[index];
                if (!ReferenceEquals(target, candidate))
                    CacheTargetDesignerLines(target, parent);
            }
        }

        private void CacheTargetDesignerLines(Control target, Control parent)
        {
            if (!TryGetSnapLines(_host.GetDesigner(target), out IList lines)
                || !TryTranslatePoint(target, parent, Point.Empty, out Point origin))
            {
                return;
            }

            for (int index = 0; index < lines.Count; index++)
            {
                if (lines[index] is not SnapLine line)
                    continue;

                int absoluteOffset = line.IsVertical
                    ? origin.X + line.Offset
                    : origin.Y + line.Offset;
                AddTargetLine(line, absoluteOffset);
            }
        }

        private static bool TryGetSnapLines(IDesigner? designer, out IList lines)
        {
            if (designer is PortableControlDesigner portableDesigner)
            {
                if (portableDesigner.ParticipatesWithSnapLinesForLayout)
                {
                    lines = portableDesigner.SnapLinesForLayout;
                    return true;
                }
            }
            else if (designer is ControlDesigner controlDesigner
                && controlDesigner.ParticipatesWithSnapLines)
            {
                lines = controlDesigner.SnapLines;
                return true;
            }

            lines = null!;
            return false;
        }

        private void AddCandidateLine(SnapLine line)
        {
            EnsureCapacity(ref _candidateLines, _candidateLineCount + 1);
            _candidateLines[_candidateLineCount++] = new SnapLineSnapshot(line, line.Offset);
        }

        private void AddTargetLine(SnapLine line, int absoluteOffset)
        {
            EnsureCapacity(ref _targetLines, _targetLineCount + 1);
            _targetLines[_targetLineCount++] = new SnapLineSnapshot(line, absoluteOffset);
        }

        private static void EnsureCapacity(ref SnapLineSnapshot[] snapshots, int required)
        {
            if (required <= snapshots.Length)
                return;

            int capacity = Math.Max(required, snapshots.Length * 2);
            Array.Resize(ref snapshots, capacity);
        }

        private DesignerLayoutOptions ReadOptions()
        {
            object? service = ((IServiceProvider)_host).GetService(typeof(DesignerOptionService));
            if (service is WindowsFormsDesignerOptionService windowsFormsOptions)
            {
                return DesignerLayoutOptions.Create(
                    windowsFormsOptions.GridSize,
                    windowsFormsOptions.SnapToGrid,
                    windowsFormsOptions.UseSnapLines);
            }

            if (service is IDesignerOptionService optionService)
            {
                Size gridSize = TryGetOptionValue(optionService, nameof(WindowsFormsDesignerOptionService.GridSize), out Size configuredGrid)
                    ? configuredGrid
                    : DesignerLayoutOptions.Default.GridSize;
                bool snapToGrid = TryGetOptionValue(optionService, nameof(WindowsFormsDesignerOptionService.SnapToGrid), out bool configuredSnap)
                    ? configuredSnap
                    : DesignerLayoutOptions.Default.SnapToGrid;
                bool useSnapLines = TryGetOptionValue(optionService, nameof(WindowsFormsDesignerOptionService.UseSnapLines), out bool configuredLines)
                    ? configuredLines
                    : DesignerLayoutOptions.Default.UseSnapLines;
                return DesignerLayoutOptions.Create(gridSize, snapToGrid, useSnapLines);
            }

            return DesignerLayoutOptions.Default;
        }

        private static bool TryGetOptionValue<T>(IDesignerOptionService service, string name, out T value)
        {
            object? configured = service.GetOptionValue("WindowsFormsDesigner", name);
            if (configured is T typed)
            {
                value = typed;
                return true;
            }

            value = default!;
            return false;
        }

        private static Rectangle CalculateRawBounds(
            Rectangle initialBounds,
            PortableDesignerPointerOperation operation,
            int deltaX,
            int deltaY,
            int minimumWidth,
            int minimumHeight)
        {
            int left = initialBounds.Left;
            int top = initialBounds.Top;
            int right = initialBounds.Right;
            int bottom = initialBounds.Bottom;

            if (ChangesLeft(operation))
                left = Math.Min(left + deltaX, right - minimumWidth);
            else if (ChangesRight(operation))
                right = Math.Max(right + deltaX, left + minimumWidth);

            if (ChangesTop(operation))
                top = Math.Min(top + deltaY, bottom - minimumHeight);
            else if (ChangesBottom(operation))
                bottom = Math.Max(bottom + deltaY, top + minimumHeight);

            if (operation == PortableDesignerPointerOperation.Move)
            {
                left += deltaX;
                right += deltaX;
                top += deltaY;
                bottom += deltaY;
            }

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static Rectangle ApplyAdjustments(
            Rectangle bounds,
            PortableDesignerPointerOperation operation,
            int adjustmentX,
            int adjustmentY,
            int minimumWidth,
            int minimumHeight)
        {
            if (operation == PortableDesignerPointerOperation.Move)
            {
                bounds.Offset(adjustmentX, adjustmentY);
                return bounds;
            }

            int left = bounds.Left;
            int top = bounds.Top;
            int right = bounds.Right;
            int bottom = bounds.Bottom;
            if (ChangesLeft(operation))
                left = Math.Min(left + adjustmentX, right - minimumWidth);
            else if (ChangesRight(operation))
                right = Math.Max(right + adjustmentX, left + minimumWidth);
            if (ChangesTop(operation))
                top = Math.Min(top + adjustmentY, bottom - minimumHeight);
            else if (ChangesBottom(operation))
                bottom = Math.Max(bottom + adjustmentY, top + minimumHeight);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static bool CandidateLineMovesWithOperation(
            SnapLine line,
            PortableDesignerPointerOperation operation)
        {
            if (operation == PortableDesignerPointerOperation.Move)
                return true;
            if (line.IsVertical)
            {
                if (ChangesLeft(operation))
                    return !IsRightAnchored(line);
                return ChangesRight(operation) && IsRightAnchored(line);
            }

            if (ChangesTop(operation))
                return !IsBottomAnchored(line);
            return ChangesBottom(operation) && IsBottomAnchored(line);
        }

        private static bool IsRightAnchored(SnapLine line)
        {
            return line.SnapLineType == SnapLineType.Right
                || string.Equals(line.Filter, SnapLine.MarginRight, StringComparison.Ordinal)
                || string.Equals(line.Filter, SnapLine.PaddingRight, StringComparison.Ordinal);
        }

        private static bool IsBottomAnchored(SnapLine line)
        {
            return line.SnapLineType == SnapLineType.Bottom
                || string.Equals(line.Filter, SnapLine.MarginBottom, StringComparison.Ordinal)
                || string.Equals(line.Filter, SnapLine.PaddingBottom, StringComparison.Ordinal);
        }

        private static int ChooseAxisAdjustment(SnapAdjustment first, SnapAdjustment second)
        {
            if (!first.Found)
                return second.Value;
            if (!second.Found)
                return first.Value;
            return Math.Abs(first.Value) <= Math.Abs(second.Value) ? first.Value : second.Value;
        }

        private static Rectangle CreateBounds(Point start, Point end)
        {
            return Rectangle.FromLTRB(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Max(start.X, end.X),
                Math.Max(start.Y, end.Y));
        }

        private static int SnapCoordinate(int value, int gridSize)
        {
            long quotient = Math.DivRem((long)value, gridSize, out long remainder);
            if (remainder < 0)
            {
                quotient--;
                remainder += gridSize;
            }

            long lower = quotient * gridSize;
            long snapped = remainder > gridSize / 2 ? lower + gridSize : lower;
            return (int)Math.Clamp(snapped, int.MinValue, int.MaxValue);
        }

        private static bool IsAltPressed()
        {
            return (Control.ModifierKeys & Keys.Alt) == Keys.Alt;
        }

        internal static bool ChangesLocation(PortableDesignerPointerOperation operation)
        {
            return operation == PortableDesignerPointerOperation.Move || ChangesLeft(operation) || ChangesTop(operation);
        }

        internal static bool ChangesSize(PortableDesignerPointerOperation operation)
        {
            return operation != PortableDesignerPointerOperation.None
                && operation != PortableDesignerPointerOperation.Move;
        }

        internal static bool ChangesLeft(PortableDesignerPointerOperation operation)
        {
            return operation is PortableDesignerPointerOperation.ResizeLeft
                or PortableDesignerPointerOperation.ResizeTopLeft
                or PortableDesignerPointerOperation.ResizeBottomLeft;
        }

        internal static bool ChangesTop(PortableDesignerPointerOperation operation)
        {
            return operation is PortableDesignerPointerOperation.ResizeTop
                or PortableDesignerPointerOperation.ResizeTopLeft
                or PortableDesignerPointerOperation.ResizeTopRight;
        }

        internal static bool ChangesRight(PortableDesignerPointerOperation operation)
        {
            return operation is PortableDesignerPointerOperation.ResizeRight
                or PortableDesignerPointerOperation.ResizeTopRight
                or PortableDesignerPointerOperation.ResizeBottomRight;
        }

        internal static bool ChangesBottom(PortableDesignerPointerOperation operation)
        {
            return operation is PortableDesignerPointerOperation.ResizeBottom
                or PortableDesignerPointerOperation.ResizeBottomLeft
                or PortableDesignerPointerOperation.ResizeBottomRight;
        }

        private readonly struct SnapLineSnapshot
        {
            internal SnapLineSnapshot(SnapLine line, int offset)
            {
                Line = line;
                Offset = offset;
            }

            internal SnapLine Line { get; }

            internal int Offset { get; }
        }

        private readonly struct SnapAdjustment
        {
            internal SnapAdjustment(bool found, int value)
            {
                Found = found;
                Value = value;
            }

            internal bool Found { get; }

            internal int Value { get; }
        }

        private readonly struct DesignerLayoutOptions
        {
            internal static DesignerLayoutOptions Default { get; } = new(new Size(8, 8), snapToGrid: true, useSnapLines: false);

            private DesignerLayoutOptions(Size gridSize, bool snapToGrid, bool useSnapLines)
            {
                GridSize = gridSize;
                SnapToGrid = snapToGrid;
                UseSnapLines = useSnapLines;
            }

            internal Size GridSize { get; }

            internal bool SnapToGrid { get; }

            internal bool UseSnapLines { get; }

            internal static DesignerLayoutOptions Create(Size gridSize, bool snapToGrid, bool useSnapLines)
            {
                return new DesignerLayoutOptions(
                    new Size(
                        Math.Clamp(gridSize.Width, MinGridSize, MaxGridSize),
                        Math.Clamp(gridSize.Height, MinGridSize, MaxGridSize)),
                    snapToGrid,
                    useSnapLines);
            }
        }
    }
}
