namespace FocusKey.Foundation.Overlay;

public readonly record struct ScreenPoint(int X, int Y);

public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public static class OverlayPositionHelper
{
    public static ScreenPoint CalculateInitialCenter(int windowWidth, int windowHeight, ScreenRect targetWorkArea) =>
        new(
            targetWorkArea.X + (targetWorkArea.Width - windowWidth) / 2,
            targetWorkArea.Y + (targetWorkArea.Height - windowHeight) / 2);

    public static ScreenPoint ClampToWorkAreas(int x, int y, int windowWidth, int windowHeight, IReadOnlyList<ScreenRect> workAreas)
    {
        if (workAreas is null || workAreas.Count == 0)
        {
            return new ScreenPoint(x, y);
        }

        int centerX = x + windowWidth / 2;
        int centerY = y + windowHeight / 2;

        ScreenRect best = workAreas[0];
        long maxOverlap = -1;
        long minDistanceSq = long.MaxValue;
        bool foundContaining = false;

        for (int i = 0; i < workAreas.Count; i++)
        {
            ScreenRect area = workAreas[i];
            bool containsCenter = centerX >= area.X && centerX < area.Right &&
                                  centerY >= area.Y && centerY < area.Bottom;

            int overlapW = Math.Max(0, Math.Min(x + windowWidth, area.Right) - Math.Max(x, area.X));
            int overlapH = Math.Max(0, Math.Min(y + windowHeight, area.Bottom) - Math.Max(y, area.Y));
            long overlap = (long)overlapW * overlapH;

            int dx = Math.Max(0, Math.Max(area.X - centerX, centerX - area.Right));
            int dy = Math.Max(0, Math.Max(area.Y - centerY, centerY - area.Bottom));
            long distSq = (long)dx * dx + (long)dy * dy;

            if (containsCenter)
            {
                if (!foundContaining || overlap > maxOverlap)
                {
                    foundContaining = true;
                    best = area;
                    maxOverlap = overlap;
                    minDistanceSq = distSq;
                }
            }
            else if (!foundContaining)
            {
                if (overlap > 0 && overlap > maxOverlap)
                {
                    best = area;
                    maxOverlap = overlap;
                    minDistanceSq = distSq;
                }
                else if (maxOverlap <= 0 && distSq < minDistanceSq)
                {
                    best = area;
                    minDistanceSq = distSq;
                }
            }
        }

        int clampedX = Math.Clamp(x, best.X, Math.Max(best.X, best.Right - windowWidth));
        int clampedY = Math.Clamp(y, best.Y, Math.Max(best.Y, best.Bottom - windowHeight));
        return new ScreenPoint(clampedX, clampedY);
    }

    public static bool IsPositionValid(int x, int y, int windowWidth, int windowHeight, IReadOnlyList<ScreenRect> workAreas)
    {
        if (workAreas is null || workAreas.Count == 0 || windowWidth <= 0 || windowHeight <= 0)
        {
            return false;
        }

        int headerHeight = Math.Min(windowHeight, 32);
        int minOverlapWidth = Math.Min(windowWidth, 32);
        int minOverlapHeight = Math.Min(headerHeight, 16);

        for (int i = 0; i < workAreas.Count; i++)
        {
            ScreenRect area = workAreas[i];
            int overlapX1 = Math.Max(x, area.X);
            int overlapX2 = Math.Min(x + windowWidth, area.Right);
            int overlapY1 = Math.Max(y, area.Y);
            int overlapY2 = Math.Min(y + headerHeight, area.Bottom);

            int overlapWidth = overlapX2 - overlapX1;
            int overlapHeight = overlapY2 - overlapY1;

            if (overlapWidth >= minOverlapWidth && overlapHeight >= minOverlapHeight)
            {
                return true;
            }
        }

        return false;
    }
}
