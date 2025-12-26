using System;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component for waypoint-based NPC movement.
    /// Defines a path of tile positions for an NPC to follow.
    /// Pure data component - no methods.
    /// </summary>
    public struct MovementRoute
    {
        /// <summary>
        /// Array of waypoint positions that the NPC will walk through.
        /// Each point is in tile coordinates (not pixels).
        /// </summary>
        public Point[] Waypoints { get; set; }

        /// <summary>
        /// Whether to loop back to the first waypoint after reaching the last.
        /// If false, NPC stops at the last waypoint.
        /// </summary>
        public bool Loop { get; set; }

        /// <summary>
        /// How long to wait (in seconds) when reaching a waypoint before moving to the next.
        /// </summary>
        public float WaypointWaitTime { get; set; }

        /// <summary>
        /// Initializes a new instance of the MovementRoute struct.
        /// </summary>
        /// <param name="waypoints">Array of waypoint positions in tile coordinates.</param>
        /// <param name="loop">Whether to loop back to the first waypoint.</param>
        /// <param name="waypointWaitTime">How long to wait at each waypoint in seconds.</param>
        public MovementRoute(Point[] waypoints, bool loop = true, float waypointWaitTime = 1.0f)
        {
            Waypoints = waypoints ?? throw new ArgumentNullException(nameof(waypoints));
            Loop = loop;
            WaypointWaitTime = waypointWaitTime;
        }
    }
}
