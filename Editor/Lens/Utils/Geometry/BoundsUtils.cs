using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Utils.Geometry
{
    static class BoundsUtils
    {
        public static Vector3[] GetCorners(this Bounds bounds)
        {
            var center = bounds.center;
            var extents = bounds.extents;
            
            return new Vector3[]
            {
                center + new Vector3(extents.x, extents.y, extents.z),
                center + new Vector3(extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, extents.y, extents.z),
                center + new Vector3(-extents.x, extents.y, -extents.z),
                center + new Vector3(extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y, extents.z),
                center + new Vector3(-extents.x, -extents.y, -extents.z)
            };
        }
    }
}
