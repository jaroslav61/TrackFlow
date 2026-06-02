using Avalonia.Controls;
using Avalonia.Media;

namespace TrackFlow.Views.Editor.Markers
{
    /// <summary>
    /// 5-stavové návestidlo marker pre editor (24×48px zaberajúci 1×2 bunky).
    /// Implementuje IMarkerAngle pre rotáciu okolo stredu hornej bunky (12, 12).
    /// </summary>
    public partial class MarkerSignalLarge : UserControl, IMarkerAngle
    {
        private const double CellSize = 24.0;
        // Os rotácie: stred hornej bunky (24×24px)
        private const double RotationCenterX = CellSize / 2;  // 12
        private const double RotationCenterY = CellSize / 2;  // 12

        public MarkerSignalLarge()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Aplikuje rotáciu okolo stredu hornej bunky (12, 12).
        /// Rotácia je kvantovaná na 0, 90, 180, 270 stupňov.
        /// </summary>
        public void SetAngle(int angle)
        {
            // Normalizuj uhol na 0-359
            angle = angle % 360;
            if (angle < 0) angle += 360;

            // Kvantizácia na 90 stupňoch
            int normalizedAngle = (angle + 45) / 90 * 90;
            normalizedAngle = normalizedAngle % 360;

            // Marker je 24×48; pri 90/270 sa hosť mení na 48×24, preto sa
            // os otáčania líši pre každú rotáciu, aby marker padol do hosta.
            this.RenderTransform = normalizedAngle switch
            {
                90  => new RotateTransform(90,  CellSize,        CellSize),        // (24, 24)
                180 => new RotateTransform(180, CellSize / 2.0,  CellSize),        // (12, 24)
                270 => new RotateTransform(270, CellSize / 2.0,  CellSize / 2.0),  // (12, 12)
                _   => null!,
            };
        }
    }
}

