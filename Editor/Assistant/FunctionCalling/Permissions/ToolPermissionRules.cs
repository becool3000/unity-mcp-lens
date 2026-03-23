using System;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor
{
    static class ToolPermissionRules
    {
        public struct PermissionCheck
        {
            public string MethodName;
            public Enum ExpectedOperation;
        }

        public struct CheckedApi
        {
            public string TypeName;
            public string MethodName;
            public PermissionCheck[] ExpectedPermissions;
        }

        public static readonly CheckedApi[] CheckedApis = new[]
        {
            // --- File System Operations ---
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "Delete",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Delete
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "WriteAllText",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "WriteAllTextAsync",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "WriteAllLines",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "WriteAllLinesAsync",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "AppendAllText",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "AppendAllTextAsync",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "ReadAllText",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Read
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "ReadAllTextAsync",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Read
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "ReadAllLines",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Read
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "ReadAllLinesAsync",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Read
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "ReadAllBytes",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Read
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "ReadAllBytesAsync",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Read
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "OpenRead",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Read
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "OpenWrite",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "Create",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckAssetGeneration),
                        ExpectedOperation = null
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "Copy",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "CopyAsync",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.File",
                MethodName = "Move",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.Directory",
                MethodName = "Delete",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Delete
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.Directory",
                MethodName = "CreateDirectory",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckAssetGeneration),
                        ExpectedOperation = null
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.Directory",
                MethodName = "GetFiles",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Read
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.Directory",
                MethodName = "GetDirectories",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Read
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.Directory",
                MethodName = "EnumerateFiles",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Read
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.Directory",
                MethodName = "EnumerateDirectories",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Read
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.FileInfo",
                MethodName = "Delete",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Delete
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.FileInfo",
                MethodName = "MoveTo",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.FileInfo",
                MethodName = "CopyTo",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.DirectoryInfo",
                MethodName = "Delete",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Delete
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.IO.DirectoryInfo",
                MethodName = "Create",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckAssetGeneration),
                        ExpectedOperation = null
                    }
                }
            },

            // --- UnityEngine.Object Operations ---
            new CheckedApi
            {
                TypeName = "UnityEngine.Object",
                MethodName = "Destroy",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Delete
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEngine.Object",
                MethodName = "DestroyImmediate",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Delete
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEngine.Object",
                MethodName = "Instantiate",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckAssetGeneration),
                        ExpectedOperation = null
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEditor.AssetDatabase",
                MethodName = "DeleteAsset",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Delete
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Delete
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEditor.AssetDatabase",
                MethodName = "CreateAsset",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckAssetGeneration),
                        ExpectedOperation = null
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEditor.AssetDatabase",
                MethodName = "ImportAsset",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Create
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckAssetGeneration),
                        ExpectedOperation = null
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEditor.AssetDatabase",
                MethodName = "MoveAsset",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEditor.AssetDatabase",
                MethodName = "MoveAssetToTrash",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Delete
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Delete
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEditor.AssetDatabase",
                MethodName = "SaveAssets",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEngine.GameObject",
                MethodName = "AddComponent",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEditor.SerializedObject",
                MethodName = "ApplyModifiedProperties",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    },
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckFileSystemAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },

            // --- Code Execution ---
            new CheckedApi
            {
                TypeName = "System.Reflection.Emit.DynamicMethod",
                MethodName = "Invoke",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckCodeExecution),
                        ExpectedOperation = null
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.CodeDom.Compiler.CompilerResults",
                MethodName = "get_CompiledAssembly",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckCodeExecution),
                        ExpectedOperation = null
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.Reflection.Assembly",
                MethodName = "CreateInstance",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckCodeExecution),
                        ExpectedOperation = null
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.Reflection.MethodInfo",
                MethodName = "Invoke",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckCodeExecution),
                        ExpectedOperation = null
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "System.Activator",
                MethodName = "CreateInstance",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckCodeExecution),
                        ExpectedOperation = null
                    }
                }
            },

            // --- Screen Capture ---
            new CheckedApi
            {
                TypeName = "UnityEngine.ScreenCapture",
                MethodName = "CaptureScreenshot",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckScreenCapture),
                        ExpectedOperation = null
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEngine.ScreenCapture",
                MethodName = "CaptureScreenshotAsTexture",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckScreenCapture),
                        ExpectedOperation = null
                    }
                }
            },

            // --- Unity Editor Operations ---
            new CheckedApi
            {
                TypeName = "UnityEditor.Undo",
                MethodName = "DestroyObjectImmediate",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Delete
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEditor.Undo",
                MethodName = "AddComponent",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEditor.EditorApplication",
                MethodName = "EnterPlaymode",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckPlayMode),
                        ExpectedOperation = IToolPermissions.PlayModeOperation.Enter
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEditor.EditorApplication",
                MethodName = "ExitPlaymode",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckPlayMode),
                        ExpectedOperation = IToolPermissions.PlayModeOperation.Exit
                    }
                }
            },

            // --- Unity Project Settings ---
            new CheckedApi
            {
                TypeName = "UnityEditor.PlayerSettings",
                MethodName = "SetScriptingDefineSymbolsForGroup",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
            new CheckedApi
            {
                TypeName = "UnityEditor.EditorBuildSettings",
                MethodName = "set_scenes",
                ExpectedPermissions = new[]
                {
                    new PermissionCheck
                    {
                        MethodName = nameof(ToolCallPermissions.CheckUnityObjectAccess),
                        ExpectedOperation = IToolPermissions.ItemOperation.Modify
                    }
                }
            },
        };
    }
}
