using System;
using System.ComponentModel;
using UnityEngine;

namespace Unity.AI.Toolkit.Utility
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    static class MenuItems
    {
        public const string animationMenuItem = "Assets/Create/Animation/Generate Animation Clip";
        public const string materialMenuItem = "Assets/Create/Rendering/Generate Material";
        public const string meshButtonItem = "Assets/Create/3D/Generate 3D Object";
        public const string soundMenuItem = "Assets/Create/Audio/Generate Audio Clip";
        public const string spriteMenuItem = "Assets/Create/Rendering/Generate Sprite"; // use this one, and not the one in 2D/ as it is not guaranteed to exist
        public const string textureMenuItem = "Assets/Create/Rendering/Generate Texture 2D";
        public const string cubemapMenuItem = "Assets/Create/Rendering/Generate Cubemap";
    }
}
