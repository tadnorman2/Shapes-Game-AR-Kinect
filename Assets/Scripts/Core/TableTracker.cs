using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// Follows the table through the Kinect: finds it once with <see cref="Calibrate"/>, then keeps
    /// a live map of the real objects standing on it from every depth image.
    /// </summary>
    public sealed class TableTracker
    {
        public TableSurface Surface { get; private set; }
        public ObstacleMap Obstacles { get; private set; }

        public bool IsCalibrated => Surface != null;

        /// <summary>
        /// Finds the table in <paramref name="frame"/>, which should show it clear. Returns whether
        /// it was found; if not, any previous calibration is kept.
        /// </summary>
        public bool Calibrate(DepthFrame frame)
        {
            var surface = TableSurface.Find(frame);
            if (surface == null) return false;
            Surface = surface;
            Obstacles = new ObstacleMap(surface.Grid);
            return true;
        }

        /// <summary>Updates the object map from <paramref name="frame"/>, using every <paramref name="step"/>th pixel each way.</summary>
        public void Update(DepthFrame frame, int step = 1)
        {
            if (Surface == null) return;
            var tableFrame = Surface.Frame;
            Obstacles.BeginFrame();
            for (var y = 0; y < frame.Height; y += step)
            {
                for (var x = 0; x < frame.Width; x += step)
                {
                    var point = frame[x, y];
                    if (!DepthFrame.IsValid(point)) continue;
                    var t = tableFrame.ToTable(point);
                    // Only things on the table matter; walls and furniture past its edge don't.
                    if (Surface.Contains(t.X, t.Z)) Obstacles.Observe(t.X, t.Z, t.Y);
                }
            }
            Obstacles.EndFrame();
        }

        /// <summary>Where the ball can go: the table, minus the objects on it.</summary>
        public IPlayArea PlayArea()
        {
            var surface = Surface;
            var obstacles = Obstacles;
            return new PlayArea((x, z) => surface.Contains(x, z) && !obstacles.IsBlocked(x, z));
        }

        /// <summary>Converts a camera-space point, like a tracked hand, to table coordinates.</summary>
        public Vector3 ToTable(Vector3 camera) => Surface.Frame.ToTable(camera);
    }
}
