using System;
using TrackFlow.Models.Layout;
using TrackFlow.Services;

namespace TrackFlow.Extensions;

/// <summary>
/// Extension methods pre BlockElement - výpočty rozmerov a hit testing.
/// </summary>
public static class BlockElementExtensions
{
    /// <summary>
    /// Získa šírku bloku v pixeloch podľa rotácie a počtu buniek.
    /// </summary>
    public static double GetWidthInPixels(this BlockElement block, double cellSize = 24.0)
    {
        int length = Math.Clamp(block.BlockLengthCells, 1, 20);
        bool isHorizontal = block.Rotation == 0 || block.Rotation == 180;
        return isHorizontal ? length * cellSize : cellSize;
    }
    
    /// <summary>
    /// Získa výšku bloku v pixeloch podľa rotácie a počtu buniek.
    /// </summary>
    public static double GetHeightInPixels(this BlockElement block, double cellSize = 24.0)
    {
        int length = Math.Clamp(block.BlockLengthCells, 1, 20);
        bool isVertical = block.Rotation == 90 || block.Rotation == 270;
        return isVertical ? length * cellSize : cellSize;
    }
    
    /// <summary>
    /// Overí či bod [x, y] je v rámci bloku (hit testing).
    /// </summary>
    public static bool ContainsPoint(this BlockElement block, double x, double y, double cellSize = 24.0)
    {
        double width = block.GetWidthInPixels(cellSize);
        double height = block.GetHeightInPixels(cellSize);
        
        return x >= block.X && x < block.X + width 
            && y >= block.Y && y < block.Y + height;
    }

    /// <summary>
    /// Striktná validácia: vráti true, ak je daný smer jazdy v bloku povolený.
    /// - pre horizontálny blok: Right = forward, Left = backward
    /// - pre vertikálny blok: Down = forward, Up = backward
    /// Ostatné smery sú pre danú orientáciu neplatné a vracajú false.
    /// </summary>
    public static bool IsTravelDirectionAllowed(this BlockElement block, NavigationDirection travelDirection)
    {
        if (block == null)
            return false;

        var rotation = LayoutElementFootprintHelper.NormalizeRightAngle(block.Rotation);
        bool isVertical = rotation is 90 or 270;

        if (!isVertical)
        {
            return travelDirection switch
            {
                NavigationDirection.Right => block.AllowForward,
                NavigationDirection.Left => block.AllowBackward,
                _ => false
            };
        }

        return travelDirection switch
        {
            NavigationDirection.Down => block.AllowForward,
            NavigationDirection.Up => block.AllowBackward,
            _ => false
        };
    }
}

