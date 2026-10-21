namespace Loggepector.Models;

public sealed class MapLocation(int x, int y, int z)
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public int Z { get; } = z;
}
