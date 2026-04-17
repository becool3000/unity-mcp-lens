using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    /// <summary>
    /// Provides helper methods for parsing Vector3 values from JSON data received from MCP clients.
    /// </summary>
    /// <remarks>
    /// Use this when MCP tools need to accept position, scale, or direction parameters as arrays.
    /// MCP clients typically send vectors as JSON arrays: [x, y, z]
    /// </remarks>
    public static class Vector3Helper
    {
        /// <summary>
        /// Parses a Newtonsoft.Json.Linq.JArray into a Unity Vector3.
        /// Expected format: [x, y, z] where each element is a number.
        /// </summary>
        /// <param name="array">JSON array containing exactly 3 numeric elements representing x, y, z coordinates</param>
        /// <returns>A Vector3 constructed from the array values</returns>
        /// <exception cref="System.Exception">Thrown if array is null, does not contain exactly 3 elements, or elements cannot be converted to floats</exception>
        /// <example>
        /// <code>
        /// [McpTool("move_object", "Moves an object")]
        /// public static object MoveObject(JObject params)
        /// {
        ///     var positionArray = params["position"] as JArray;
        ///     var position = Vector3Helper.ParseVector3(positionArray);
        ///
        ///     // Use the parsed Vector3
        ///     targetObject.transform.position = position;
        ///     return Response.Success("Object moved");
        /// }
        /// </code>
        /// </example>
        public static Vector3 ParseVector3(JArray array)
        {
            if (array == null || array.Count != 3)
                throw new System.Exception("Vector3 must be an array of 3 floats [x, y, z].");
            return new Vector3((float)array[0], (float)array[1], (float)array[2]);
        }
    }
}

