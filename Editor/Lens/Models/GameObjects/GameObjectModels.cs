#nullable disable
using System;
using System.Collections.Generic;

namespace Becool.UnityMcpLens.Editor.Models.GameObjects
{
    sealed class Vector3Value
    {
        const float ComparisonEpsilon = 0.0001f;

        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }

        public bool SameValue(Vector3Value other)
        {
            return other != null
                && Math.Abs(x - other.x) <= ComparisonEpsilon
                && Math.Abs(y - other.y) <= ComparisonEpsilon
                && Math.Abs(z - other.z) <= ComparisonEpsilon;
        }
    }

    sealed class GameObjectTargetRef
    {
        public string text { get; set; }
        public bool isInteger { get; set; }
        public bool isNull { get; set; }
        public bool isEmptyString { get; set; }
    }

    sealed class GameObjectQueryRequest
    {
        public GameObjectTargetRef target { get; set; }
        public string searchMethod { get; set; }
        public string searchTerm { get; set; }
        public bool findAll { get; set; }
        public bool searchInChildren { get; set; }
        public bool searchInactive { get; set; }
    }

    sealed class GameObjectBoundsRequest
    {
        public GameObjectTargetRef target { get; set; }
        public string searchMethod { get; set; }
    }

    sealed class GameObjectSimpleModifyRequest
    {
        public GameObjectTargetRef target { get; set; }
        public string searchMethod { get; set; }
        public string name { get; set; }
        public bool hasSetActive { get; set; }
        public bool setActive { get; set; }
        public bool hasTag { get; set; }
        public string tag { get; set; }
        public string layer { get; set; }
        public Vector3Value position { get; set; }
        public string positionType { get; set; }
        public Vector3Value rotation { get; set; }
        public Vector3Value scale { get; set; }
        public bool hasParent { get; set; }
        public GameObjectTargetRef parent { get; set; }
    }

    sealed class GameObjectComponentListRequest
    {
        public GameObjectTargetRef target { get; set; }
        public string searchMethod { get; set; }
        public bool searchInactive { get; set; }
    }

    sealed class GameObjectComponentGetRequest
    {
        public GameObjectTargetRef target { get; set; }
        public string searchMethod { get; set; }
        public bool searchInactive { get; set; }
        public string componentName { get; set; }
        public int? componentIndex { get; set; }
        public bool includeNonPublicSerialized { get; set; }
    }

    sealed class GameObjectTransformInfo
    {
        public Vector3Value position { get; set; }
        public Vector3Value localPosition { get; set; }
        public Vector3Value rotation { get; set; }
        public Vector3Value localRotation { get; set; }
        public Vector3Value scale { get; set; }
        public Vector3Value forward { get; set; }
        public Vector3Value up { get; set; }
        public Vector3Value right { get; set; }
    }

    sealed class GameObjectInfo
    {
        public string name { get; set; }
        public string id { get; set; }
        public object instanceID { get; set; }
        public string tag { get; set; }
        public int layer { get; set; }
        public bool activeSelf { get; set; }
        public bool activeInHierarchy { get; set; }
        public bool isStatic { get; set; }
        public string scenePath { get; set; }
        public GameObjectTransformInfo transform { get; set; }
        public Vector3Value center { get; set; }
        public Vector3Value extents { get; set; }
        public Vector3Value size { get; set; }
        public string parentId { get; set; }
        public object parentInstanceID { get; set; }
        public List<string> componentNames { get; set; }
    }

    sealed class GameObjectSelectionResult
    {
        public int count { get; set; }
        public GameObjectInfo[] objects { get; set; }
    }

    sealed class GameObjectBoundsInfo
    {
        public string target { get; set; }
        public string id { get; set; }
        public object instanceID { get; set; }
        public bool hasRendererOrColliderBounds { get; set; }
        public Vector3Value center { get; set; }
        public Vector3Value size { get; set; }
        public Vector3Value extents { get; set; }
        public int rendererCount { get; set; }
        public int colliderCount { get; set; }
    }

    sealed class GameObjectMutableState
    {
        public string name { get; set; }
        public string tag { get; set; }
        public int layer { get; set; }
        public bool activeSelf { get; set; }
        public Vector3Value localPosition { get; set; }
        public Vector3Value localRotation { get; set; }
        public Vector3Value localScale { get; set; }
    }

    sealed class GameObjectTargetSummary
    {
        public string name { get; set; }
        public string id { get; set; }
        public object instanceID { get; set; }
        public string path { get; set; }
        public string scenePath { get; set; }
    }

    sealed class GameObjectInspectResult
    {
        public int count { get; set; }
        public List<GameObjectInfo> objects { get; set; }
    }

    sealed class GameObjectComponentInfo
    {
        public string id { get; set; }
        public object instanceID { get; set; }
        public int index { get; set; }
        public string typeName { get; set; }
        public string shortTypeName { get; set; }
        public string name { get; set; }
        public bool? enabled { get; set; }
        public bool missing { get; set; }
    }

    sealed class GameObjectComponentListResult
    {
        public GameObjectTargetSummary target { get; set; }
        public int count { get; set; }
        public List<GameObjectComponentInfo> components { get; set; }
    }

    sealed class GameObjectComponentGetResult
    {
        public GameObjectTargetSummary target { get; set; }
        public GameObjectComponentInfo component { get; set; }
        public object data { get; set; }
    }

    sealed class GameObjectChangeEntry
    {
        public string field { get; set; }
        public object before { get; set; }
        public object after { get; set; }
    }

    sealed class ValidationMessage
    {
        public string severity { get; set; }
        public string code { get; set; }
        public string message { get; set; }
    }

    sealed class GameObjectChangePreviewResult
    {
        public GameObjectTargetSummary target { get; set; }
        public bool willModify { get; set; }
        public List<GameObjectChangeEntry> changes { get; set; }
        public List<ValidationMessage> validationMessages { get; set; }
        public GameObjectInfo @object { get; set; }
    }

    sealed class GameObjectChangeApplyResult
    {
        public GameObjectTargetSummary target { get; set; }
        public bool applied { get; set; }
        public List<GameObjectChangeEntry> changes { get; set; }
        public GameObjectInfo @object { get; set; }
    }

    sealed class GameObjectOperationResult
    {
        public bool success { get; set; }
        public string message { get; set; }
        public object data { get; set; }
        public string errorKind { get; set; }
        public object errorData { get; set; }

        public static GameObjectOperationResult Ok(string message, object data = null)
        {
            return new GameObjectOperationResult
            {
                success = true,
                message = message,
                data = data
            };
        }

        public static GameObjectOperationResult Error(string message, string errorKind = null, object errorData = null)
        {
            return new GameObjectOperationResult
            {
                success = false,
                message = message,
                errorKind = errorKind,
                errorData = errorData
            };
        }
    }
}
